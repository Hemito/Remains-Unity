using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Runtime;
using UnityEngine;
namespace Remains.Editor
{
    public static class ProbeRouteMotionChecks
    {
        public static void Run()
        {
            Anatomy.Load();FootSupportCatalog.Load();var settings=MovementSettings.Load();
            // Exact old bang-bang/coast controller: reproduce the observed x25.6↔26.2 limit cycle at7Hz.
            var old=new ActorState {x=25.6f};float dt=1f/7;
            for(int i=0;i<7*10;i++)
            {
                float delta=26-old.x;bool coast=Mathf.Abs(old.vx)>.1f && (old.vx*delta<0 || Mathf.Abs(delta)<Mathf.Abs(old.vx)*.1f+.025f);
                float axis=!coast && Mathf.Abs(delta)>.03f?Mathf.Sign(delta):0;
                old.vx=HorizontalMotion.Step(old.vx,axis,true,false,dt,settings);old.x+=old.vx*dt;
            }
            if(Mathf.Abs(old.x-26)<=.08f && Mathf.Abs(old.vx)<=.1f)throw new Exception("Old low-FPS limit cycle no longer reproduced");
            foreach(int fps in new[]{3,5,7,10,15,30,60,144})
            foreach(float initial in new[]{-4f,25.6f,26.2f,40f})
            {
                var room=new RoomLayout {id="route",left=-100,right=100,bottom=-10,top=20,ladders=Array.Empty<RoomLadder>(),
                    platforms=new[]{new RoomSurface {x=-100,y=0,width=200,height=.8f}}};
                var actor=new ActorState {id="probe",player=true,connected=true,x=initial,y=.8f};
                var movement=new MovementController(room,settings);dt=1f/fps;
                for(int i=0;i<fps*20;i++)
                {
                    ActorPose.Advance(actor,dt);
                    movement.Step(actor,new Vector2(ProbeRouteMotion.Axis(actor,26,dt,settings),0),false,dt);
                }
                if(Mathf.Abs(actor.x-26)>.08f || Mathf.Abs(actor.vx)>.1f)
                    throw new Exception($"Route did not settle with unchanged movement: fps={fps}, start={initial}, x={actor.x}, vx={actor.vx}");
            }
            Debug.Log("PROBE_ROUTE_MOTION_PASS: original7Hz cycle reproduced; actual MovementController settles3/5/7/10/15/30/60/144Hz without widening assertions");
        }
    }
}
