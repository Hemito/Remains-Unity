using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class ActorTypeDefinition
    {
        public string id,presentation,artwork;
        public bool combatAi,hasWeapon,bodyInventory;
        public float restoreHealthAfterHit;
    }
    [Serializable] public sealed class ActorTypeList {public ActorTypeDefinition[] actors;}
    public static class ActorTypes
    {
        private static readonly Dictionary<string,ActorTypeDefinition> definitions=new(StringComparer.Ordinal);
        public static ActorTypeDefinition Get(string id)=>id!=null && definitions.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException($"Не найден тип актёра: {id ?? "<null>"}.");
        public static void Load()
        {
            var asset=Resources.Load<TextAsset>("Content/actors");
            if(asset==null) throw new InvalidOperationException("Нет определений актёров Content/actors.");
            var list=JsonUtility.FromJson<ActorTypeList>(asset.text);
            if(list?.actors==null) throw new InvalidOperationException("Нет списка типов актёров.");
            definitions.Clear();foreach(var definition in list.actors) Register(definition);
        }
        public static void Register(ActorTypeDefinition definition,bool replace=false)
        {
            if(definition==null || string.IsNullOrWhiteSpace(definition.id) || string.IsNullOrWhiteSpace(definition.presentation) ||
                !float.IsFinite(definition.restoreHealthAfterHit) || definition.restoreHealthAfterHit<0)
                throw new InvalidOperationException($"Некорректное определение актёра: {definition?.id}.");
            if(!replace && definitions.ContainsKey(definition.id)) throw new InvalidOperationException($"Повтор типа актёра: {definition.id}.");
            definitions[definition.id]=definition;
        }
    }
}
