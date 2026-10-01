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
        private Vector2 swingDirection,previousTip;
        private float swingElapsed;
        private string swingId,swingWeapon;
        private string swingLevel;
        private int swingGeneration,swingLifecycle;
        public string Weapon { get; set; } = "pistol";
        public bool IsMeleeActive=>swingActor!=null;
        public WeaponReloadController Reload { get; }
        private float nextResourceNotice;
        private string selectedActor;
        private int selectedGeneration;
        public Func<string,Vector2?> LocalExplosionPosition { get; set; }
        public CombatController(CoopSession session)
        {
            this.session = session;
            Reload=new WeaponReloadController(session);
            session.AttackReceived += ReceiveAttack;
            session.NpcHitReceived += ReceiveNpcHit;
            session.Changed+=RestoreSelection;
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
                swingActor=attacker;swingWeapon=Weapon;swingDirection=direction;swingElapsed=0;
                swingLevel=attacker.level;swingGeneration=attacker.generation;swingLifecycle=attacker.lifecycleSequence;
                swingId=Guid.NewGuid().ToString("N");previousTip=WeaponPose.Tip(Weapon,origin,direction,0);
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
            swingElapsed=Mathf.Min(0.3f,swingElapsed+dt);
            var origin=actor.Position+new Vector2(0,0.8f);
            var tip=WeaponPose.Tip(swingWeapon,origin,swingDirection,swingElapsed/0.3f);
            var delta=tip-previousTip;
            actor.weaponTipX=tip.x;actor.weaponTipY=tip.y;
            actor.weaponReach=Vector2.Distance(tip,origin);
            actor.weaponAngle=Mathf.Atan2(tip.y-origin.y,tip.x-origin.x)*Mathf.Rad2Deg;
            session.Fire(new AttackEvent { id=swingId,attacker=actor.id,weapon=swingWeapon,level=swingLevel,
                originX=previousTip.x,originY=previousTip.y,endX=tip.x,endY=tip.y,
                speed=delta.magnitude/Mathf.Max(0.001f,dt),radius=0.08f });
            previousTip=tip;
            if(swingElapsed>=0.3f) swingActor=null;
        }

        public void Explode(GrenadeMessage grenade, Vector2 location)
        {
            session.Fire(new AttackEvent { id = grenade.id, attacker = grenade.attacker, weapon = "grenade",level=grenade.level,
                originX = grenade.x, originY = grenade.y, endX = location.x, endY = location.y });
        }

        private void ReceiveAttack(AttackEvent attack)
        {
            if (session.World == null) return;
            if(attack.weapon=="grenade")
            {
                var position=LocalExplosionPosition?.Invoke(attack.id);
                if(position.HasValue) { attack.endX=position.Value.x;attack.endY=position.Value.y; }
            }
            foreach (var actor in session.World.actors.ToArray())
            {
                if (actor.id == attack.attacker) continue;
                if(!AttackScope.Includes(attack,actor)) continue;
                bool contact = HitGeometry.Contacts(actor, attack);
                if (actor.player)
                {
                    if (!session.World.friendlyFire && attack.sourcePlayer) continue;
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
