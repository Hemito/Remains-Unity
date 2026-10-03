using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Presentation;
using Remains.Gameplay;
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
            public uint poseSequence;
            public float time,phase,simulationPhase,x,y,visualX,visualY;
            public bool hasMuzzle;
            public float muzzleError,muzzleX,muzzleY,expectedMuzzleX,expectedMuzzleY;
        }
        [Serializable] private sealed class Trace {public List<Sample> samples=new();}
        private readonly Trace trace=new();
        public void Observe(ActorState actor,PonyVisual visual,bool local,WeaponView weapon=null)
        {
            if(visual==null || visual.DrawnClip!="run" || trace.samples.Count>=10000) return;
            var position=visual.transform.position;
            bool hasMuzzle=weapon!=null && weapon.gameObject.activeInHierarchy && weapon.Muzzle!=null;
            Vector2 muzzle=hasMuzzle?(Vector2)weapon.Muzzle.position:Vector2.zero;
            Vector2 expected=hasMuzzle?WeaponPose.Muzzle(actor):Vector2.zero;
            trace.samples.Add(new Sample {actor=actor.id,local=local,clip=visual.DrawnClip,frame=visual.DrawnFrame,
                time=Time.unscaledTime,phase=visual.DrawnTime,simulationPhase=actor.poseTime,poseSequence=actor.poseSequence,
                x=actor.x,y=actor.y,visualX=position.x,visualY=position.y,hasMuzzle=hasMuzzle,
                muzzleX=muzzle.x,muzzleY=muzzle.y,expectedMuzzleX=expected.x,expectedMuzzleY=expected.y,
                muzzleError=Vector2.Distance(muzzle,expected)});
        }
        public void Save(string path)=>File.WriteAllText(path,JsonUtility.ToJson(trace));
    }
}
