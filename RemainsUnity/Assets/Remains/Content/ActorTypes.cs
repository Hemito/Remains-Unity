using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class ActorTypeDefinition
    {
        public string id,presentation,artwork;
        public bool combatAi,hasWeapon,bodyInventory;
        [InspectorName("Оружие на точке кадра атласа")] public bool atlasWeaponSocket;
        [InspectorName("Может пользоваться дверями")] public bool canUseDoors;
        [Min(0),InspectorName("Масса тела, кг")] public float bodyMassKg;
        [InspectorName("Разрешён перенос телекинезом")] public bool telekinesisMovable;
        public float restoreHealthAfterHit;
        [Min(0),InspectorName("Поглощение урона от попадания")] public float hitProtection;
        [InspectorName("Восприятие (для боевого AI)")] public Remains.Domain.NpcPerceptionSettings perception;
        [SerializeReference,InspectorName("Естественная атака (пусто — оружейный AI)")] public Remains.Domain.NaturalAttackDefinition naturalAttack;
        [InspectorName("Поведение (семейство существа оригинала; пусто — ползун)")] public string behavior;
        [InspectorName("Движение существа (летуны, слизь)")] public Remains.Domain.CreatureMotionSettings motion;
        // Original Unit.udarBullet material: flesh (10), metal (1), slime (12). Hit sounds are chosen by it when the
        // audio catalogs have no entry for this very type. Empty: the type has its own entries.
        [InspectorName("Материал попадания (flesh/metal/slime)")] public string hitMaterial;
        // Original Unit.udarUnit by the attacker's damage type (fang_hit, acid, hit_flesh...): one is chosen per touch.
        [InspectorName("Звуки атаки касанием (AudioClipSource)")] public string[] attackSounds;
        // Original Unit.activateTrap (AllData param acttrap): 2 springs traps (players and most units), 1 is a small
        // creature that only players' mines notice, 0 springs nothing. Unset in older assets: 2, the original default.
        [Range(0,2),InspectorName("Срабатывание ловушек (acttrap: 2 — да, 1 — мелкое, 0 — нет)")] public int activateTrap=2;
    }
    [Serializable] public sealed class ActorTypeList {public ActorTypeDefinition[] actors;}
    public static class ActorTypes
    {
        private static readonly Dictionary<string,ActorTypeDefinition> definitions=new(StringComparer.Ordinal);
        public static ActorTypeDefinition Get(string id)=>id!=null && definitions.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException($"Не найден тип актёра: {id ?? "<null>"}.");
        public static void Load()
        {
            var entries=EntityAssetCatalog.Load().CreateActors();
            definitions.Clear();foreach(var definition in entries) Register(definition);
        }
        public static void Register(ActorTypeDefinition definition,bool replace=false)
        {
            Validate(definition);
            AtlasWeaponSockets.Prepare(definition);
            if(!replace && definitions.ContainsKey(definition.id)) throw new InvalidOperationException($"Повтор типа актёра: {definition.id}.");
            definitions[definition.id]=definition;
        }
        internal static void Validate(ActorTypeDefinition definition)
        {
            if(definition==null || string.IsNullOrWhiteSpace(definition.id) || string.IsNullOrWhiteSpace(definition.presentation) ||
                !float.IsFinite(definition.restoreHealthAfterHit) || definition.restoreHealthAfterHit<0 ||
                !float.IsFinite(definition.hitProtection) || definition.hitProtection<0 ||
                !float.IsFinite(definition.bodyMassKg) || definition.bodyMassKg<0 ||
                definition.telekinesisMovable && definition.bodyMassKg<=0)
                throw new InvalidOperationException($"Некорректное определение актёра: {definition?.id}.");
            if(definition.atlasWeaponSocket && (!definition.hasWeapon || definition.presentation!="atlas" || string.IsNullOrWhiteSpace(definition.artwork)))
                throw new InvalidOperationException($"Тип {definition.id}: точка оружия требует оружие и атласное представление с указанным ресурсом.");
            if(definition.combatAi)
            {
                if(definition.perception==null) throw new InvalidOperationException($"У боевого типа {definition.id} нет настроек восприятия.");
                definition.perception.Validate();
            }
            if(!Remains.Domain.NpcCreatureBehaviours.Valid(definition.behavior))
                throw new InvalidOperationException($"Тип {definition.id}: неизвестное поведение {definition.behavior}.");
            if(Remains.Domain.NpcCreatureBehaviours.Fixed(definition.behavior) && (!definition.combatAi || !definition.hasWeapon || !definition.atlasWeaponSocket || definition.naturalAttack!=null))
                throw new InvalidOperationException($"Тип {definition.id}: турель требует боевой ИИ с оружием на точке атласа.");
            if(Remains.Domain.NpcCreatureBehaviours.Touches(definition.behavior))
            {
                if(definition.naturalAttack==null || definition.motion==null) throw new InvalidOperationException($"Тип {definition.id}: поведение {definition.behavior} требует атаку касанием и движение.");
                definition.motion.Validate(definition.id);
            }
            if(definition.naturalAttack!=null)
            {
                if(!definition.combatAi || definition.hasWeapon || definition.atlasWeaponSocket)
                    throw new InvalidOperationException("Естественная атака требует безоружный боевой тип: "+definition.id);
                definition.naturalAttack.Validate(definition.id);
            }
        }
    }
}
