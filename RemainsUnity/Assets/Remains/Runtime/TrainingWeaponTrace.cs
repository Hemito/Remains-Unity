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
        }
        [Serializable] private sealed class Trace {public List<Sample> samples=new();}
        private readonly Trace trace=new();
        private readonly Queue<Sample> pending=new();
        public void Observe(AttackEvent attack,ActorState actor,bool local)
        {
            if(attack.weapon!="pistol" || actor==null || trace.samples.Count+pending.Count>=128) return;
            pending.Enqueue(new Sample {attack=attack.id,actor=actor.id,local=local,moving=Mathf.Abs(actor.vx)>4,
                receivedAt=Time.unscaledTime,originX=attack.originX,originY=attack.originY});
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
                    Vector2 muzzle=view.Muzzle.position;sample.muzzleX=muzzle.x;sample.muzzleY=muzzle.y;
                    sample.launchGap=Vector2.Distance(new Vector2(sample.originX,sample.originY),muzzle);
                    sample.poseError=Vector2.Distance(WeaponPose.Muzzle(actor),muzzle);
                }
                movingShot|=sample.moving;trace.samples.Add(sample);
            }
            return movingShot;
        }
        public void Save(string path)=>File.WriteAllText(path,JsonUtility.ToJson(trace,true));
    }
}
