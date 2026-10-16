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
        // Placement versions: each adds objects to the ones before; a world gets only what its version lacks.
        private static readonly (string patch,string[] objects)[] versions=
        {
            ("base:factory-generated:creatures-v1",new[]{"tarakan","rat","molerat"}),
            ("base:factory-generated:creatures-v2",new[]{"scorp"}),
            ("base:factory-generated:creatures-v3",new[]{"bloodwing","bloat","slime"})
        };
        public static string Patch=>versions[^1].patch;
        public static string PreviousPatch=>versions[^2].patch;
        private const string ObjectPrefix="remains:obj:";

        public static int Install(FactoryLevelRoot root)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Существа завода: нужен подготовленный корень.");
            if(root.GetComponent<RoomNpcContent>()!=null) throw new InvalidOperationException("Существа завода: корень уже содержит расстановку NPC.");
            var catalog=CreatureCatalog.Load();
            var gaps=new Dictionary<string,int>(StringComparer.Ordinal);
            int count=0;var added=new Dictionary<string,List<string>>(StringComparer.Ordinal);   // object → local IDs
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                string type=placement.Source.Type;
                if(!type.StartsWith(ObjectPrefix,StringComparison.Ordinal)) continue;
                string id=type.Substring(ObjectPrefix.Length);
                if(!catalog.TryObject(id,out var rule))
                {
                    if(catalog.GapReason(id)!=null) gaps[id]=gaps.TryGetValue(id,out int seen)?seen+1:1;
                    continue;
                }
                if(placement.Source.Params.Count>0 || placement.Source.Items.Count>0 || placement.Source.Links.Count>0 || placement.Source.Extra!=null)
                {Debug.LogWarning($"Существо завода с параметрами или связями пока не поддержано и пропущено: {placement.Id} ({type}).");continue;}
                string localId=$"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}";
                var node=new GameObject("Существо: "+placement.Id);
                node.transform.SetParent(root.transform,false);node.transform.localPosition=placement.Position;
                // The original picks a random variant and turns a new unit to a random side; stable choices per placement here.
                int hash=localId.Aggregate(0,(value,c)=>unchecked(value*31+c));
                var creature=catalog.Require(rule.actorTypes[(int)((uint)hash>>1)%rule.actorTypes.Length]);
                int facing=(hash&1)==0?1:-1;
                var marker=node.AddComponent<RoomActorPlacement>();marker.Configure(localId,creature.actorType,facing,false,null);marker.SetSpawn(creature.spawn);
                root.HideObjectArtwork(placement);
                if(!added.TryGetValue(id,out var ids)) added.Add(id,ids=new List<string>());
                ids.Add(localId);
                count++;
            }
            var unknown=added.Keys.Where(o=>!versions.Any(v=>v.objects.Contains(o))).ToArray();
            if(unknown.Length>0) throw new InvalidOperationException("Существа завода: объекты без версии расстановки: "+string.Join(", ",unknown));
            string[] Since(int version)=>versions.Skip(version+1).SelectMany(v=>v.objects).Where(added.ContainsKey).SelectMany(o=>added[o]).ToArray();
            var content=root.gameObject.AddComponent<RoomNpcContent>();content.Configure(Patch);
            content.ConfigureExpansion(new ContentExpansion {previousPatch=versions[^2].patch,additions=Since(versions.Length-2),
                earlierVersions=Enumerable.Range(0,versions.Length-2).Reverse().Select(v=>new ContentExpansionStep {previousPatch=versions[v].patch,additions=Since(v)}).ToArray()});
            if(gaps.Count>0)
                Debug.LogWarning($"Завод {root.Prepared.Physics.id}: существа ещё не подключены и не поставлены — "+
                    string.Join("; ",gaps.OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>$"{g.Key} ×{g.Value} ({catalog.GapReason(g.Key)})"))+".");
            return count;
        }
    }
}
