using System;
using Remains.Domain;
using Remains.Content;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class LadderHoldChecks
    {
        public static void Run()
        {
            CheckAnimation();
            var room=RoomLayout.Load();
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var actor=new ActorState {id="ladder-hold",x=4,y=1};
                var motion=new MovementController(room);
                motion.Step(actor,Vector2.up,false,dt);
                Require(actor.climbing && actor.vy>0,"Вход на лестницу");
                float height=actor.y;
                for(int i=0;i<300;i++) motion.Step(actor,Vector2.zero,false,dt);
                Require(actor.climbing && actor.pose=="climb" && actor.y==height && actor.vy==0,"Удержание без ввода");
                float time=actor.poseTime;ActorPose.Advance(actor,1);
                Require(actor.poseTime==time,"Неподвижная поза не перебирает кадры");
                var copy=StateJson.Copy(actor);
                new MovementController(room).Step(copy,Vector2.zero,false,dt);
                Require(copy.climbing && copy.y==height,"Загрузка сохраняет захват лестницы");
                motion.Step(actor,Vector2.zero,true,dt);
                Require(!actor.climbing && actor.vy>0 && actor.pose=="jump","Прыжок отпускает лестницу");
                actor=new ActorState {id="ladder-side",x=4,y=1,climbing=true};
                for(int i=0;i<60;i++) motion.Step(actor,Vector2.right,false,dt);
                Require(!actor.climbing && actor.x>4.65f,"Боковой выход отпускает лестницу");
                actor=new ActorState {id="ladder-down",x=4,y=1,climbing=true,down=true};
                motion.Step(actor,Vector2.zero,false,dt);
                Require(!actor.climbing,"Нокдаун снимает захват лестницы");
                Debug.Log($"Удержание лестницы: PASS, dt={dt}, остановка, загрузка, прыжок и выход.");
            }
        }
        private static void CheckAnimation()
        {
            PonyAnimationData.Load();
            var actor=new ActorState {id="ladder-animation",climbing=true,pose="climb",vy=3.15f};
            actor.poseTime=.14f;
            int before=PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false);
            ActorPose.Advance(actor,1f/30);
            Require(PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false)==before+1,"Подъём продвигает цикл");
            actor.vy=-4.725f;ActorPose.Advance(actor,1f/30);
            Require(PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false)==before,"Смена направления возвращает прежнюю фазу");
            actor.poseTime=0;ActorPose.Advance(actor,.02f);
            Require(PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false)==12,"Спуск с начальной фазы проходит через конец цикла");
            var copy=StateJson.Copy(actor);ActorPose.Advance(actor,.05f);ActorPose.Advance(copy,.05f);
            Require(actor.poseTime==copy.poseTime && actor.poseTime<0,"Отрицательная фаза сохраняется без повторного старта");
            for(int i=0;i<120;i++)
            {
                ActorPose.Advance(actor,1f/60);
                int frame=PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false);
                Require(frame>=1 && frame<=12,"Неподвижный хват не входит в цикл спуска");
            }
            actor.vy=0;ActorPose.Advance(actor,.1f);
            Require(actor.poseTime==0 && PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false,ActorPose.StationaryClimb(actor))==0,
                "Остановка выбирает отдельный неподвижный хват");
            var atlas=new AtlasActorClip {count=13,framesPerSecond=30,loop=true};
            Require(atlas.FrameIndex(-.02f)==12 && atlas.FrameIndex(-.02f,true)==0,"Атлас поддерживает обратную фазу и покой");
            Require(PonyAnimationData.FrameIndex("crouchLower",-.1f,false)==0,"Незацикленный переход не оборачивается назад");
        }
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            WeaponCatalog.Load();PonyAnimationData.Load();Remains.Presentation.PonyVisual.Prepare();
            for(int row=0;row<3;row++)
                for(int column=0;column<4;column++)
                {
                    var actor=new ActorState {id=$"ladder-{row}-{column}",climbing=true,pose="climb",x=column*2.2f,y=-row*2.8f,
                        vy=row==0?3.15f:row==1?-4.725f:0,poseTime=row==0?column*.08f:row==1?-column*.08f:0};
                    var visual=new GameObject(actor.id).AddComponent<Remains.Presentation.PonyVisual>();
                    visual.Bind(actor);visual.DrawPose(actor.poseTime);
                }
            var camera=new GameObject("Камера лестничных поз").AddComponent<Camera>();camera.orthographic=true;
            camera.orthographicSize=4.7f;camera.transform.position=new Vector3(3.3f,-2,-10);camera.backgroundColor=new Color(.18f,.2f,.22f);
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../..", ".reference/tests/ladder-poses.png"));
            Remains.Runtime.NetworkFrameCapture.Save(camera,path);
            Debug.Log("Проверочный кадр лестницы: "+path);
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Проверка лестницы: "+reason);}
    }
}
