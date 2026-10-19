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
        [InspectorName("Добыча после принятой гибели (отдельно от установленного оружия)")] public ContainerLootAsset deathLoot;
        [InspectorName("Исходная высота scY для точки добычи, м")] public float deathLootHeight;
        [InspectorName("Замечания генерации")] public string[] notes=Array.Empty<string>();
    }
    // A template object (AllData obj) and the creatures it becomes; several = the original's random variant.
    [Serializable] public sealed class CreatureObjectRule
    {
        [InspectorName("ID объекта шаблона")] public string objectId;
        [InspectorName("Типы персонажей (варианты)")] public string[] actorTypes=Array.Empty<string>();
    }
    [Serializable] public sealed class CreatureGap
    {
        [InspectorName("ID объекта шаблона")] public string sourceId;
        [InspectorName("Почему ещё не подключено")] public string reason;
    }

    // Creatures generated from the original data (Editor: CreatureAuthoring): which original unit ID becomes which
    // actor type, and why the others are not playable yet. Level adapters place only the listed ones and report
    // the gaps; nothing is substituted.
    public sealed class CreatureCatalog : ScriptableObject
    {
        public const string Resource="Content/CreatureCatalog";
        [SerializeField,InspectorName("Подключённые существа")] private CreatureEntry[] creatures=Array.Empty<CreatureEntry>();
        [SerializeField,InspectorName("Объекты шаблонов")] private CreatureObjectRule[] objects=Array.Empty<CreatureObjectRule>();
        [SerializeField,InspectorName("Ещё не подключённые объекты")] private CreatureGap[] gaps=Array.Empty<CreatureGap>();
        public IReadOnlyList<CreatureEntry> Creatures=>creatures;
        public IReadOnlyList<CreatureGap> Gaps=>gaps;
        public IReadOnlyList<CreatureObjectRule> Objects=>objects;
        public void Configure(CreatureEntry[] entries,CreatureObjectRule[] rules,CreatureGap[] missing) {creatures=entries;objects=rules;gaps=missing;}
        public bool TryObject(string objectId,out CreatureObjectRule rule)
        {
            foreach(var candidate in objects) if(candidate.objectId==objectId) {rule=candidate;return true;}
            rule=null;return false;
        }
        public CreatureEntry Require(string actorType)
        {
            foreach(var candidate in creatures) if(candidate.actorType==actorType) return candidate;
            throw new InvalidOperationException("Каталог существ: нет типа "+actorType);
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
