using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class CreatureEntry
    {
        [InspectorName("ID существа оригинала")] public string sourceId;
        [InspectorName("Тип персонажа")] public string actorType;
        [InspectorName("Семейство поведения (класс оригинала)")] public string family;
        [InspectorName("Начальное состояние")] public NpcSpawnAsset spawn;
        [InspectorName("Замечания генерации")] public string[] notes=Array.Empty<string>();
    }
    [Serializable] public sealed class CreatureGap
    {
        [InspectorName("ID существа оригинала")] public string sourceId;
        [InspectorName("Почему ещё не подключено")] public string reason;
    }

    // Creatures generated from the original data (Editor: CreatureAuthoring): which original unit ID becomes which
    // actor type, and why the others are not playable yet. Level adapters place only the listed ones and report
    // the gaps; nothing is substituted.
    public sealed class CreatureCatalog : ScriptableObject
    {
        public const string Resource="Content/CreatureCatalog";
        [SerializeField,InspectorName("Подключённые существа")] private CreatureEntry[] creatures=Array.Empty<CreatureEntry>();
        [SerializeField,InspectorName("Ещё не подключённые")] private CreatureGap[] gaps=Array.Empty<CreatureGap>();
        public IReadOnlyList<CreatureEntry> Creatures=>creatures;
        public IReadOnlyList<CreatureGap> Gaps=>gaps;
        public void Configure(CreatureEntry[] entries,CreatureGap[] missing) {creatures=entries;gaps=missing;}

        public bool TryGet(string sourceId,out CreatureEntry entry)
        {
            foreach(var candidate in creatures) if(candidate.sourceId==sourceId) {entry=candidate;return true;}
            entry=null;return false;
        }
        public string GapReason(string sourceId)
        {
            foreach(var gap in gaps) if(gap.sourceId==sourceId) return gap.reason;
            return null;
        }

        public static CreatureCatalog Load()=>Resources.Load<CreatureCatalog>(Resource)??
            throw new InvalidOperationException("Нет каталога существ "+Resource+"; запустите генерацию существ (CreatureAuthoring).");
    }
}
