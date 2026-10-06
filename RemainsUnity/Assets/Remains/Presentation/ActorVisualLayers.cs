using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // Root transforms stay at identity: facing flips the body, never its weapon pose.
    public sealed class ActorVisualLayers
    {
        public const int FirstActorOrder=5000,WorldObjectOrder=21000,EffectOrder=30000;
        // Saved prefab local defaults retain their identity; runtime world groups use Orders.
        public const int WallFixtureOrder=999,RestingObjectOrder=998;
        private readonly Dictionary<string,ActorVisualGroup> groups=new(StringComparer.Ordinal);
        private readonly HashSet<string> active=new(StringComparer.Ordinal);
        private readonly List<string> removed=new();
        private readonly Transform parent;
        public WorldVisualOrderTable Orders {get;}
        public ActorVisualLayers(Transform parent=null,WorldVisualOrderTable orders=null)
        {this.parent=parent;Orders=orders??new WorldVisualOrderTable();}
        public bool TryGetGroup(string id,out ActorVisualGroup group)=>groups.TryGetValue(id,out group);
        public ActorVisualGroup Attach(string id,Transform child)
        {
            if(!groups.TryGetValue(id,out var group))
            {
                group=new GameObject("Персонаж целиком: "+id).AddComponent<ActorVisualGroup>();
                group.transform.SetParent(parent,false);group.Initialize(id,Orders);groups.Add(id,group);
            }
            child.SetParent(group.transform,true);
            return group;
        }
        public void Synchronize(WorldState world,string visibleLevel)
        {
            active.Clear();removed.Clear();
            if(world!=null && visibleLevel!=null) foreach(var actor in world.actors)
                if(actor.level==visibleLevel && ActorPresence.IsPresent(actor)) active.Add(actor.id);
            foreach(string id in groups.Keys) if(!active.Contains(id)) removed.Add(id);
            foreach(string id in removed)
            {
                groups[id].gameObject.SetActive(false);UnityEngine.Object.Destroy(groups[id].gameObject);groups.Remove(id);
            }
            foreach(string id in groups.Keys) Orders.GetOrder(WorldVisualKind.Actor,id);
        }
    }
}
