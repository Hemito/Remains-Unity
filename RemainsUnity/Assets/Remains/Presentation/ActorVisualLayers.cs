using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Presentation
{
    // Root transforms stay at identity: facing flips the body, never its weapon pose.
    public sealed class ActorVisualLayers
    {
        public const int FirstActorOrder=1000,WorldObjectOrder=21000,EffectOrder=30000;
        private readonly Dictionary<string,ActorVisualGroup> groups=new(StringComparer.Ordinal);
        private readonly Transform parent;
        public ActorVisualLayers(Transform parent=null)=>this.parent=parent;
        public ActorVisualGroup Attach(string id,Transform child)
        {
            if(!groups.TryGetValue(id,out var group))
            {
                group=new GameObject("Персонаж целиком: "+id).AddComponent<ActorVisualGroup>();
                group.transform.SetParent(parent,false);group.Initialize(id);groups.Add(id,group);
            }
            child.SetParent(group.transform,true);
            return group;
        }
        public void Synchronize(IEnumerable<string> activeIds)
        {
            var active=new HashSet<string>(activeIds,StringComparer.Ordinal);
            foreach(string id in groups.Keys.Where(id=>!active.Contains(id)).ToArray())
            {
                groups[id].gameObject.SetActive(false);UnityEngine.Object.Destroy(groups[id].gameObject);groups.Remove(id);
            }
            if(groups.Count>=WorldObjectOrder-FirstActorOrder)
                throw new InvalidOperationException($"Недостаточно порядка отрисовки для {groups.Count} составных актёров.");
            int order=FirstActorOrder;
            foreach(var pair in groups.OrderBy(p=>p.Key,StringComparer.Ordinal)) pair.Value.Order=order++;
        }
    }
}
