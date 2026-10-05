using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class StandingSupportPoseChecks
    {
        public static void Run()
        {
            Anatomy.Load();PonyVisual.Prepare();StandingEdgePoseCatalog.Load();
            foreach(int fps in new[]{30,60,144}) foreach(int facing in new[]{-1,1}) foreach(bool crouched in new[]{false,true})
                CheckResolvedPose(fps,facing,crouched);
            CheckTransitionPriority();
            Debug.Log("Опора и краевой рисунок: PASS — реальный MovementController и PonyVisual, общий pose/кадр, неподвижное тело и sequence, стойка/приседание, обе стороны, 30/60/144 Гц, сомкнутые опоры и приоритет переходов. Габариты и правила устойчивости сохранены.");
        }
        private static RoomLayout Room(bool seam=false)=>new()
        {
            id="standing-support-check",left=-4,right=4,bottom=-1,top=5,ladders=Array.Empty<RoomLadder>(),
            platforms=seam?new[]{new RoomSurface {x=-4,y=-1,width=4,height=1},new RoomSurface {x=0,y=-1,width=4,height=1}}:
                new[]{new RoomSurface {x=-4,y=-1,width=4,height=1}}
        };
        private static void CheckResolvedPose(int fps,int facing,bool crouched)
        {
            var room=Room();
            var actor=new ActorState {id=$"standing-edge-{fps}-{facing}-{crouched}",player=true,connected=true,x=.45f,y=0,
                facing=facing,crouched=crouched,pose=crouched?"crouch":"idle"};
            var motion=new MovementController(room);var hull=Anatomy.Movement(actor);
            var view=new GameObject(actor.id).AddComponent<PonyVisual>();
            try
            {
                view.Bind(actor);motion.Step(actor,Vector2.zero,false,1f/fps);
                Require(StandingEdgePoseCatalog.TryGet(actor.anatomy,out var profile),"Нет профиля краевых поз.");
                var expected=StandingEdgePoseSelector.Select(profile,crouched,facing,.45f,new[]{new StandingSupportInterval(-4,0)});
                Require(expected.Outcome==StandingEdgePoseOutcome.Selected && actor.pose==expected.PoseId,"Контроллер не выбрал фактическую краевую позу.");
                uint sequence=actor.poseSequence;string pose=actor.pose;
                for(int tick=0;tick<20;tick++)
                {
                    ActorPose.Advance(actor,1f/fps);motion.Step(actor,Vector2.zero,false,1f/fps);view.DrawPose(actor.poseTime);
                    Require(actor.x==.45f && actor.y==0 && actor.vy==0 && !actor.climbing && actor.crouched==crouched,
                        "Краевой рисунок изменил физическое тело/стойку.");
                    Require(actor.pose==pose && actor.poseSequence==sequence && view.DrawnClip==pose && view.DrawnFrame==0 &&
                        Anatomy.TrySupportContacts(actor,out var contacts) && contacts.Count==4,"Поза сбрасывается каждый кадр или расходится с рисунком/контактами.");
                    Require(Anatomy.Movement(actor).width==hull.width && Anatomy.Movement(actor).height==hull.height,"Краевая поза изменила габарит движения.");
                }
                var seamed=Room(true);var onSeam=new ActorState {id="standing-seam",player=true,connected=true,x=.45f,facing=facing,
                    crouched=crouched,pose=crouched?"crouch":"idle"};
                new MovementController(seamed).Step(onSeam,Vector2.zero,false,1f/fps);
                Require(onSeam.pose==(crouched?"crouch":"idle"),"Стык двух настоящих опор объявлен обрывом.");
            }
            finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
        }
        private static void CheckTransitionPriority()
        {
            var room=Room();var selector=new StandingSupportPose();
            var actor=new ActorState {id="standing-landing",player=true,connected=true,x=.45f,pose="jump"};
            string landing=Anatomy.LandingPose(actor);
            StanceAnimation.Select(actor,"idle",false,selector,room);
            Require(actor.pose==landing,"Краевая стойка прервала приземление.");
            actor.poseTime=Anatomy.PoseDuration(actor,landing);StanceAnimation.Select(actor,"idle",false,selector,room);
            Require(actor.pose!="idle" && actor.pose!=landing,"После приземления краевой рисунок не выбран.");
            actor.crouched=true;StanceAnimation.Select(actor,"crouch",false,selector,room);
            Require(actor.pose=="crouchLower","Краевой рисунок прервал опускание в приседание.");
            actor.poseTime=PonyAnimationData.Duration("crouchLower");StanceAnimation.Select(actor,"crouch",true,selector,room);
            Require(actor.pose!="crouch" && actor.pose!="crouchLower","После приседания не выбран краевой рисунок.");
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
