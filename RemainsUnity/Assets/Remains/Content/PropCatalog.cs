using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class PropDefinition
    {public string id,name,resource;public float mass,width,height;}
    [Serializable] public sealed class PropDefinitionList {public PropDefinition[] props;}
    public static class PropCatalog
    {
        private static readonly Dictionary<string,PropDefinition> definitions=new();
        public static IEnumerable<PropDefinition> All=>definitions.Values;
        public static PropDefinition Get(string id)=>id!=null && definitions.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException("Не найден тип физического объекта: "+id);
        public static void Load()
        {
            var asset=Resources.Load<TextAsset>("Content/props");
            if(asset==null) throw new InvalidOperationException("Нет каталога Content/props.");
            var list=JsonUtility.FromJson<PropDefinitionList>(asset.text);
            if(list?.props==null) throw new InvalidOperationException("Нет определений физических объектов.");
            definitions.Clear();foreach(var definition in list.props) Register(definition);
        }
        public static void Register(PropDefinition definition,bool replace=false)
        {
            if(definition==null || string.IsNullOrEmpty(definition.id) || string.IsNullOrEmpty(definition.resource) ||
                !float.IsFinite(definition.mass) || definition.mass<=0 || !float.IsFinite(definition.width) || definition.width<=0 ||
                !float.IsFinite(definition.height) || definition.height<=0 || !replace && definitions.ContainsKey(definition.id))
                throw new InvalidOperationException("Некорректное определение физического объекта: "+definition?.id);
            definitions[definition.id]=definition;
        }
    }
}
