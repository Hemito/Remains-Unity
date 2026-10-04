using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;
using Remains.Content;

namespace Remains.Gameplay
{
    public sealed class CombatController
    {
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private DamageReceiver damage;
        public int AttackerDecidedHits {get;private set;}
        public int VictimDecidedHits {get;private set;}
        public event Action<string> NpcReplayRejected;
        public event Action<DamageResult> DamageApplied;
        // A locally observed contact is cosmetic evidence, not confirmation of damage.
        public event Action<AttackEvent,ActorState> ContactObserved;
        public event Action<AttackEvent,string,Vector2> SurfaceContactObserved;
        public event Action<AttackEvent,string,string,Vector2> PropContactObserved;
        private readonly SimulationTimer shotCooldown=new();
        public float ShotCooldownRemaining=>shotCooldown.Remaining;
        private ActorState swingActor;
        private MeleeSweep swingSweep;
        private string swingId,swingWeapon,swingItem;
        private string swingLevel;
        private int swingGeneration,swingLifecycle;
        public string Weapon { get; set; } = "pistol";
        public bool IsMeleeActive=>swingActor!=null;
        public string ActiveMeleeId=>IsMeleeActive?swingId:null;
        public float MeleeRemaining=>IsMeleeActive?swingSweep.Duration-swingSweep.Elapsed:0;
        public WeaponReloadController Reload { get; }
        public ProjectileController Projectiles {get;}
        private float nextResourceNotice;
        private string selectedActor;
        private int selectedGeneration;
        private bool resolvingExplosions;
        private readonly DeferredLevelActions pending=new();
        public int PendingHitCount=>pending.Count;
        public Func<string,int,Vector2?> LocalExplosionPosition { get; set; }
        public CombatController(CoopSession session,SimulationGeometry geometry)
        {
            this.session = session;
            this.geometry=geometry;
            session.ClientReady+=()=>{damage=new DamageReceiver(session.Rules.Damage,session.Rules.Knockdown);damage.Applied+=result=>DamageApplied?.Invoke(result);AttackerDecidedHits=VictimDecidedHits=0;};
            Reload=new WeaponReloadController(session);
            Projectiles=new ProjectileController(session,geometry,ApplyAttackTarget);
            session.AttackReceived += ReceiveAttack;
            session.NpcHitReceived += ReceiveNpcHit;
            session.FriendlyHitReceived+=ReceiveFriendlyHit;
            session.Changed+=RestoreSelection;
            session.Changed+=ResolveExplosions;
            session.Pause.Changed+=ResolveExplosions;
            session.Changed+=()=>{if(session.World==null) pending.Clear();};
        }

        private void RestoreSelection()
        {
            var actor=session.LocalActor;
            if(actor==null) {selectedActor=null;return;}
            if(selectedActor==actor.id && selectedGeneration==actor.generation) return;
            selectedActor=actor.id;selectedGeneration=actor.generation;
            Weapon=actor.weapon;shotCooldown.Restart(0);swingActor=null;
        }

        public bool Fire(ActorState attacker, Vector2 aim)
        {
            if(attacker?.weaponInventoryVersion==1) Weapon=WeaponInventory.Selected(attacker)?.definition;
            if(Weapon=="grenade") return false; // Throws use their own inventory transaction and flight.
            if (string.IsNullOrEmpty(Weapon) || attacker == null || attacker.down || session.Pause.IsPaused(attacker.level) || Reload.Active || !shotCooldown.Ready) return false;
            var definition=WeaponCatalog.Get(Weapon);
            attacker.weapon=Weapon;
            WeaponPose.Aim(attacker,aim,geometry.Get(attacker.level));
            bool melee=Weapon is "spear" or "crowbar";
            Vector2 origin=melee?WeaponPose.Origin(attacker):WeaponPose.Muzzle(attacker);
            Vector2 direction=WeaponPose.Direction(attacker);
            if(!melee && ShotObstacles.FirstContact(geometry.Get(attacker.level),WeaponPose.RangedAnchor(attacker),origin).HasValue)
            {
                if(Time.unscaledTime>=nextResourceNotice)
                {nextResourceNotice=Time.unscaledTime+1;session.ShowNotice("Ствол перекрыт препятствием.");}
                return false;
            }
            if(!WeaponAmmunition.SpendShot(attacker,definition,out string reason))
            {
                if(Time.unscaledTime>=nextResourceNotice)
                {nextResourceNotice=Time.unscaledTime+1;session.ShowNotice(reason);}
                return false;
            }
            if(definition.manaCost>0 || !string.IsNullOrEmpty(definition.ammo))
            {attacker.revision++;session.PublishActor(attacker);}
            shotCooldown.Restart(definition.cooldown);
            if(melee)
            {
                swingActor=attacker;swingWeapon=Weapon;swingItem=attacker.equippedWeaponItem;
                swingLevel=attacker.level;swingGeneration=attacker.generation;swingLifecycle=attacker.lifecycleSequence;
                swingId=Guid.NewGuid().ToString("N");swingSweep=new MeleeSweep(Weapon,WeaponPose.Origin(attacker),direction,attacker.weaponReach);
                return true;
            }
            float range = definition.range;
            session.Fire(new AttackEvent { id = Guid.NewGuid().ToString("N"), attacker = attacker.id, weapon = Weapon,level=attacker.level,
                originX = origin.x, originY = origin.y, endX = origin.x + direction.x * range,
                endY = origin.y + direction.y * range, speed = attacker.Velocity.magnitude + range / 0.15f });
            return true;
        }

