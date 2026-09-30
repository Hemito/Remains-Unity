using System;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class CombatController
    {
        private readonly CoopSession session;
        private readonly DamageReceiver damage = new(new PrototypeDamageRule());
        private float nextShot;
        public string Weapon { get; set; } = "pistol";
        public CombatController(CoopSession session)
        {
            this.session = session;
            session.AttackReceived += ReceiveAttack;
            session.NpcHitReceived += ReceiveNpcHit;
        }

        public bool Fire(ActorState attacker, Vector2 aim)
        {
            if (attacker == null || attacker.down || Time.time < nextShot) return false;
            nextShot = Time.time + (Weapon == "energy" ? 0.45f : 0.3f);
            Vector2 origin = attacker.Position + new Vector2(0,0.55f);
            Vector2 direction = (aim-origin).normalized;
            if (direction == Vector2.zero) direction = new Vector2(attacker.facing,0);
            float range = Weapon is "spear" or "crowbar" ? 1.5f : 20;
            session.Fire(new AttackEvent { id = Guid.NewGuid().ToString("N"), attacker = attacker.id, weapon = Weapon,
                originX = origin.x, originY = origin.y, endX = origin.x + direction.x * range,
                endY = origin.y + direction.y * range, speed = attacker.Velocity.magnitude + range / 0.15f });
            return true;
        }

        public void Explode(ActorState attacker, Vector2 location)
        {
            session.Fire(new AttackEvent { id = Guid.NewGuid().ToString("N"), attacker = attacker.id, weapon = "grenade",
                originX = attacker.x, originY = attacker.y, endX = location.x, endY = location.y });
        }

        private void ReceiveAttack(AttackEvent attack)
        {
            if (session.World == null) return;
            foreach (var actor in session.World.actors.ToArray())
            {
                if (actor.id == attack.attacker) continue;
                bool contact = HitGeometry.Contacts(actor, attack);
                if (actor.player)
                {
                    if (!session.World.friendlyFire && session.World.Find(attack.attacker)?.player == true) continue;
                    if (actor.owner == session.LocalOwner && damage.Apply(actor, attack, contact))
                    {
                        session.PublishActor(actor);
                        Debug.Log($"Урон принят пострадавшим: {actor.id}, событие={attack.id}, здоровье={actor.health}, нокдаун={actor.down}.");
                    }
                }
                else if (session.World.Find(attack.attacker)?.owner == session.LocalOwner && contact)
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
