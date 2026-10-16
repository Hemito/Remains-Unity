using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Content
{
    // Creatures of the generated factory from its room templates (original Location.createUnit): every template
    // object whose type is a creature of CreatureCatalog becomes an NPC placement of that actor type; the creatures
    // the catalog lists as gaps are reported with their reason and count, never replaced by another creature.
    public static class FactoryCreatureAdapter
    {
        public const string Patch="base:factory-generated:creatures-v1";
        private const string ObjectPrefix="remains:obj:";

        public static int Install(FactoryLevelRoot root)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Существа завода: нужен подготовленный корень.");
            if(root.GetComponent<RoomNpcContent>()!=null) throw new InvalidOperationException("Существа завода: корень уже содержит расстановку NPC.");
            var catalog=CreatureCatalog.Load();
            var gaps=new Dictionary<string,int>(StringComparer.Ordinal);
            int count=0;
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                string type=placement.Source.Type;
                if(!type.StartsWith(ObjectPrefix,StringComparison.Ordinal)) continue;
                string id=type.Substring(ObjectPrefix.Length);
                if(!catalog.TryGet(id,out var creature))
                {
                    if(catalog.GapReason(id)!=null) gaps[id]=gaps.TryGetValue(id,out int seen)?seen+1:1;
                    continue;
                }
                if(placement.Source.Params.Count>0 || placement.Source.Items.Count>0 || placement.Source.Links.Count>0 || placement.Source.Extra!=null)
                {Debug.LogWarning($"Существо завода с параметрами или связями пока не поддержано и пропущено: {placement.Id} ({type}).");continue;}
                string localId=$"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}";
                var node=new GameObject("Существо: "+placement.Id);
                node.transform.SetParent(root.transform,false);node.transform.localPosition=placement.Position;
                // The original turns a new unit to a random side (Unit.as); a stable choice per placement here.
                int facing=(localId.Aggregate(0,(hash,c)=>unchecked(hash*31+c))&1)==0?1:-1;
                var marker=node.AddComponent<RoomActorPlacement>();marker.Configure(localId,creature.actorType,facing,false,null);marker.SetSpawn(creature.spawn);
                root.HideObjectArtwork(placement);
                count++;
            }
            root.gameObject.AddComponent<RoomNpcContent>().Configure(Patch);
            if(gaps.Count>0)
                Debug.LogWarning($"Завод {root.Prepared.Physics.id}: существа ещё не подключены и не поставлены — "+
                    string.Join("; ",gaps.OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>$"{g.Key} ×{g.Value} ({catalog.GapReason(g.Key)})"))+".");
            return count;
        }
    }
}
