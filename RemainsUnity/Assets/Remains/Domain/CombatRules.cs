using System.Collections.Generic;
using UnityEngine;
using Remains.Content;

namespace Remains.Domain
{
    public interface IDamageRule
    {
        float Calculate(string weapon, float contactSpeed);
    }

    public sealed class PrototypeDamageRule : IDamageRule
    {
        public float Calculate(string weapon, float contactSpeed)
        {
            var definition=WeaponCatalog.Get(weapon);
            return definition.damage+Mathf.Clamp(contactSpeed*definition.speedDamage,0,definition.maxSpeedDamage);
        }
    }

    public sealed class DamageReceiver
    {
        private readonly HashSet<string> applied = new();
        private readonly IDamageRule rules;
        public DamageReceiver(IDamageRule rules) => this.rules = rules;

        public bool Apply(ActorState actor, AttackEvent attack, bool contact)
        {
            if (!contact || actor.health <= 0 || !applied.Add(attack.id + ":" + actor.id)) return false;
            actor.health = Mathf.Max(0, actor.health - rules.Calculate(attack.weapon, attack.speed));
            actor.down |= actor.health <= 0;
            actor.revision++;
            if (actor.down) ActorPose.Select(actor,"down");
            return true;
        }
    }

    public static class HitGeometry
    {
        // One geometric contract for hitscan, swept melee and regional extensions.
        public static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            var delta = end - start;
            var t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude) : 0;
            return Vector2.Distance(point, start + delta * t);
        }

        public static bool Contacts(ActorState target, AttackEvent attack)
        {
            return Anatomy.Contact(target,attack)!=null;
        }
    }
}
