using System;
using System.Collections.Generic;
using Remains.Serialization;

namespace Remains.Domain
{
    // Accepted transport envelopes, not simulated hits. Multiple melee sweeps keep one attack ID.
    public sealed class DoorDamageAttackLedger
    {
        private sealed class Entry
        {
            public string owner,actor,level,weapon;
            public int generation,lifecycle;
            public readonly HashSet<string> segments=new();
        }
        public const int MaximumAttacks=256,MaximumSegmentsPerAttack=256;
        private readonly Dictionary<string,Entry> attacks=new();
        private readonly Queue<string> order=new();
        private string world;
        public void Clear() {attacks.Clear();order.Clear();world=null;}
        public string Record(string worldId,ActorState source,AttackEvent attack)
        {
            if(string.IsNullOrWhiteSpace(worldId) || source==null || attack==null || string.IsNullOrWhiteSpace(attack.id) ||
                source.id!=attack.attacker || source.owner!=attack.sourceOwner || source.level!=attack.level)
                return "Не совпала принадлежность принятой атаки закреплённому объекту.";
            if(world!=worldId) {Clear();world=worldId;}
            if(!attacks.TryGetValue(attack.id,out var entry))
            {
                if(attacks.Count>=MaximumAttacks) attacks.Remove(order.Dequeue());
                entry=new Entry {owner=source.owner,actor=source.id,level=source.level,weapon=attack.weapon,
                    generation=source.generation,lifecycle=source.lifecycleSequence};
                attacks.Add(attack.id,entry);order.Enqueue(attack.id);
            }
            if(entry.owner!=source.owner || entry.actor!=source.id || entry.level!=source.level || entry.weapon!=attack.weapon ||
                entry.generation!=source.generation || entry.lifecycle!=source.lifecycleSequence)
                return "ID атаки повторно использован другой сущностью или эпохой.";
            string segment=StateJson.ToJson(attack);
            if(entry.segments.Contains(segment)) return null;
            if(entry.segments.Count>=MaximumSegmentsPerAttack) return "Превышено число принятых сегментов одной атаки.";
            entry.segments.Add(segment);return null;
        }
        public bool Contains(string worldId,ActorState source,AttackEvent attack)=>world==worldId && source!=null && attack!=null &&
            attacks.TryGetValue(attack.id,out var entry) && entry.owner==source.owner && entry.actor==source.id &&
            entry.generation==source.generation && entry.lifecycle==source.lifecycleSequence && entry.segments.Contains(StateJson.ToJson(attack));
    }
}
