using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Runtime;
using UnityEngine;

namespace Remains.Editor
{
    public static class GrenadeProbeRouteChecks
    {
        [Serializable] private sealed class Sample
        {public int fps;public float initial,x,y,vx;public bool ready;}
        [Serializable] private sealed class Report
        {public bool success,oldEverReady;public string failure;public float throwerX,range,target,oldX,oldVx;public Sample[] samples;}
        private static RoomLayout Room()=>new() {id="grenade-stance",left=-100,right=100,bottom=-10,top=20,
            ladders=Array.Empty<RoomLadder>(),platforms=new[]{new RoomSurface {x=-100,y=0,width=200,height=.8f}}};
        // Exact NetworkGrenadeProbe.ReadyToThrow tolerances, including shared support height.
        private static bool Ready(ActorState actor,float target)=>Mathf.Abs(actor.x-target)<.05f &&
            Mathf.Abs(actor.vx)<.15f && Mathf.Abs(actor.y-.8f)<.05f;
        private static ActorState Actor(float x)=>new() {id="grenade-stance",player=true,connected=true,x=x,y=.8f};
        public static void Run()
        {
            var report=new Report {throwerX=17};var samples=new List<Sample>();
            try
            {
                Anatomy.Load();FootSupportCatalog.Load();WeaponCatalog.Load();var settings=MovementSettings.Load();
                report.range=WeaponCatalog.Get("grenade").range;report.target=report.throwerX+report.range*5/6;
                var old=Actor(report.throwerX);var oldMovement=new MovementController(Room(),settings);float dt=1f/3;
                for(int frame=0;frame<3*45;frame++)
                {
                    ActorPose.Advance(old,dt);
                    oldMovement.Step(old,new Vector2(Mathf.Clamp((report.target-old.x)*2,-1,1),0),false,dt);
                    report.oldEverReady|=Ready(old,report.target);
                }
                report.oldX=old.x;report.oldVx=old.vx;
                if(report.oldEverReady)throw new InvalidOperationException("Original3Hz grenade stance did not reproduce the phase0 failure.");
                foreach(int fps in new[]{3,5,7,10,15,30,60,144})
                foreach(float initial in new[]{report.throwerX,report.target+(report.target-report.throwerX)})
                {
                    var actor=Actor(initial);var movement=new MovementController(Room(),settings);dt=1f/fps;
                    for(int frame=0;frame<fps*20;frame++)
                    {
                        ActorPose.Advance(actor,dt);
                        movement.Step(actor,new Vector2(ProbeRouteMotion.Axis(actor,report.target,dt,settings),0),false,dt);
                    }
                    var sample=new Sample {fps=fps,initial=initial,x=actor.x,y=actor.y,vx=actor.vx,ready=Ready(actor,report.target)};
                    samples.Add(sample);
                    if(!sample.ready)throw new InvalidOperationException($"Grenade stance not ready: fps={fps}, initial={initial:R}, target={report.target:R}, x={actor.x:R}, vx={actor.vx:R}.");
                }
                report.success=true;
                Debug.Log($"GRENADE_PROBE_ROUTE_PASS: old3Hz fails45s; actual MovementController ready at3/5/7/10/15/30/60/144Hz from both sides; target={report.target:R}, range={report.range:R}; unchanged .05/.15 tolerances.");
            }
            catch(Exception error) {report.failure=error.Message;throw;}
            finally
            {
                report.samples=samples.ToArray();
                string path=Environment.GetEnvironmentVariable("REMAINS_GRENADE_ROUTE_REPORT")??Path.Combine(Application.dataPath,"../Temp/GrenadeProbeRouteChecks/result.json");
                path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            }
        }
    }
}