        public void Step(float dt)
        {
            pending.Drain(session.Pause.IsPaused);
            Projectiles.Step(dt);
            if(session.LocalActor!=null && session.Pause.IsPaused(session.LocalActor.level)) dt=0;
            shotCooldown.Advance(dt);
            Reload.Step(Weapon,dt);
            if(swingActor==null) return;
            var actor=session.World?.Find(swingActor.id);
            if(actor==null || actor.down || actor.owner!=session.LocalOwner || actor.level!=swingLevel ||
                actor.generation!=swingGeneration || actor.lifecycleSequence!=swingLifecycle ||
                actor.weaponInventoryVersion==1 && (actor.equippedWeaponItem!=swingItem || !WeaponInventory.Available(actor,swingWeapon))) { swingActor=null;return; }
            if(session.Pause.IsPaused(actor.level)) return;
            var origin=WeaponPose.Origin(actor);
            swingSweep.Advance(origin,dt,segment=>session.Fire(new AttackEvent { id=swingId,attacker=actor.id,weapon=swingWeapon,level=swingLevel,
                originX=segment.start.x,originY=segment.start.y,endX=segment.end.x,endY=segment.end.y,
                speed=segment.speed,radius=0.08f }));
            var tip=swingSweep.Tip;
            actor.weaponTipX=tip.x;actor.weaponTipY=tip.y;
            actor.weaponReach=Vector2.Distance(tip,origin);
            actor.weaponAngle=Mathf.Atan2(tip.y-origin.y,tip.x-origin.x)*Mathf.Rad2Deg;
            if(swingSweep.Finished) swingActor=null;
        }

        private void ResolveExplosions()
        {
            if(resolvingExplosions || session.World==null || session.LocalOwner==null) return;
            resolvingExplosions=true;
            try
            {
                foreach(var explosion in session.World.explosions.ToArray())
                foreach(var actor in session.World.actors.Where(a=>ActorPresence.IsPresent(a) && a.owner==session.LocalOwner).ToArray())
                {
                    if(session.Pause.IsPaused(actor.level) || actor.resolvedExplosions.Contains(explosion.attack.id) ||
                        !(actor.player?explosion.players:explosion.enemies).Contains(actor.id)) continue;
                    var local=LocalExplosionPosition?.Invoke(explosion.attack.id,explosion.generation);
                    if(actor.player && !local.HasValue)
                        Debug.LogWarning($"Восстановленное решение о взрыве без локального полёта: grenade={explosion.attack.id}, actor={actor.id}, generation={explosion.generation}.");
                    if(GrenadeRules.Resolve(actor,explosion,damage,local ?? explosion.attack.End)) session.PublishActor(actor);
                }
            }
            finally {resolvingExplosions=false;}
        }

