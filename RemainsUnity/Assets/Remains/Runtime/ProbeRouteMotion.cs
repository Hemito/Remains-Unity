using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Opt-in probe commands only. Production movement, acceleration and final-position assertions stay unchanged.
    public static class ProbeRouteMotion
    {
        private static MovementSettings settings;
        [Serializable] private struct Sample
        {public int frame;public float time,dt,target,x,y,vx,vy,axis;public string actor,owner,route;public uint sequence;}
        [Serializable] private sealed class Trace {public Sample[] samples;}
        private static readonly Queue<Sample> samples=new();
        public static float Axis(ActorState actor,float target,float dt,MovementSettings data)
        {
            if(actor==null || data==null || !float.IsFinite(target) || !float.IsFinite(dt) || dt<=0) return 0;
            float delta=target-actor.x;
            if(Mathf.Abs(delta)<=.025f) return 0;
            float speed=actor.crouched?data.crouchSpeed:data.walkSpeed;
            // A full-axis step at 5 Hz exceeds the route tolerance. Bound the requested displacement to half of
            // the remaining distance, and keep the ordinary proportional approach at normal frame rates.
            float gain=Mathf.Min(6,.5f/dt);
            return Mathf.Clamp(delta*gain/speed,-1,1);
        }
        public static PlayerCommand Approach(ActorState actor,float target,string route)
        {
            settings??=MovementSettings.Load();float dt=Time.deltaTime;
            float axis=Axis(actor,target,dt,settings);
            if(samples.Count>=256) samples.Dequeue();
            samples.Enqueue(new Sample {frame=Time.frameCount,time=Time.unscaledTime,dt=dt,target=target,x=actor.x,y=actor.y,
                vx=actor.vx,vy=actor.vy,axis=axis,actor=actor.id,owner=actor.owner,route=route,sequence=actor.motionSequence});
            return new PlayerCommand {movement=new Vector2(axis,0)};
        }
        public static void SaveTrace(string report)
        {
            if(!string.IsNullOrEmpty(report)) File.WriteAllText(report+".route.json",JsonUtility.ToJson(new Trace {samples=samples.ToArray()},true));
        }
    }
}
