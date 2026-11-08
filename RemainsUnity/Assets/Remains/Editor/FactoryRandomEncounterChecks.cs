using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Random room enemies on the seed104 factory: the plan is deterministic, never exceeds the free points, keeps off
    // the passage strips, gives unique IDs, and the installed NPCs are exactly the plan's connected creatures; the
    // trap points' mines and traps are exactly the plan's, and a world of their v1 layouts gets only those.
    //   batch -executeMethod Remains.Editor.FactoryRandomEncounterChecks.RunBatch
    public static class FactoryRandomEncounterChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()=>RunFactory(0);
        public static void RunProfilesBatch()
        {
            try
            {
                CheckDangerBoundaries();
                RunFactory(0);
                var profiles=FactoryProfileCatalog.Load();
                foreach(var profile in profiles.profiles)
                {
                    int number=Enumerable.Range(1,200).Select(i=>-i).First(i=>profiles.For(104,i)==profile);
                    RunFactory(number);
                }
                Debug.Log("FactoryRandomEncounterChecks profiles: PASS — danger boundaries and every shipped factory profile.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void CheckDangerBoundaries()
        {
            foreach(var row in new (double danger,int raiders,int zombies)[]{(0d,2,1),(1.999,2,1),(2d,5,4),(4.999,5,4),(5d,9,5),(20d,9,5)})
            {
                var raiders=new HashSet<int>();var zombies=new HashSet<int>();
                for(int i=0;i<1000;i++)
                {
                    double roll=(i+.5)/1000;
                    raiders.Add(RandomEncounterRules.RaiderVariant(row.danger,()=>roll));
                    zombies.Add(RandomEncounterRules.ZombieVariant(row.danger,()=>roll));
                }
                Require(raiders.SetEquals(Enumerable.Range(1,row.raiders)),"Диапазон рейдеров при опасности "+row.danger);
                Require(zombies.SetEquals(Enumerable.Range(0,row.zombies)),"Диапазон зомби при опасности "+row.danger);
            }
            // The list order/chance must not make a high-danger weapon available before its threshold.
            var rule=new CreatureObjectRule {objectId="raider",variantRule=CreatureObjectRule.RaiderVariants,
                variants=new[]{new CreatureVariant {cid=1,unit="raider1",weapons=new[]{
                    new CreatureWeaponChoice {weapon="late",difficulty=5,actorType="late-weapon"},
                    new CreatureWeaponChoice {weapon="early",actorType="early-weapon"}}}}};
            Require(CreatureCatalog.ChooseVariant(rule,4.999,()=>0,out _)=="early-weapon","Оружие опасности5 выдано раньше порога");
            Require(CreatureCatalog.ChooseVariant(rule,5,()=>0,out _)=="late-weapon","Оружие опасности5 не выдано на пороге");
            var profile=new FactoryProfile {raiderVariants=new[]{1},raiderEquipment=2};
            Require(CreatureCatalog.ChooseVariant(rule,3,()=>0,out _,profile)=="late-weapon","Не учтён запас оружия профиля");
        }
        private static void RunFactory(int factoryIndex)
        {
            string level=WorldFactories.Level(factoryIndex);
            try
            {
                Anatomy.Load();WeaponCatalog.Load();
                var world=new WorldState{seed=104,mechanismVersion=1};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),factoryIndex);
                FactoryContentSources.Prepare(world,level);
                var authoring=RoomAuthoring.Load(level,world);
                var root=authoring.GetComponent<FactoryLevelRoot>()??throw new InvalidOperationException("Нет корня завода.");
                var rooms=new List<FactoryRandomEncounters.RoomPlan>();
                var plan=FactoryRandomEncounters.Plan(root,rooms);
                var again=FactoryRandomEncounters.Plan(root);
                Require(plan.Count==again.Count && plan.Zip(again,(a,b)=>a.LocalId==b.LocalId && a.Unit==b.Unit && a.Hidden==b.Hidden).All(x=>x),"План случайных врагов не повторяется.");
                Require(plan.Select(e=>e.LocalId).Distinct(StringComparer.Ordinal).Count()==plan.Count,"Повтор точки в плане случайных врагов.");
                var explicitIds=new HashSet<string>(root.Objects.Select(o=>$"{o.Room.Cell.X},{o.Room.Cell.Y}:{o.Source.Id}"),StringComparer.Ordinal);
                Require(plan.All(e=>explicitIds.Contains(e.LocalId)),"Случайный враг не на точке шаблона.");
                foreach(var room in rooms)
                {
                    int planned=plan.Count(e=>e.Point.Room==room.Room && !e.Hidden);
                    int possible=Enumerable.Range(1,RandomEncounterRules.Kinds-1).Sum(k=>Math.Min(room.Counts[k],room.Points[k]));
                    Require(planned<=possible,$"Комната {room.Room.Cell.X},{room.Room.Cell.Y}: врагов {planned} больше возможного {possible}.");
                }
                int types=rooms.Count;
                string Share(int t)=>$"{rooms.Count(r=>r.EnemyType==t)}";
                Debug.Log($"Случайные враги seed104 {level} ({root.Prepared.Profile.id}): комнат {types}, тип врагов монстры/рейдеры/роботы = {Share(0)}/{Share(1)}/{Share(2)}; "+
                    $"план {plan.Count} (скрытых {plan.Count(e=>e.Hidden)}); точек свободных {rooms.Sum(r=>r.Points.Sum())} из {root.Objects.Count(o=>RandomEncounterRules.Kind(o.Source.Type.Substring("remains:obj:".Length))>0)}.");
                foreach(var group in plan.GroupBy(e=>(e.Unit,e.Hidden)).OrderByDescending(g=>g.Count()))
                    Debug.Log($"  {group.Key.Unit}{(group.Key.Hidden?" (скрытый)":"")}: {group.Count()}");

                // The installed creatures: every placed random NPC is a connected plan entry.
                var catalog=CreatureCatalog.Load();
                var placed=root.GetComponentsInChildren<RoomActorPlacement>(true).Where(p=>p.name.StartsWith("Случайное существо: ",StringComparison.Ordinal)).ToArray();
                // Hidden zombies are connected NPCs too. Numbered factory profiles also affect cid and weapon danger.
                var expected=new Dictionary<string,string>(StringComparer.Ordinal);
                var hiddenZombies=new HashSet<string>(StringComparer.Ordinal);
                foreach(var encounter in plan)
                {
                    if(encounter.Hidden && encounter.Unit!="zombie" || !catalog.TryObject(encounter.Unit,out var rule))continue;
                    string actorType;
                    if(string.IsNullOrEmpty(rule.variantRule))
                    {
                        int hash=encounter.LocalId.Aggregate(0,(value,c)=>unchecked(value*31+c));
                        actorType=rule.actorTypes[(int)((uint)hash>>1)%rule.actorTypes.Length];
                    }
                    else actorType=CreatureCatalog.ChooseVariant(rule,encounter.Danger,
                        FactoryRandomEncounters.VariantRandom(root,encounter.LocalId),out _,root.Prepared.Profile);
                    if(actorType==null)continue;
                    expected.Add(encounter.LocalId,actorType);
                    if(encounter.Hidden)hiddenZombies.Add(encounter.LocalId);
                }
                Require(placed.Length==expected.Count,$"Поставлено случайных существ {placed.Length}, по плану подключённых {expected.Count}.");
                foreach(var marker in placed)
                {
                    var placement=marker.Read(authoring);
                    Require(expected.TryGetValue(placement.id,out string actorType) && placement.type==actorType,
                        $"Существо {placement.id}: поставлено {placement.type}, ожидалось {actorType}.");
                    if(hiddenZombies.Contains(placement.id))Require(ActorPresence.Hidden(marker.ReadNpc(authoring,level)),
                        "Скрытый зомби появился незакопанным: "+placement.id);
                }
                // Node names carry the template object's world ID: one NPC per object.
                var allIds=root.GetComponentsInChildren<RoomActorPlacement>(true).Select(p=>p.name.Substring(p.name.IndexOf(": ",StringComparison.Ordinal)+2)).ToArray();
                Require(allIds.Distinct(StringComparer.Ordinal).Count()==allIds.Length,"Повтор объекта среди NPC завода.");

                // Trap points: mines (mine/hmine by randomCid) and traps, placed by their own adapters.
                var planMines=plan.Where(e=>e.Kind==RandomEncounterRules.Trap && e.Unit=="mine").ToArray();
                var planTraps=plan.Where(e=>e.Kind==RandomEncounterRules.Trap && HazardCatalog.ForObject(e.Unit)!=null).ToArray();
                Require(planMines.All(e=>e.Variant=="mine" || e.Variant=="hmine") && plan.Where(e=>e.Unit!="mine").All(e=>e.Variant==null),"Вариант случайной мины не выбран или выбран не мине.");
                Require(plan.Zip(again,(a,b)=>a.Variant==b.Variant).All(x=>x),"Вариант случайной мины не повторяется.");
                var randomMines=root.GetComponentsInChildren<RoomMinePlacement>(true).Where(p=>p.name.StartsWith("Случайная мина: ",StringComparison.Ordinal)).ToArray();
                var randomTraps=root.GetComponentsInChildren<RoomHazardPlacement>(true).Where(p=>p.name.StartsWith("Случайная ловушка: ",StringComparison.Ordinal)).ToArray();
                Require(randomMines.Length==planMines.Length && randomTraps.Length==planTraps.Length,
                    $"Случайных мин {randomMines.Length} из {planMines.Length}, ловушек {randomTraps.Length} из {planTraps.Length}.");
                var batch=RoomContentBatch.Read(authoring,level);
                Require(batch.mines.patch==level+":"+(planMines.Length>0?FactoryMineAdapter.Patch:FactoryMineAdapter.PatchV1) &&
                    batch.hazards.patch==level+":"+(planTraps.Length>0?FactoryHazardAdapter.Patch:FactoryHazardAdapter.PatchV1),"Версии расстановки мин и ловушек.");
                var mineIds=new HashSet<string>(planMines.Select(e=>level+":"+e.LocalId),StringComparer.Ordinal);
                var trapIds=new HashSet<string>(planTraps.Select(e=>level+":"+e.LocalId),StringComparer.Ordinal);
                Require(batch.mines.mines.Count(m=>mineIds.Contains(m.id))==mineIds.Count && batch.mines.mines.Where(m=>mineIds.Contains(m.id)).All(m=>planMines.Any(e=>level+":"+e.LocalId==m.id && e.Variant==m.definition)) &&
                    batch.hazards.hazards.Count(h=>trapIds.Contains(h.id))==trapIds.Count,"Случайные мины и ловушки в расстановке уровня.");
                Require(RoomContentInstaller.Apply(world,batch),"Первая установка завода.");
                Require(mineIds.All(id=>world.mines.Exists(m=>m.id==id)) && trapIds.All(id=>world.hazards.Exists(h=>h.id==id)),"Случайные мины и ловушки установлены.");
                // A world of v1: only the random ones are added; a template mine/trap it lost (exploded, sprung) stays lost.
                if(mineIds.Count>0)
                {
                    var older=Remains.Serialization.StateJson.Copy(world);
                    older.mines.RemoveAll(m=>mineIds.Contains(m.id));
                    var lost=older.mines.First(m=>!mineIds.Contains(m.id)).id;older.mines.RemoveAll(m=>m.id==lost);
                    older.contentPatches.Remove(level+":"+FactoryMineAdapter.Patch);older.contentPatches.Add(level+":"+FactoryMineAdapter.PatchV1);
                    Require(RoomContentInstaller.Apply(older,batch) && mineIds.All(id=>older.mines.Exists(m=>m.id==id)) && !older.mines.Exists(m=>m.id==lost),"Переход мин v1 → v2 добавляет только случайные.");
                }
                if(trapIds.Count>0)
                {
                    var older=Remains.Serialization.StateJson.Copy(world);
                    older.hazards.RemoveAll(h=>trapIds.Contains(h.id));
                    var lost=older.hazards.FirstOrDefault(h=>!trapIds.Contains(h.id))?.id;if(lost!=null) older.hazards.RemoveAll(h=>h.id==lost);
                    older.contentPatches.Remove(level+":"+FactoryHazardAdapter.Patch);older.contentPatches.Add(level+":"+FactoryHazardAdapter.PatchV1);
                    Require(RoomContentInstaller.Apply(older,batch) && trapIds.All(id=>older.hazards.Exists(h=>h.id==id)) && (lost==null || !older.hazards.Exists(h=>h.id==lost)),"Переход ловушек v1 → v2 добавляет только случайные.");
                }
                Debug.Log($"FactoryRandomEncounterChecks: PASS {level} ({root.Prepared.Profile.id}) — план детерминирован, {plan.Count} врагов в {types} комнатах, поставлено {placed.Length} подключённых существ, "+
                    $"мин {randomMines.Length} (обычных {planMines.Count(e=>e.Variant=="mine")}), капканов {randomTraps.Length}; старые миры получают только случайные; ID уникальны.");
            }
            finally
            {
                FactoryContentSources.Clear();
            }
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
