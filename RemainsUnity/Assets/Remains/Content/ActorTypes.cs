using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class ActorTypeDefinition
    {
        public string id,presentation,artwork;
        public bool combatAi,hasWeapon,bodyInventory;
        [Min(0),InspectorName("Масса тела, кг")] public float bodyMassKg;
        [InspectorName("Разрешён перенос телекинезом")] public bool telekinesisMovable;
        public float restoreHealthAfterHit;
        [Min(0),InspectorName("Поглощение урона от попадания")] public float hitProtection;
        [InspectorName("Восприятие (для боевого AI)")] public Remains.Domain.NpcPerceptionSettings perception;
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
            if(definition.combatAi)
            {
                if(definition.perception==null) throw new InvalidOperationException($"У боевого типа {definition.id} нет настроек восприятия.");
                definition.perception.Validate();
            }
        }
    }
}
