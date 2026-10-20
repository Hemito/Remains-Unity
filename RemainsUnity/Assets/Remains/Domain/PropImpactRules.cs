using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Serialization;

namespace Remains.Domain
{
    [Serializable] public sealed class PropImpactSample
    {
        public string id,world,prop,owner,level;
        public int generation;
        public uint sequence;
        public double at;
        public float x,y,vx,vy,width,height,anchorHeight,mass;
        public bool friendlyPlayers;
        public Vector2 Velocity=>new(vx,vy);
        public Rect Bounds=>new(x-width*.5f,y-anchorHeight,width,height);
    }
    [Serializable] public sealed class PropImpactContact
    {
        public string id,target;
        public int lifecycle,generationBefore,generationAfter;
        public bool targetFixed;
        public PropImpactSample source;
        public double until;
        public float targetVx,targetVy,propVx,propVy,damage;
        [System.Runtime.Serialization.OnDeserialized]
        internal void ReadLegacyGeneration(System.Runtime.Serialization.StreamingContext context)=>NormalizeLegacyGeneration();
        internal void NormalizeLegacyGeneration()
        {
            // Protocol 75/76 persisted only generationAfter; preserve its accepted effects exactly.
            if(source==null || source.generation<=0 || generationBefore!=0 || generationAfter!=source.generation+1) return;
            generationBefore=source.generation;
            Debug.LogWarning($"Контакт Box {id}: прочитан журнал версии 75/76 без generationBefore; восстановлено поколение {generationBefore}. Исторические эффекты сохранены.");
        }
    }
    [Serializable] public sealed class PropImpactJournal
    {
        public int sampleGeneration=-1;
        public uint sampleSequence;
        [NonSerialized] public List<PropImpactSample> samples=new();
        public List<PropImpactContact> contacts=new();
    }
    public readonly struct PropImpactBody
    {
        public readonly float mass,knocked;
        public readonly double immunity;
        public readonly bool fixedBody,immune;
        public PropImpactBody(float mass,float knocked,double immunity,bool fixedBody=false,bool immune=false)
        {
            if(!float.IsFinite(mass) || mass<0 || !fixedBody && mass<=0 || !float.IsFinite(knocked) || knocked<0 ||
                !double.IsFinite(immunity) || immunity<0) throw new ArgumentException("Некорректный профиль контакта физического тела.");
            this.mass=mass;this.knocked=knocked;this.immunity=immunity;this.fixedBody=fixedBody;this.immune=immune;
        }
    }
    // Box.step/attDrop and Unit.udarBox. State uses kg and m/s; original uses mass/50 and px/frame.
    public static class PropImpactRules
    {
        public const float MetresPerOriginalVelocity=.6f,KgPerOriginalMass=50;
        public const double SampleHistorySeconds=2;
        public static bool IsBox(PropState prop)=>prop!=null && prop.movable && prop.hasPhysicalBody &&
            prop.definition!="loose-item" && !(prop is ContainerState container && container.presentationItem!=null);
        public static bool Dangerous(Vector2 velocity)
        {
            var original=velocity/MetresPerOriginalVelocity;
            return float.IsFinite(original.sqrMagnitude) && (Mathf.Abs(original.x)>5 || Mathf.Abs(original.y)>5) && original.sqrMagnitude>=50;
        }
        public static float Damage(float kilograms,Vector2 velocity)
        {
            if(!float.IsFinite(kilograms) || kilograms<=0 || !float.IsFinite(velocity.sqrMagnitude))
                throw new InvalidOperationException("Некорректные масса или скорость удара Box.");
            return Mathf.Max(0,kilograms/KgPerOriginalMass*(velocity.sqrMagnitude/(MetresPerOriginalVelocity*MetresPerOriginalVelocity)-50)*.2f);
        }
        public static bool Contact(PropImpactSample sample,ActorState target)
        {
            if(sample==null || target==null || sample.level!=target.level || target.health<=0 ||
                !ActorPresence.IsPresent(target) || sample.friendlyPlayers && target.player) return false;
            var hull=Anatomy.Movement(target);
            return sample.Bounds.Overlaps(new Rect(target.x-hull.width*.5f,target.y,hull.width,hull.height));
        }
        public static bool Valid(PropImpactSample sample)=>sample!=null && !string.IsNullOrEmpty(sample.id) &&
            !string.IsNullOrEmpty(sample.world) && !string.IsNullOrEmpty(sample.prop) && !string.IsNullOrEmpty(sample.owner) &&
            !string.IsNullOrEmpty(sample.level) && sample.generation>=0 && sample.sequence>0 &&
            float.IsFinite(sample.x) && float.IsFinite(sample.y) && float.IsFinite(sample.vx) && float.IsFinite(sample.vy) &&
            float.IsFinite(sample.width) && sample.width>0 && float.IsFinite(sample.height) && sample.height>0 &&
            float.IsFinite(sample.anchorHeight) && sample.anchorHeight>=0 && sample.anchorHeight<=sample.height &&
            float.IsFinite(sample.mass) && sample.mass>0 && double.IsFinite(sample.at) && sample.at>=0 &&
            sample.id==SampleId(sample.world,sample.prop,sample.generation,sample.sequence);
        public static string SampleId(string world,string prop,int generation,uint sequence)=>$"box:{world}:{prop}:{generation}:{sequence}";
        public static bool AcceptSample(WorldState world,PropState prop,string owner,PropImpactSample incoming,double now,out PropImpactSample accepted)
        {
            accepted=null;
            if(world==null || !IsBox(prop) || prop.held || prop.owner!=owner || incoming==null || incoming.world!=world.worldId ||
                incoming.prop!=prop.id || incoming.owner!=owner || incoming.generation!=prop.generation || incoming.level!=prop.level ||
                incoming.id!=SampleId(world.worldId,prop.id,prop.generation,incoming.sequence) ||
                !float.IsFinite(incoming.x) || !float.IsFinite(incoming.y) || !Dangerous(incoming.Velocity) || !double.IsFinite(now) || now<0) return false;
            var journal=prop.impacts;
            foreach(var sample in journal?.samples??new List<PropImpactSample>())
                if(sample.id==incoming.id) {accepted=sample;return true;}
            if(journal!=null && journal.sampleGeneration==incoming.generation && !PropReplication.IsNewer(incoming.sequence,journal.sampleSequence)) return false;
            foreach(var sample in journal?.samples??new List<PropImpactSample>())
                if(sample.generation==incoming.generation && !PropReplication.IsNewer(incoming.sequence,sample.sequence)) return false;
            accepted=StateJson.Copy(incoming);accepted.at=now;accepted.width=prop.width;accepted.height=prop.height;
            accepted.anchorHeight=prop.anchorHeight;accepted.mass=prop.mass;accepted.friendlyPlayers=prop.impactFriendlyPlayers;
            if(!Valid(accepted)) throw new InvalidOperationException($"Некорректный принятый образец Box: {prop.id}.");
            journal=prop.impacts??=new PropImpactJournal();
            journal.sampleGeneration=incoming.generation;journal.sampleSequence=incoming.sequence;
            journal.samples.RemoveAll(sample=>sample.at<now-SampleHistorySeconds);
            journal.contacts.RemoveAll(contact=>contact.source.at<now-SampleHistorySeconds &&
                (world.Find(contact.target) is not ActorState target || target.lifecycleSequence!=contact.lifecycle || target.resolvedAttacks.Contains(contact.id)));
            journal.samples.Add(accepted);return true;
        }
        public static PropImpactContact Prepare(WorldState world,PropState prop,PropImpactSample sample,ActorState target,
            PropImpactBody body,Vector2 targetVelocity,double now)
        {
            if(world==null || target==null || prop==null || !Valid(sample) || body.immune || !IsBox(prop) || prop.held ||
                sample.world!=world.worldId || sample.prop!=prop.id || sample.owner!=prop.owner || !ResponseVelocity(prop,sample,out _) ||
                sample.level!=prop.level || sample.level!=target.level || sample.at>now || now-sample.at>SampleHistorySeconds ||
                target.health<=0 || sample.friendlyPlayers && target.player || PropImpactCooldown.Active(target,now) ||
                !Dangerous(sample.Velocity) || !float.IsFinite(targetVelocity.sqrMagnitude)) return null;
            var registered=prop.impacts?.samples.Find(value=>value.id==sample.id);
            if(registered==null || StateJson.ToJson(registered)!=StateJson.ToJson(sample) ||
                prop.impacts.contacts.Exists(value=>value.id==sample.id+":"+target.id+":"+target.lifecycleSequence)) return null;
            if(prop.generation==int.MaxValue) throw new InvalidOperationException("Исчерпано поколение Box: "+prop.id);
            ResponseVelocity(prop,sample,out var boxVelocity);var resultTarget=targetVelocity;
            if(body.fixedBody) boxVelocity*=.5f;
            else
            {
                var centre=(boxVelocity*sample.mass+targetVelocity*body.mass)/(sample.mass+body.mass);
                resultTarget=(-targetVelocity+centre)*body.knocked+centre;
                boxVelocity=(-boxVelocity+centre)*.25f+centre;
            }
            return new PropImpactContact {id=sample.id+":"+target.id+":"+target.lifecycleSequence,target=target.id,
                lifecycle=target.lifecycleSequence,source=StateJson.Copy(sample),generationBefore=prop.generation,generationAfter=prop.generation+1,targetFixed=body.fixedBody,
                until=now+body.immunity,targetVx=resultTarget.x,targetVy=resultTarget.y,propVx=boxVelocity.x,propVy=boxVelocity.y,
                damage=Damage(sample.mass,sample.Velocity)};
        }
        public static void Commit(WorldState world,PropState prop,ActorState target,PropImpactContact contact)
        {
            if(contact==null || prop==null || target==null || contact.target!=target.id || target.lifecycleSequence!=contact.lifecycle ||
                prop.generation!=contact.generationBefore || contact.generationAfter!=prop.generation+1 || prop.held || prop.owner!=contact.source.owner)
                throw new InvalidOperationException("Контакт Box изменился до атомарной публикации.");
            prop.impacts??=new PropImpactJournal();prop.impacts.contacts.Add(contact);
            prop.generation=contact.generationAfter;prop.sequence=0;prop.vx=contact.propVx;prop.vy=contact.propVy;
            PropImpactCooldown.Record(target,contact.source.level,contact.until);
            WorldChanges.Mark(world,prop);WorldChanges.Mark(world,target);
        }
        public static bool AdoptImpulse(PropState prop,PropImpactContact contact,string authorityEpoch=null)
        {
            if(prop==null || contact==null || prop.id!=contact.source.prop || prop.owner!=contact.source.owner ||
                prop.level!=contact.source.level || prop.generation!=contact.generationBefore || prop.held) return false;
            prop.generation=contact.generationAfter;prop.sequence=0;prop.vx=contact.propVx;prop.vy=contact.propVy;
            prop.impactAuthorityEpoch=authorityEpoch;
            prop.impacts??=new PropImpactJournal();
            if(!prop.impacts.contacts.Exists(value=>value.id==contact.id)) prop.impacts.contacts.Add(StateJson.Copy(contact));
            return true;
        }
        public static void PreserveTarget(ActorState current,ActorState incoming)
        {
            if(current==null || incoming==null || current.id!=incoming.id || current.level!=incoming.level || current.lifecycleSequence!=incoming.lifecycleSequence) return;
            PropImpactCooldown.Preserve(current,incoming);
            // Never copy receipts independently of health. Existing owner reconciliation preserves health +
            // receipts atomically within one generation; a handoff without accepted health must replay the journal.
        }
        // attDrop caches damage speed once, then udarBox mutates momentum for each overlapping unit.
        private static bool ResponseVelocity(PropState prop,PropImpactSample sample,out Vector2 velocity)
        {
            velocity=sample.Velocity;
            if(prop.generation==sample.generation) return true;
            if(prop.impacts==null || prop.impacts.sampleGeneration==prop.generation) return false;
            for(int i=prop.impacts.contacts.Count-1;i>=0;i--)
            {
                var previous=prop.impacts.contacts[i];
                if(previous.source.id!=sample.id || previous.generationAfter!=prop.generation) continue;
                velocity=new Vector2(previous.propVx,previous.propVy);return true;
            }
            return false;
        }
        public static bool CanReport(PropImpactSample sample,ActorState target,string reporterOwner)=>sample!=null && target!=null &&
            (target.player?target.owner==reporterOwner:sample.owner==reporterOwner);
        public static uint NextSampleSequence(PropState prop,int sentGeneration,uint sentSequence)
        {
            uint sequence=sentGeneration==prop.generation?sentSequence:0;
            if(prop.impacts?.sampleGeneration==prop.generation && PropReplication.IsNewer(prop.impacts.sampleSequence,sequence)) sequence=prop.impacts.sampleSequence;
            if(sequence==uint.MaxValue) throw new InvalidOperationException("Исчерпан номер образца Box: "+prop.id);
            return sequence+1;
        }
        public static void Validate(PropImpactContact contact)
        {
            contact?.NormalizeLegacyGeneration();
            if(contact==null || !Valid(contact.source) || string.IsNullOrEmpty(contact.target) || contact.lifecycle<0 ||
                contact.id!=contact.source.id+":"+contact.target+":"+contact.lifecycle ||
                contact.generationBefore<contact.source.generation || contact.generationAfter!=contact.generationBefore+1 || !double.IsFinite(contact.until) || contact.until<contact.source.at ||
                !float.IsFinite(contact.targetVx) || !float.IsFinite(contact.targetVy) || !float.IsFinite(contact.propVx) || !float.IsFinite(contact.propVy) ||
                !float.IsFinite(contact.damage) || Mathf.Abs(contact.damage-Damage(contact.source.mass,contact.source.Velocity))>.0001f)
                throw new InvalidOperationException("Повреждён подтверждённый контакт Box: "+contact?.id);
        }
        public static void ValidateJournal(PropState prop)
        {
            if(prop.impacts==null) return;
            if(prop.impacts.samples==null || prop.impacts.contacts==null) throw new InvalidOperationException("Нет списков журнала Box: "+prop.id);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var sample in prop.impacts.samples)
                if(!Valid(sample) || sample.prop!=prop.id || sample.level!=prop.level || !ids.Add(sample.id) ||
                    sample.width!=prop.width || sample.height!=prop.height || sample.mass!=prop.mass)
                    throw new InvalidOperationException("Повреждён образец журнала Box: prop="+prop.id+", sample="+sample?.id);
            ids.Clear();
            foreach(var contact in prop.impacts.contacts)
            {
                Validate(contact);
                if(contact.source.prop!=prop.id || contact.source.level!=prop.level || !ids.Add(contact.id))
                    throw new InvalidOperationException("Повреждена принадлежность контакта Box: "+contact.id);
            }
        }
    }
}
