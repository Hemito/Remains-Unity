using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Domain;
namespace Remains.Content
{
    [Serializable] public sealed class PropDefinition
    {public string id,name,resource;public float mass,width,height;public Vector2 interactionOffset;public bool movable=true,hasPhysicalBody=true;}
    [Serializable] public sealed class PropDefinitionList {public PropDefinition[] props;}
    public static class PropCatalog
    {
        private static readonly Dictionary<string,PropDefinition> definitions=new();
        // Optional cursor overrides: absence explicitly means the saved body's rectangle.
        private static readonly Dictionary<string,Vector2> interactionOffsets=new();
        public static Vector2 InteractionOffset(string id)=>id!=null && interactionOffsets.TryGetValue(id,out var offset)?offset:Vector2.zero;
        public static IEnumerable<PropDefinition> All=>definitions.Values;
        public static PropDefinition Get(string id)=>id!=null && definitions.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException("Не найден тип физического объекта: "+id);
        public static PropState Create(string id,string definition,string level,Vector2 position)
        {
            if(string.IsNullOrEmpty(id) || string.IsNullOrEmpty(level) || !float.IsFinite(position.x) || !float.IsFinite(position.y))
                throw new InvalidOperationException($"Некорректное размещение объекта: id={id}, type={definition}, level={level}.");
            var source=Get(definition);
            var state=new PropState {id=id,definition=definition,level=level,x=position.x,y=position.y,
                mass=source.mass,width=source.width,height=source.height,movable=source.movable,hasPhysicalBody=source.hasPhysicalBody,
                anchorHeight=source.hasPhysicalBody?source.height/2:0};
            PropBodyRules.Validate(state);return state;
        }
        public static void Load()
        {
            var entries=EntityAssetCatalog.Load().CreateProps();
            definitions.Clear();interactionOffsets.Clear();foreach(var definition in entries) Register(definition);
        }
        public static void Register(PropDefinition definition,bool replace=false)
        {
            Validate(definition);
            if(!replace && definitions.ContainsKey(definition.id)) throw new InvalidOperationException("Повтор типа физического объекта: "+definition.id);
            definitions[definition.id]=definition;
            if(definition.interactionOffset!=Vector2.zero) interactionOffsets[definition.id]=definition.interactionOffset;
            else interactionOffsets.Remove(definition.id);
        }
        internal static void Validate(PropDefinition definition)
        {
            if(definition==null || string.IsNullOrEmpty(definition.id) || string.IsNullOrEmpty(definition.resource) ||
                !float.IsFinite(definition.width) || definition.width<=0 ||
                !float.IsFinite(definition.height) || definition.height<=0)
                throw new InvalidOperationException("Некорректное определение физического объекта: "+definition?.id);
            if(!float.IsFinite(definition.interactionOffset.x) || !float.IsFinite(definition.interactionOffset.y))
                throw new InvalidOperationException("Некорректное смещение взаимодействия: тип="+definition.id);
            PropBodyRules.Validate(definition.movable,definition.hasPhysicalBody,definition.mass,"тип="+definition.id);
        }
    }
}
