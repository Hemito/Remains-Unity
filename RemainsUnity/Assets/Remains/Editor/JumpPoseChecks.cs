using System;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class JumpPoseChecks
    {
        public static void Run()
        {
            PonyAnimationData.Load();PonyVisual.Prepare();
            CheckPhysicalLanding();
            var actor=new ActorState {id="jump-pose-check",pose="jump",vy=7.2f};
            int rising=PonyMotionFrames.Sample(actor,0);
            Require(rising==PonyMotionFrames.Sample(actor,10),"Время меняет позу при неизменной скорости.");
            actor.vy=0;int apex=PonyMotionFrames.Sample(actor,0);
            actor.vy=-7.2f;int falling=PonyMotionFrames.Sample(actor,0);
            Require(rising<apex && apex<falling,"Порядок подъём/вершина/падение нарушен.");
            Require(falling==PonyMotionFrames.Sample(actor.Copy(),100),"Сетевая копия меняет позу.");
            var root=new GameObject("Проверка позы прыжка");
            try
            {
                var visual=root.AddComponent<PonyVisual>();visual.Bind(actor);visual.DrawPose(100);
                Require(visual.DrawnFrame==falling,"Изображение использует другую позу.");
                Require(Anatomy.TryRegion(actor,"head",out var before,out _),"Нет области головы.");
                actor.poseTime=100;
                Require(Anatomy.TryRegion(actor,"head",out var after,out _) && before==after,"Область попадания зависит от часов анимации.");
                actor.vy=-100;Require(PonyMotionFrames.Sample(actor,0)==31,"Падение оборачивается к подъёму.");
                actor.vy=100;Require(PonyMotionFrames.Sample(actor,0)==0,"Подъём выходит за библиотеку.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            actor.pose="jump";actor.poseTime=3;actor.vy=0;
            StanceAnimation.Select(actor,"idle",false);
            Require(actor.pose=="landing" && actor.poseTime==0,"Нет перехода приземления.");
            ActorPose.Advance(actor,.1f);StanceAnimation.Select(actor,"idle",false);
            Require(actor.pose=="landing","Приземление оборвалось раньше завершения.");
            ActorPose.Advance(actor,.2f);StanceAnimation.Select(actor,"idle",false);
            Require(actor.pose=="idle","Приземление зациклилось.");
            foreach(string target in new[]{"jump","walk","run","climb","crouch"})
            {
                actor.pose="landing";actor.poseTime=0;
                StanceAnimation.Select(actor,target,false);
                Require(actor.pose==target,"Приземление блокирует действие: "+target);
            }
            var raider=new ActorState {anatomy="raider",pose="jump"};
            StanceAnimation.Select(raider,"idle",false);
            Require(raider.pose=="idle","Рейдер получил отсутствующую позу пони.");
            Debug.Log("Позы прыжка: PASS — движение, сетевая копия, изображение и область попадания.");
        }
        private static void CheckPhysicalLanding()
        {
            var room=new RoomLayout {left=-10,right=10,bottom=-1,top=8,
                platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}},ladders=Array.Empty<RoomLadder>()};
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var actor=new ActorState {id="physical-landing",x=0,y=1,vy=-2,pose="fall"};
                var motion=new MovementController(room);
                for(int i=0;i<300 && actor.y>0;i++)
                {motion.Step(actor,Vector2.zero,false,dt);if(actor.y>0) ActorPose.Advance(actor,dt);}
                Require(actor.y==0 && actor.vy==0 && actor.pose=="landing","Физическое касание не запустило переход");
                var landed=actor.Copy();float elapsed=0;
                while(actor.pose=="landing" && elapsed<1)
                {
                    ActorPose.Advance(actor,dt);elapsed+=dt;motion.Step(actor,Vector2.zero,false,dt);
                    Require(actor.y==0 && actor.vy==0,"Переход приземления сдвинул тело с опоры");
                }
                float duration=Anatomy.PoseDuration(actor,"landing");
                Require(actor.pose=="idle" && elapsed>=duration-.0001f && elapsed<=duration+dt+.0001f,
                    "Реальный контроллер перезапускает или обрывает приземление");
                var jump=landed.Copy();new MovementController(room).Step(jump,Vector2.zero,true,dt);
                Require(jump.y>0 && jump.pose=="jump","Переход блокирует новый прыжок");
                var walk=landed.Copy();new MovementController(room).Step(walk,Vector2.right,false,dt);
                Require(walk.x>0 && walk.pose=="walk","Переход блокирует движение");
                Debug.Log($"Приземление после физического падения: dt={dt}, переход={elapsed}, клип={duration}, прыжок/ходьба доступны.");
            }
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Позы прыжка: "+message);}
    }
}
