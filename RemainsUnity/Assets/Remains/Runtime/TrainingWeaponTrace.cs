using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    // Measurement only: launch coordinates are never adjusted to fit the visible weapon.
    public sealed class TrainingWeaponTrace
    {
        [Serializable] private sealed class Sample
        {
            public string attack,actor;
            public bool local,moving,hasView;
            public float receivedAt,drawnAt,originX,originY,muzzleX,muzzleY,launchGap,poseError;
            public double observedClock,receivedMotionClock,drawnMotionClock;
            public int generation,receivedClockGeneration,drawnClockGeneration;
            public string owner;
            public uint receivedMotionSequence,drawnMotionSequence;
            public float receivedGap,receivedX,receivedY,drawnX,drawnY;
            public bool trailObserved;
            public string muzzleJoinState;
            public float trailJoinGap,trailHeadError,trailTailDistance;
        }
        [Serializable] private sealed class Trace {public List<Sample> samples=new();}
        private readonly Trace trace=new();
        private readonly Queue<Sample> pending=new();
        private readonly string weapon;
        public TrainingWeaponTrace(string weapon="pistol") {this.weapon=weapon;}
        public void ObserveTrail(WorldState world,ProjectileVisual view)
        {
            var sample=trace.samples.Find(value=>value.attack==view.AttackId);
            if(sample==null) foreach(var value in pending) if(value.attack==view.AttackId) {sample=value;break;}
            var actor=world?.Find(view.AttackerId);
            if(sample==null || sample.trailObserved || actor==null) return;
            sample.trailObserved=true;
            sample.muzzleJoinState=view.MuzzleJoinState;
            sample.trailJoinGap=Vector2.Distance(view.TrailStart,WeaponPose.Muzzle(actor));
            sample.trailHeadError=Vector2.Distance(view.TrailEnd,view.PhysicalHead);
            sample.trailTailDistance=view.TrailTailDistance;
        }
        public void Observe(AttackEvent attack,ActorState actor,bool local)
        {
            if(attack.weapon!=weapon || actor==null || trace.samples.Count+pending.Count>=128) return;
            pending.Enqueue(new Sample {attack=attack.id,actor=actor.id,local=local,moving=Mathf.Abs(actor.vx)>4,
                receivedAt=Time.unscaledTime,originX=attack.originX,originY=attack.originY,
                observedClock=Time.unscaledTimeAsDouble,receivedMotionClock=actor.motionSentAt,
                generation=actor.generation,receivedClockGeneration=actor.motionClockGeneration,owner=actor.owner,
                receivedMotionSequence=actor.motionSequence,receivedX=actor.x,receivedY=actor.y,
                receivedGap=Vector2.Distance(attack.Origin,WeaponPose.Muzzle(actor))});
        }
        public bool Draw(WorldState world,InteractionVisuals visuals)
        {
            bool movingShot=false;
            while(pending.Count>0)
            {
                var sample=pending.Dequeue();var actor=world.Find(sample.actor);var view=visuals.FindWeapon(sample.actor);
                sample.drawnAt=Time.unscaledTime;
                sample.hasView=actor!=null && view!=null && view.gameObject.activeInHierarchy && view.Muzzle!=null;
                if(sample.hasView)
                {
                    sample.drawnMotionClock=actor.motionSentAt;sample.drawnClockGeneration=actor.motionClockGeneration;
                    sample.drawnMotionSequence=actor.motionSequence;sample.drawnX=actor.x;sample.drawnY=actor.y;
                    Vector2 muzzle=view.Muzzle.position;sample.muzzleX=muzzle.x;sample.muzzleY=muzzle.y;
                    sample.launchGap=Vector2.Distance(new Vector2(sample.originX,sample.originY),muzzle);
                    sample.poseError=Vector2.Distance(WeaponPose.Muzzle(actor),muzzle);
                }
                movingShot|=sample.moving;trace.samples.Add(sample);
            }
            return movingShot;
        }
        public bool ValidateLocal(out string reason)
        {
            int count=0;
            foreach(var sample in trace.samples)
            {
                if(!sample.local) continue;
                count++;
                if(!sample.hasView || !float.IsFinite(sample.launchGap) || sample.launchGap>.001f || sample.poseError>.001f)
                {reason=$"Дуло собственного выстрела не совпало: shot={sample.attack}, view={sample.hasView}, launchGap={sample.launchGap}, poseError={sample.poseError}.";return false;}
            }
            reason=count==3?null:"Ожидались три собственных выстрела в журнале, получено "+count;
            return reason==null;
        }
        public void Save(string path)=>File.WriteAllText(path,JsonUtility.ToJson(trace,true));
    }
}
