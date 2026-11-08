using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
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
            ("base:factory-generated:creatures-v3",new[]{"bloodwing","bloat","slime"}),
            ("base:factory-generated:creatures-v4",new[]{"turret","landturret","armturret"}),
            // Random enemies of the rooms (FactoryRandomEncounters) of the creatures already connected.
            ("base:factory-generated:creatures-v5",RandomKeys("tarakan","rat","molerat","scorp","bloodwing","bloat","slime","turret","landturret")),
            // Raiders by the danger of their room (randomCid) — keyed by the variant, so a variant connected later is an
            // addition of its own: the shooters raider5 and raider7, placed and random.
            ("base:factory-generated:creatures-v6",new[]{"raider5","raider7","random:raider5","random:raider7"}),
            // The melee raiders: weapon in the teeth (raider1, raider3) and levitated (raider2, raider4).
            ("base:factory-generated:creatures-v7",new[]{"raider1","raider2","raider3","raider4","random:raider1","random:raider2","random:raider3","random:raider4"}),
            // The first robot: the flying spritebot with its laser, placed and random.
            ("base:factory-generated:creatures-v8",new[]{"spritebot","random:spritebot"}),
            // The spider mine: asleep on its floor, ceiling or wall until it runs at a target and blows up.
            ("base:factory-generated:creatures-v9",new[]{"msp","random:msp"}),
            // The roller: rests, rolls at a target and touches it.
            ("base:factory-generated:creatures-v10",new[]{"roller","random:roller"}),
            // The robobrain (object robot): a walking robot with a laser or a spark gun.
            ("base:factory-generated:creatures-v11",new[]{"robot","random:robot"}),
            // Zombies of the factory (biome 0: zombie0–4) by the danger of their room, some buried; the hidden random
            // enemies of the rooms (createHidden: dig 2) are buried zombies.
            ("base:factory-generated:creatures-v12",new[]{"zombie0","zombie1","zombie2","zombie3","zombie4",
                "random:zombie0","random:zombie1","random:zombie2","random:zombie3","random:zombie4"})
        };
        private const string RandomPrefix="random:";
        private static string[] RandomKeys(params string[] objects)=>objects.Select(o=>RandomPrefix+o).ToArray();
        public static string Patch=>versions[^1].patch;
        public static string PreviousPatch=>versions[^2].patch;
        // A placement this adapter writes: a level keeps the last version that added something on it (Install).
        public static bool Generated(NpcLayout layout)=>layout!=null && versions.Any(v=>layout.patch==layout.level+":"+v.patch);
        private const string ObjectPrefix="remains:obj:";

        public static int Install(FactoryLevelRoot root,IReadOnlyList<FactoryRandomEncounters.Encounter> plan)
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
                // The original picks a random variant and turns a new unit to a random side; stable choices per placement here.
                int hash=localId.Aggregate(0,(value,c)=>unchecked(value*31+c));
                string key=id;
                CreatureEntry creature;
                if(string.IsNullOrEmpty(rule.variantRule)) creature=catalog.Require(catalog.ChooseStableVariant(rule,placement.Room.Cell.Y/2.0,hash));
                else
                {
                    string chosen=CreatureCatalog.ChooseVariant(rule,placement.Room.Cell.Y/2.0,FactoryRandomEncounters.VariantRandom(root,localId),out string reason,root.Prepared.Profile);
                    if(chosen==null) {string gap=$"{id}: {reason}";gaps[gap]=gaps.TryGetValue(gap,out int seen)?seen+1:1;continue;}
                    creature=catalog.Require(chosen);key=creature.sourceId;
                }
                if(root.Prepared.Profile.DropsPlaced(creature.family,FactoryRandomEncounters.VariantRandom(root,localId+":profile")))
                {root.HideObjectArtwork(placement);continue;}
                var node=new GameObject("Существо: "+placement.Id);
                node.transform.SetParent(root.transform,false);node.transform.localPosition=placement.Position;
                int facing=(hash&1)==0?1:-1;
                var marker=node.AddComponent<RoomActorPlacement>();marker.Configure(localId,creature.actorType,facing,false,null);marker.SetSpawn(creature.spawn);
                // UnitZombie: a digger buries itself where the level creates it (the template places it on its floor).
                if(creature.family==NpcCreatureBehaviours.Zombie)
                    marker.SetBuried(RandomEncounterRules.ZombieDigs(placement.Room.Cell.Y/2.0,FactoryRandomEncounters.VariantRandom(root,localId+":dig")));
                root.HideObjectArtwork(placement);
                if(!added.TryGetValue(key,out var ids)) added.Add(key,ids=new List<string>());
                ids.Add(localId);
                count++;
            }
            var randomGaps=new Dictionary<string,int>(StringComparer.Ordinal);int random=0;
            foreach(var encounter in plan)
            {
                // Mines and traps of the trap points are placed by FactoryMineAdapter and FactoryHazardAdapter.
                if(encounter.Kind==RandomEncounterRules.Trap && (encounter.Unit=="mine" || HazardCatalog.ForObject(encounter.Unit)!=null)) continue;
                string key=RandomPrefix+encounter.Unit;
                CreatureEntry chosenVariant=null;string variantGap=null;
                // A hidden zombie (createHidden) is placed as a buried one; other hidden enemies stay gaps.
                bool buriedZombie=encounter.Hidden && encounter.Unit=="zombie";
                if((!encounter.Hidden || buriedZombie) && catalog.TryObject(encounter.Unit,out var variantRule) && !string.IsNullOrEmpty(variantRule.variantRule))
                {
                    string chosen=CreatureCatalog.ChooseVariant(variantRule,encounter.Danger,FactoryRandomEncounters.VariantRandom(root,encounter.LocalId),out variantGap,root.Prepared.Profile);
                    if(chosen!=null) {chosenVariant=catalog.Require(chosen);key=RandomPrefix+chosenVariant.sourceId;}
                }
                if(variantGap!=null) {string gap=$"{encounter.Unit} ({variantGap})";randomGaps[gap]=randomGaps.TryGetValue(gap,out int seen)?seen+1:1;continue;}
                if(encounter.Hidden && !buriedZombie || !catalog.TryObject(encounter.Unit,out var rule) || !versions.Any(v=>v.objects.Contains(key)))
                {
                    string reason=encounter.Hidden?"скрытый враг, поведение закопанного/клетки ещё не реализовано":
                        encounter.Unit=="slmine"?"слизь-мина: скрытый вариант слизи (UnitSlime isMine) ещё не реализован":
                        encounter.Kind==RandomEncounterRules.Trap?"ловушка-триггер с самострелом или гранатой (UnitTrigger → UnitDamager) ещё не реализована":
                        catalog.GapReason(encounter.Unit)??"нет в каталоге существ";
                    string gap=$"{encounter.Unit} ({reason})";randomGaps[gap]=randomGaps.TryGetValue(gap,out int seen)?seen+1:1;
                    continue;
                }
                string localId=encounter.LocalId;
                var node=new GameObject("Случайное существо: "+encounter.Point.Id);
                node.transform.SetParent(root.transform,false);node.transform.localPosition=encounter.Point.Position;
                int hash=localId.Aggregate(0,(value,c)=>unchecked(value*31+c));
                var creature=chosenVariant??catalog.Require(catalog.ChooseStableVariant(rule,encounter.Danger,hash));
                var marker=node.AddComponent<RoomActorPlacement>();marker.Configure(localId,creature.actorType,(hash&1)==0?1:-1,false,null);marker.SetSpawn(creature.spawn);
                if(creature.family==NpcCreatureBehaviours.Zombie)
                    marker.SetBuried(buriedZombie || RandomEncounterRules.ZombieDigs(encounter.Danger,FactoryRandomEncounters.VariantRandom(root,localId+":dig")));
                if(!added.TryGetValue(key,out var ids)) added.Add(key,ids=new List<string>());
                ids.Add(localId);
                count++;random++;
            }
            var unknown=added.Keys.Where(o=>!versions.Any(v=>v.objects.Contains(o))).ToArray();
            if(unknown.Length>0) throw new InvalidOperationException("Существа завода: объекты без версии расстановки: "+string.Join(", ",unknown));
            var (patch,expansion)=Versions(added);
            var content=root.gameObject.AddComponent<RoomNpcContent>();content.Configure(patch);
            if(expansion!=null) content.ConfigureExpansion(expansion);
            Debug.Log($"Завод {root.Prepared.Physics.id}: случайных существ поставлено {random}.");
            if(randomGaps.Count>0)
                Debug.LogWarning($"Завод {root.Prepared.Physics.id}: случайные враги комнат по плану оригинала ещё не ставятся — "+
                    string.Join("; ",randomGaps.OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>$"{g.Key} ×{g.Value}"))+".");
            if(gaps.Count>0)
                Debug.LogWarning($"Завод {root.Prepared.Physics.id}: существа ещё не подключены и не поставлены — "+
                    string.Join("; ",gaps.OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>catalog.GapReason(g.Key) is {} reason?$"{g.Key} ×{g.Value} ({reason})":$"{g.Key} ×{g.Value}"))+".");
            return count;
        }

        // The level's versions from its placed objects (object key → local IDs): the first and every later one that adds
        // something here. A version adding nothing on this level (a seed without spritebots) keeps it at the version
        // before, as FactoryHazardAdapter keeps v1, so no step of the update is empty and no world can have recorded a
        // skipped version on this level. Returns the level's version and the update to it (null for the first).
        public static (string patch,ContentExpansion expansion) Versions(IReadOnlyDictionary<string,List<string>> added)
        {
            string[] Since(int version)=>versions.Skip(version+1).SelectMany(v=>v.objects).Where(added.ContainsKey).SelectMany(o=>added[o]).ToArray();
            var own=Enumerable.Range(0,versions.Length).Where(v=>v==0 || versions[v].objects.Any(added.ContainsKey)).ToArray();
            if(own.Length==1) return (versions[0].patch,null);
            return (versions[own[^1]].patch,new ContentExpansion {previousPatch=versions[own[^2]].patch,additions=Since(own[^2]),
                earlierVersions=own.Take(own.Length-2).Reverse().Select(v=>new ContentExpansionStep {previousPatch=versions[v].patch,additions=Since(v)}).ToArray()});
        }
    }
}
