using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Runtime
{
    // Probe-only bounded observations after all Update methods; never drives animation.
    public sealed class TrainingAnimationTrace
    {
        [Serializable] private sealed class Sample
        {
            public string actor,clip;
            public bool local;
            public int frame;
            public float time,phase,simulationPhase,x,y,visualX,visualY;
        }
        [Serializable] private sealed class Trace {public List<Sample> samples=new();}
        private readonly Trace trace=new();
        public void Observe(ActorState actor,PonyVisual visual,bool local)
        {
            if(visual==null || visual.DrawnClip!="run" || trace.samples.Count>=10000) return;
            var position=visual.transform.position;
            trace.samples.Add(new Sample {actor=actor.id,local=local,clip=visual.DrawnClip,frame=visual.DrawnFrame,
                time=Time.unscaledTime,phase=visual.DrawnTime,simulationPhase=actor.poseTime,
                x=actor.x,y=actor.y,visualX=position.x,visualY=position.y});
        }
        public void Save(string path)=>File.WriteAllText(path,JsonUtility.ToJson(trace));
    }
}
