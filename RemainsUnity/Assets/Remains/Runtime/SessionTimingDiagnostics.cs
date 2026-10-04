using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;

namespace Remains.Runtime
{
    // Local monotonic timings: never subtract clocks belonging to different PCs.
    // Bounded in-memory capture; no per-frame disk writes or effect on gameplay decisions.
    public sealed class SessionTimingDiagnostics : MonoBehaviour
    {
        [Serializable] private sealed class Entry
        {
            public string stage,attack,actor;
            public double seconds,rtt,gapSeconds;
            public double operationSeconds;
            public int jsonCharacters;
            public bool local;
            public string targetOwner;
            public int requestedGeneration,targetGeneration;
        }
        [Serializable] private sealed class Report
        {
            public string character;
            public int frames,droppedEntries;
            public int poses;
            public double maxFrameSeconds;
            public double maxPoseGapSeconds;
            public List<Entry> events=new();
        }
        private readonly Report report=new();
        private PrototypeRuntime runtime;
        private string path;
        private bool written;
        private double lastFrame;
        private readonly Dictionary<string,(int generation,int lifecycle,string level,double time)> lastPoses=new();
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;report.character=value.Options.Character;lastFrame=Time.realtimeSinceStartupAsDouble;
            path=string.IsNullOrEmpty(value.Options.Report)?
                Path.Combine(Application.persistentDataPath,"Diagnostics","session-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".json"):
                value.Options.Report+".timing.json";
            value.Session.AttackReceived+=attack=>Record("launch-observed",attack.id,attack.attacker);
            value.Session.Projectiles.Confirmed+=id=>Record("launch-confirmed",id,value.Session.LocalActorId);
            value.Session.NpcHitTrace+=(stage,message,target)=>
            {
                var attack=Remains.Serialization.StateJson.FromJson<Remains.Domain.AttackEvent>(message.attackJson);
                Record(stage,attack.id,message.target,owner:target?.owner,
                    requestedGeneration:message.generation,targetGeneration:target?.generation??-1);
            };
            value.Session.RecoveryMeasured+=(stage,seconds,characters)=>Record(stage,null,null,operation:seconds,characters:characters);
            value.Combat.Projectiles.ContactObserved+=contact=>Record("contact-observed",contact.projectile,contact.target);
            value.Combat.DamageApplied+=result=>Record("damage-applied",result.attack,result.actor);
            value.DamageFeedback.Received+=result=>Record("damage-number-received",result.attack,result.actor);
            value.DamageFeedback.DeliveryMeasured+=(stage,result)=>Record(stage,result.attack,result.actor);
            value.DamageNumbers.Shown+=result=>Record("damage-number-shown",result.attack,result.actor);
            value.Session.PoseReceived+=actor=>
            {
                double now=Time.realtimeSinceStartupAsDouble;report.poses++;
                if(lastPoses.TryGetValue(actor.id,out var previous) && previous.generation==actor.generation &&
                    previous.lifecycle==actor.lifecycleSequence && previous.level==actor.level)
                {
                    double gap=now-previous.time;report.maxPoseGapSeconds=Math.Max(report.maxPoseGapSeconds,gap);
                    if(gap>.1) Record("pose-gap",null,actor.id,gap);
                }
                lastPoses[actor.id]=(actor.generation,actor.lifecycleSequence,actor.level,now);
            };
            Debug.Log("Диагностика задержек включена. Отчёт будет записан при выходе: "+path);
        }
        private void Record(string stage,string attack,string actor,double gap=0,double operation=0,int characters=0,
            string owner=null,int requestedGeneration=0,int targetGeneration=0)
        {
            if(report.events.Count>=16384) {report.droppedEntries++;return;}
            report.events.Add(new Entry {stage=stage,attack=attack,actor=actor,
                seconds=Time.realtimeSinceStartupAsDouble,rtt=NetworkTime.rtt,gapSeconds=gap,operationSeconds=operation,jsonCharacters=characters,
                targetOwner=owner,requestedGeneration=requestedGeneration,targetGeneration=targetGeneration,
                local=actor!=null && actor==runtime.Session.LocalActorId});
        }
        private void Update()
        {
            double now=Time.realtimeSinceStartupAsDouble,elapsed=now-lastFrame;lastFrame=now;
            report.frames++;report.maxFrameSeconds=Math.Max(report.maxFrameSeconds,elapsed);
            if(elapsed>.1) Record("frame-stall",null,null,elapsed);
        }
        private void OnApplicationQuit()=>Write();
        private void OnDestroy()=>Write();
        private void Write()
        {
            if(written || path==null) return;written=true;
            try {Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,JsonUtility.ToJson(report,true));}
            catch(Exception error) {Debug.LogWarning("Не удалось сохранить диагностику задержек: "+path+". "+error.Message);}
        }
    }
}