        private void ReceiveAttack(AttackEvent attack)
        {
            if (session.World == null) return;
            if(attack.weapon=="grenade") return; // Persisted explosion decisions are processed from World.
            if(!LevelActivity.HasConnectedPlayer(session.World,attack.level)) return;
            var definition=WeaponCatalog.Get(attack.weapon);
            if(definition.projectileSpeed>0) return;
            var terrain=ShotObstacles.FirstContact(geometry.Get(attack.level),attack.Origin,attack.End,out var surface,attack.radius);
            if(terrain.HasValue)
            {
                var position=Vector2.Lerp(attack.Origin,attack.End,terrain.Value);
                position=new Vector2(Mathf.Clamp(position.x,surface.x,surface.x+surface.width),
                    Mathf.Clamp(position.y,surface.y,surface.y+surface.height));
                if(session.Pause.IsPaused(attack.level)) pending.Add(attack.level,()=>SurfaceContactObserved?.Invoke(attack,surface.texture,position));
                else SurfaceContactObserved?.Invoke(attack,surface.texture,position);
            }
            float limit=definition.stopAtTerrain?terrain??1:1;
            foreach(var prop in WorldProps.All(session.World))
            {
                if(prop.level!=attack.level) continue;
                var bounds=new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height);
                var contact=SurfaceContact.Entry(bounds,attack.Origin,attack.End,attack.radius);
                if(!contact.HasValue || contact.Value>limit) continue;
                var center=Vector2.Lerp(attack.Origin,attack.End,contact.Value);
                var point=new Vector2(Mathf.Clamp(center.x,bounds.xMin,bounds.xMax),Mathf.Clamp(center.y,bounds.yMin,bounds.yMax));
                string id=prop.id,type=prop.definition;
                if(session.Pause.IsPaused(attack.level)) pending.Add(attack.level,()=>PropContactObserved?.Invoke(attack,id,type,point));
                else PropContactObserved?.Invoke(attack,id,type,point);
            }
            foreach(var target in AttackContacts.Capture(session.World.actors,attack,session.World.friendlyFire,
                definition.stopAtFirstActor,limit))
            {
                if(session.Pause.IsPaused(attack.level)) pending.Add(attack.level,()=>ApplyAttackTarget(attack,target));
                else ApplyAttackTarget(attack,target);
            }
        }
        private void ApplyAttackTarget(AttackEvent attack,AttackTargetSnapshot target,string decisionOwner=null)
        {
                var actor=session.World?.Find(target.id);
                if(!target.CanApply(actor) || !AttackScope.CanAffect(attack,actor,session.World.friendlyFire)) return;
                bool contact=target.contact;
                if(contact) ContactObserved?.Invoke(attack,actor);
                if (actor.player)
                {
                    if(FriendlyFireDecision.UsesAttacker(attack,actor))
                    {
                        if((decisionOwner??attack.sourceOwner)==session.LocalOwner && contact)
                            session.ReportFriendlyHit(new FriendlyHitMessage {target=actor.id,generation=actor.generation,lifecycle=actor.lifecycleSequence,attackJson=JsonUtility.ToJson(attack)});
                        return;
                    }
                    if (actor.owner == session.LocalOwner && damage.Apply(actor, attack, contact))
                    {
                        VictimDecidedHits++;
                        session.PublishActor(actor);
                        Debug.Log($"Урон принят пострадавшим: {actor.id}, событие={attack.id}, здоровье={actor.health}, нокдаун={actor.down}.");
                    }
                }
                else if ((decisionOwner??attack.sourceOwner) == session.LocalOwner && contact)
                {
                    session.ReportNpcHit(new NpcHitMessage { target = actor.id, generation = actor.generation,
                        attackJson = JsonUtility.ToJson(attack) });
                }
        }

        private void ReceiveNpcHit(NpcHitMessage message)
        {
            var actor = session.World?.Find(message.target);
            if (actor == null || actor.player || actor.owner != session.LocalOwner || actor.generation != message.generation) return;
            if(session.Pause.IsPaused(actor.level)) {pending.Add(actor.level,()=>ReceiveNpcHit(message));return;}
            var attack = JsonUtility.FromJson<AttackEvent>(message.attackJson);
            if(actor.resolvedAttacks.Contains(attack.id)) {NpcReplayRejected?.Invoke(attack.id);return;}
            if (damage.Apply(actor, attack, true)) session.PublishActor(actor);
        }
        private void ReceiveFriendlyHit(FriendlyHitMessage message)
        {
            var actor=session.World?.Find(message.target);
            if(actor==null || actor.owner!=session.LocalOwner || actor.generation!=message.generation || actor.lifecycleSequence!=message.lifecycle) return;
            if(session.Pause.IsPaused(actor.level)) {pending.Add(actor.level,()=>ReceiveFriendlyHit(message));return;}
            var attack=JsonUtility.FromJson<AttackEvent>(message.attackJson);
            if(!FriendlyFireDecision.UsesAttacker(attack,actor) || !AttackScope.CanAffect(attack,actor,session.World.friendlyFire)) return;
            if(damage.Apply(actor,attack,true)) {AttackerDecidedHits++;session.PublishActor(actor);}
        }
    }
}
