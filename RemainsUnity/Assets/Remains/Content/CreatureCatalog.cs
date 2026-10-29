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
        public const string RaiderVariants="raider";
        [InspectorName("ID объекта шаблона")] public string objectId;
        [InspectorName("Типы персонажей (варианты)")] public string[] actorTypes=Array.Empty<string>();
        // Set: the variant follows the room's danger (Location.randomCid) and the weapon its list (Unit.getXmlWeapon);
        // empty: a stable choice among actorTypes per placement.
        [InspectorName("Выбор варианта по опасности (randomCid)")] public string variantRule;
        [InspectorName("Варианты по опасности")] public CreatureVariant[] variants=Array.Empty<CreatureVariant>();
    }
    // A variant of a danger-chosen object (raiderN) with its weapon list in the original's order.
    [Serializable] public sealed class CreatureVariant
    {
        [InspectorName("ID существа оригинала")] public string unit;
        [InspectorName("Номер варианта (cid)")] public int cid;
        [InspectorName("Оружие (w) по порядку")] public CreatureWeaponChoice[] weapons=Array.Empty<CreatureWeaponChoice>();
        [InspectorName("Почему ещё не подключён (пусто — подключён)")] public string gap;
    }
    [Serializable] public sealed class CreatureWeaponChoice
    {
        [InspectorName("Оружие оригинала")] public string weapon;
        [InspectorName("Шанс (ch; 0 — без проверки)")] public float chance;
        [InspectorName("С опасности (dif)")] public int difficulty;
        [InspectorName("Тип персонажа с этим оружием")] public string actorType;
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

        // The actor type a danger-chosen object becomes here, or null with the reason (a variant or weapon not playable).
        public static string ChooseVariant(CreatureObjectRule rule,double danger,Func<double> random,out string reason)
        {
            reason=null;
            if(rule.variantRule!=CreatureObjectRule.RaiderVariants) throw new InvalidOperationException($"Объект {rule.objectId}: неизвестный выбор варианта {rule.variantRule}.");
            int cid=Remains.Domain.RandomEncounterRules.RaiderVariant(danger,random);
            CreatureVariant variant=null;
            foreach(var candidate in rule.variants) if(candidate.cid==cid) variant=candidate;
            if(variant==null) {reason=$"нет варианта {rule.objectId}{cid}";return null;}
            if(!string.IsNullOrEmpty(variant.gap)) {reason=$"{variant.unit}: {variant.gap}";return null;}
            // Unit.getXmlWeapon(int locDif): the first listed weapon allowed at this danger that passes its chance.
            foreach(var choice in variant.weapons)
            {
                if(choice.difficulty>(int)danger) continue;
                if(choice.chance>0 && random()>=choice.chance) continue;
                if(string.IsNullOrEmpty(choice.actorType)) {reason=$"{variant.unit}: оружие {choice.weapon} ещё не подключено";return null;}
                return choice.actorType;
            }
            reason=$"{variant.unit}: без оружия (рукопашный без оружия ещё не реализован)";return null;
        }

        public string GapReason(string sourceId)
        {
            foreach(var gap in gaps) if(gap.sourceId==sourceId) return gap.reason;
            return null;
        }

        public static CreatureCatalog Load()=>GameAssets.Load<CreatureCatalog>(Resource)??
            throw new InvalidOperationException("Нет каталога существ "+Resource+"; запустите генерацию существ (CreatureAuthoring).");
    }
}
