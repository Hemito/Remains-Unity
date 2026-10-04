using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorClosureMotionChecks
    {
        public static void Run()
        {
            foreach(int direction in new[]{-1,1})
            foreach(int fps in new[]{30,144})
            {
                var floor=new RoomSurface {x=-10,y=-1,width=20,height=1};
                var room=new RoomLayout {left=-10,right=10,bottom=-1,top=10,platforms=new[]{floor},ladders=Array.Empty<RoomLadder>()};
                var door=new DoorState {id="late-door",definition="test",level="factory:0",surfaceTexture="floor",x=-.1f,y=0,width=.2f,height=3};
                var actor=new ActorState {id="crossing",x=0,y=0,connected=true,player=true};
                var motion=new MovementController(room);
                // The local actor has already entered when the closure snapshot arrives.
                room.platforms=DoorGeometry.Compose(new[]{floor},new[]{door},door.level);
                motion.Step(actor,Vector2.zero,false,1f/fps);
                Require(actor.Position==Vector2.zero,"Запоздалое закрытие переместило стоящего игрока.");
                for(int i=0;i<fps;i++) motion.Step(actor,Vector2.right*direction,false,1f/fps);
                Require(actor.x*direction>1,"Игрок не может выйти из поздно закрытого проёма.");
                for(int i=0;i<fps*2;i++) motion.Step(actor,Vector2.left*direction,false,1f/fps);
                Require(actor.x*direction>=.1f+Anatomy.Movement(actor).width*.5f-.001f,"Игрок повторно прошёл через закрытую дверь.");
                var prop=new PropState {id="crossing-prop",x=0,y=.3f};
                for(int i=0;i<fps;i++) PropMotion.Step(prop,room,1f/fps,new Vector2(direction*3,.3f));
                Require(prop.x*direction>1,"Предмет зажат поздним закрытием.");
                for(int i=0;i<fps*2;i++) PropMotion.Step(prop,room,1f/fps,new Vector2(-direction*3,.3f));
                Require(prop.x*direction>=.1f+prop.width*.5f-.001f,"Предмет повторно прошёл через закрытую дверь.");
            }
            Debug.Log("Позднее закрытие: PASS — без телепортации, выход в обе стороны и запрет повторного входа, игрок/предмет, 30/144 FPS.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
