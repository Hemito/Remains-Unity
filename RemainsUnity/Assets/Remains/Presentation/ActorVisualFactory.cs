using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
namespace Remains.Presentation
{
    public static class ActorVisualFactory
    {
        private static readonly Dictionary<string,Func<GameObject,ActorVisual>> builders=new()
        {{"pony",obj=>obj.AddComponent<PonyVisual>()},{"static",obj=>obj.AddComponent<StaticActorVisual>()},{"atlas",obj=>obj.AddComponent<AtlasActorVisual>()}};
        private static readonly HashSet<string> registeredOverrides=new();
        public static void Register(string id,Func<GameObject,ActorVisual> builder,bool replace=false)
        {
            if(string.IsNullOrEmpty(id) || builder==null || !replace && builders.ContainsKey(id)) throw new InvalidOperationException("Некорректная регистрация представления актёра: "+id);
            builders[id]=builder;
            registeredOverrides.Add(id);
        }
        public static ActorVisual Create(ActorState actor)
        {
            var definition=ActorTypes.Get(actor.actorType);
            if(!registeredOverrides.Contains(definition.presentation) && EntityAssetCatalog.Load().TryGetActorPrefab(definition,out var prefab))
            {var instance=UnityEngine.Object.Instantiate(prefab);instance.name=actor.id;return instance;}
            string kind=definition.presentation;
            if(!builders.TryGetValue(kind,out var builder)) throw new InvalidOperationException($"Нет представления актёра: {actor.id}, presentation={kind}.");
            return builder(new GameObject(actor.id));
        }
    }
}
