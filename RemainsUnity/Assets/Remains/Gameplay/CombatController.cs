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
        private readonly DamageReceiver damage = new(new PrototypeDamageRule());
        private float nextShot;
        private ActorState swingActor;
        private MeleeSweep swingSweep;
        private string swingId,swingWeapon;
        private string swingLevel;
        private int swingGeneration,swingLifecycle;
        public string Weapon { get; set; } = "pistol";
        public bool IsMeleeActive=>swingActor!=null;
        public WeaponReloadController Reload { get; }
        private float nextResourceNotice;
        private string selectedActor;
        private int selectedGeneration;
        private bool resolvingExplosions;
        public Func<string,int,Vector2?> LocalExplosionPosition { get; set; }
        public CombatController(CoopSession session)
        {
            this.session = session;
            Reload=new WeaponReloadController(session);
            session.AttackReceived += ReceiveAttack;
            session.NpcHitReceived += ReceiveNpcHit;
            session.Changed+=RestoreSelection;
            session.Changed+=ResolveExplosions;
        }

        private void RestoreSelection()
        {
            var actor=session.LocalActor;
            if(actor==null) {selectedActor=null;return;}
            if(selectedActor==actor.id && selectedGeneration==actor.generation) return;
            selectedActor=actor.id;selectedGeneration=actor.generation;
            Weapon=actor.weapon;nextShot=0;swingActor=null;
        }

        public bool Fire(ActorState attacker, Vector2 aim)
        {
            if (attacker == null || attacker.down || Reload.Active || Time.time < nextShot) return false;
            var definition=WeaponCatalog.Get(Weapon);
            if(!WeaponAmmunition.SpendShot(attacker,definition,out string reason))
            {
                if(Time.unscaledTime>=nextResourceNotice)
                {nextResourceNotice=Time.unscaledTime+1;session.ShowNotice(reason);}
                return false;
            }
            if(definition.manaCost>0 || !string.IsNullOrEmpty(definition.ammo))
            {attacker.revision++;session.PublishActor(attacker);}
            nextShot = Time.time + definition.cooldown;
            Vector2 origin = attacker.Position + new Vector2(0,0.55f);
            Vector2 direction = (aim-origin).normalized;
            if (direction == Vector2.zero) direction = new Vector2(attacker.facing,0);
            attacker.weapon=Weapon;
            attacker.weaponAngle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
            if(Weapon is "spear" or "crowbar")
            {
                swingActor=attacker;swingWeapon=Weapon;
                swingLevel=attacker.level;swingGeneration=attacker.generation;swingLifecycle=attacker.lifecycleSequence;
                swingId=Guid.NewGuid().ToString("N");swingSweep=new MeleeSweep(Weapon,WeaponPose.Origin(attacker.Position),direction);
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
            Reload.Step(Weapon,dt);
            if(swingActor==null) return;
            var actor=session.World?.Find(swingActor.id);
            if(actor==null || actor.down || actor.owner!=session.LocalOwner || actor.level!=swingLevel ||
                actor.generation!=swingGeneration || actor.lifecycleSequence!=swingLifecycle) { swingActor=null;return; }
            var origin=WeaponPose.Origin(actor.Position);
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
                    if(actor.resolvedExplosions.Contains(explosion.attack.id) ||
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
            foreach (var actor in session.World.actors.ToArray())
            {
                if(!AttackScope.CanAffect(attack,actor,session.World.friendlyFire)) continue;
                bool contact = HitGeometry.Contacts(actor, attack);
                if (actor.player)
                {
                    if (actor.owner == session.LocalOwner && damage.Apply(actor, attack, contact))
                    {
                        session.PublishActor(actor);
                        Debug.Log($"Урон принят пострадавшим: {actor.id}, событие={attack.id}, здоровье={actor.health}, нокдаун={actor.down}.");
                    }
                }
                else if (attack.sourceOwner == session.LocalOwner && contact)
                {
                    session.ReportNpcHit(new NpcHitMessage { target = actor.id, generation = actor.generation,
                        attackJson = JsonUtility.ToJson(attack) });
                }
            }
        }

        private void ReceiveNpcHit(NpcHitMessage message)
        {
            var actor = session.World?.Find(message.target);
            if (actor == null || actor.owner != session.LocalOwner || actor.generation != message.generation) return;
            var attack = JsonUtility.FromJson<AttackEvent>(message.attackJson);
            if (damage.Apply(actor, attack, true)) session.PublishActor(actor);
        }
    }
}
