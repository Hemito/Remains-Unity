using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Owner-only transient impulses. Actor references may be replaced by recovery snapshots.
    // Clear on a new session binding or an explicit rule/content reload, even for the same WorldId.
    public sealed class WeaponRecoilMotion
    {
        private sealed class Impulse
        {
            public string id,context;
            public double age;
            public int side;
        }
        private sealed class Binding
        {
            public string actorId,owner,level,item,definition;
            public int generation,lifecycle,tick;
            public long selection;
            public ActorState actor;
            public WeaponRecoilProfile profile;
            public readonly List<Impulse> impulses=new();
            public bool Matches(ActorState value)=>value!=null && value.id==actorId && value.owner==owner &&
                value.level==level && value.equippedWeaponItem==item && value.weapon==definition &&
                value.generation==generation && value.lifecycleSequence==lifecycle && value.weaponSelectionRevision==selection;
        }
        private readonly Func<string,WeaponDefinition> definitions;
        private readonly IWeaponRecoilRule rule;
        private readonly Action<string> warning;
        private static readonly Action<string> alreadyWarned=_=>{};
        private readonly Dictionary<string,Binding> bindings=new(StringComparer.Ordinal);
        // Session/world-scoped receipts also remember rejected/expired shots. Clear releases them.
        private readonly HashSet<string> acceptedIds=new(StringComparer.Ordinal),legacyWarnings=new(StringComparer.Ordinal);
        private readonly List<string> removed=new();
        private Func<string,bool> paused;
        private Func<ActorState,bool> blocked;
        private string worldId,localOwner;
        private int tick;
        public int ActiveActors=>bindings.Count;
        public int ActiveImpulses
        {
            get {int count=0;foreach(var binding in bindings.Values) count+=binding.impulses.Count;return count;}
        }
        public WeaponRecoilMotion(Func<string,WeaponDefinition> definitions=null,IWeaponRecoilRule curveRule=null,Action<string> warning=null)
        {
            this.definitions=definitions??WeaponCatalog.Get;
            rule=curveRule??new SmoothWeaponRecoilRule();
            this.warning=warning??(message=>Debug.LogWarning(message));
        }
        public void Step(WorldState world,string localOwner,float dt,Func<string,bool> paused,long pauseRevision,Func<ActorState,bool> blocked)
        {
            ValidateTime(dt,pauseRevision);
            if(paused==null || blocked==null) throw new ArgumentNullException(paused==null?nameof(paused):nameof(blocked),"Отдача: нужны явные предикаты паузы и блокировки.");
            if(!BindWorld(world,localOwner)) return;
            this.paused=paused;this.blocked=blocked;
            tick=unchecked(tick+1);
            foreach(var actor in world.actors)
            {
                if(actor.owner!=localOwner) continue; // Remote never samples gameplay impulses.
                ValidateExistingPose(actor);
                bool usable=CanUse(actor) && !paused(actor.level) && !blocked(actor);
                if(!usable || !bindings.TryGetValue(actor.id,out var binding) || !binding.Matches(actor))
                {
                    bindings.Remove(actor.id);Neutralize(actor,pauseRevision);continue;
                }
                binding.actor=actor;binding.tick=tick;
                foreach(var impulse in binding.impulses) impulse.age+=dt;
                Compose(binding,actor,pauseRevision);
                for(int i=binding.impulses.Count-1;i>=0;i--)
                    if(binding.impulses[i].age>=binding.profile.DurationSeconds) binding.impulses.RemoveAt(i);
            }
            removed.Clear();
            foreach(var pair in bindings) if(pair.Value.tick!=tick) removed.Add(pair.Key);
            foreach(string id in removed)
            {
                var binding=bindings[id];
                if(binding.Matches(binding.actor)) Neutralize(binding.actor,pauseRevision);
                bindings.Remove(id);
            }
        }
        public bool CommitShot(WorldState world,string localOwner,ActorState actor,AttackEvent shot,long pauseRevision)
        {
            ValidateTime(0,pauseRevision);
            if(!BindWorld(world,localOwner)) return false;
            if(actor==null || shot==null) throw new ArgumentNullException(actor==null?nameof(actor):nameof(shot),"Нет персонажа или принятого выстрела для отдачи.");
            var current=world.Find(actor.id);
            if(current==null || current.owner!=localOwner || !CanUse(current)) return false;
            if(string.IsNullOrWhiteSpace(shot.id)) throw new ArgumentException($"Отдача: нет ID выстрела, actor={actor.id}.",nameof(shot));
            if(acceptedIds.Contains(shot.id)) return false;
            var pose=shot.rangedPose??throw new InvalidOperationException($"Отдача без исторической позы: world={world.worldId}, actor={actor.id}, shot={shot.id}, weapon={shot.weapon}.");
            pose.Validate($"отдача, actor={actor.id}, shot={shot.id}");
            if(shot.attacker!=current.id || shot.sourceOwner!=localOwner || shot.level!=current.level || shot.weapon!=current.weapon ||
                pose.worldId!=world.worldId || pose.weaponItem!=current.equippedWeaponItem || pose.selectionRevision!=current.weaponSelectionRevision ||
                pose.actorGeneration!=current.generation || pose.actorLifecycle!=current.lifecycleSequence) return false;
            ValidateExistingPose(current);
            if(paused?.Invoke(current.level)==true || blocked?.Invoke(current)==true)
            {bindings.Remove(current.id);Neutralize(current,pauseRevision);return false;}
            string context=$"world={world.worldId}, actor={current.id}, shot={shot.id}, weapon={shot.weapon}";
            var definition=definitions(shot.weapon);
            if(definition==null || definition.id!=shot.weapon)
                throw new InvalidOperationException($"Отдача: resolver вернул другое или отсутствующее определение, {context}, resolved={definition?.id}.");
            var profile=WeaponRecoilProfile.ValidateForWeapon(definition,context,
                definition.recoil==null && !legacyWarnings.Add(definition.id)?alreadyWarned:warning);
            if(profile==null || !profile.enabled)
            {acceptedIds.Add(shot.id);bindings.Remove(current.id);Neutralize(current,pauseRevision);return false;}
            // Also validated in owner preparation before ammo debit. Protect direct API consumers.
            WeaponRecoilSampling.ValidateSample(rule,profile,0,context);
            if(!bindings.TryGetValue(current.id,out var binding) || !binding.Matches(current) || !binding.profile.SameValues(profile))
            {
                binding=new Binding {actorId=current.id,owner=localOwner,level=current.level,item=current.equippedWeaponItem,
                    definition=current.weapon,generation=current.generation,lifecycle=current.lifecycleSequence,
                    selection=current.weaponSelectionRevision,profile=profile.Copy()};
                bindings[current.id]=binding;
            }
            acceptedIds.Add(shot.id);binding.actor=current;
            binding.impulses.Add(new Impulse {id=shot.id,context=context,side=pose.mirrored?-1:1});
            Compose(binding,current,pauseRevision);return true;
        }
        public bool Reject(string shotId,WorldState world,string localOwner,long pauseRevision)
        {
            ValidateTime(0,pauseRevision);
            if(!BindWorld(world,localOwner) || string.IsNullOrEmpty(shotId)) return false;
            bool rejected=false;
            foreach(var binding in bindings.Values)
            {
                bool removedImpulse=false;
                for(int i=binding.impulses.Count-1;i>=0;i--)
                    if(binding.impulses[i].id==shotId) {binding.impulses.RemoveAt(i);removedImpulse=true;}
                if(!removedImpulse) continue;
                rejected=true;var actor=world.Find(binding.actorId);
                if(binding.Matches(actor))
                {
                    binding.actor=actor;
                    if(!CanUse(actor) || paused?.Invoke(actor.level)==true || blocked?.Invoke(actor)==true)
                    {binding.impulses.Clear();Neutralize(actor,pauseRevision);}
                    else Compose(binding,actor,pauseRevision);
                }
            }
            // Never delete the accepted ID receipt or rewrite any already emitted projectile.
            return rejected;
        }
        public void Clear()
        {
            foreach(var binding in bindings.Values)
                if(binding.Matches(binding.actor)) Neutralize(binding.actor,binding.actor.weaponRecoil?.pauseRevision??-1);
            bindings.Clear();acceptedIds.Clear();legacyWarnings.Clear();removed.Clear();
            worldId=null;localOwner=null;paused=null;blocked=null;tick=0;
        }
        private bool BindWorld(WorldState world,string owner)
        {
            if(world==null || string.IsNullOrEmpty(owner)) {Clear();return false;}
            if(string.IsNullOrWhiteSpace(world.worldId)) throw new ArgumentException("Отдача: у мира отсутствует стабильный WorldId.",nameof(world));
            if(worldId!=world.worldId || localOwner!=owner) {Clear();worldId=world.worldId;localOwner=owner;}
            return true;
        }
        private void Compose(Binding binding,ActorState actor,long pauseRevision)
        {
            double horizontal=0,angle=0;
            foreach(var impulse in binding.impulses)
            {
                var sample=WeaponRecoilSampling.ValidateSample(rule,binding.profile,impulse.age,impulse.context);
                horizontal-=impulse.side*(double)sample.horizontal;angle+=impulse.side*(double)sample.angle;
            }
            var pose=actor.weaponRecoil??=WeaponRecoilPose.Neutral(pauseRevision);
            pose.offsetX=(float)Math.Clamp(horizontal,-binding.profile.maxHorizontal,binding.profile.maxHorizontal);
            pose.angleDegrees=(float)Math.Clamp(angle,-binding.profile.maxAngleDegrees,binding.profile.maxAngleDegrees);
            pose.pauseRevision=pauseRevision;
        }
        private static bool CanUse(ActorState actor)=>ActorPresence.IsPresent(actor) && !ActorSessionRules.IsPassive(actor) &&
            string.IsNullOrEmpty(actor.heldItemId) && !string.IsNullOrEmpty(actor.weapon) && actor.weapon!="grenade";
        private static void Neutralize(ActorState actor,long pauseRevision)
        {
            if(actor==null) return;
            var pose=actor.weaponRecoil??=WeaponRecoilPose.Neutral(pauseRevision);
            pose.offsetX=0;pose.angleDegrees=0;pose.pauseRevision=pauseRevision;
        }
        private static void ValidateExistingPose(ActorState actor)
        {
            var pose=actor.weaponRecoil;
            if(pose!=null && (pose.version!=1 || !float.IsFinite(pose.offsetX) || !float.IsFinite(pose.angleDegrees) || pose.pauseRevision < -1))
                pose.Validate($"owner motion, actor={actor.id}, weapon={actor.weapon}");
        }
        private static void ValidateTime(float dt,long pauseRevision)
        {
            if(!float.IsFinite(dt) || dt<0 || pauseRevision < -1)
                throw new ArgumentOutOfRangeException(nameof(dt),$"Отдача: некорректное время/пауза, dt={dt}, pauseRevision={pauseRevision}.");
        }
    }
}
