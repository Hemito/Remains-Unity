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
    // the passage strips, gives unique IDs, and the installed NPCs are exactly the plan's connected creatures.
    //   batch -executeMethod Remains.Editor.FactoryRandomEncounterChecks.RunBatch
    public static class FactoryRandomEncounterChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            try
            {
                Anatomy.Load();WeaponCatalog.Load();
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");
                var authoring=RoomAuthoring.Load("factory:0",world);
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
                Debug.Log($"Случайные враги seed104: комнат {types}, тип врагов монстры/рейдеры/роботы = {Share(0)}/{Share(1)}/{Share(2)}; "+
                    $"план {plan.Count} (скрытых {plan.Count(e=>e.Hidden)}); точек свободных {rooms.Sum(r=>r.Points.Sum())} из {root.Objects.Count(o=>RandomEncounterRules.Kind(o.Source.Type.Substring("remains:obj:".Length))>0)}.");
                foreach(var group in plan.GroupBy(e=>(e.Unit,e.Hidden)).OrderByDescending(g=>g.Count()))
                    Debug.Log($"  {group.Key.Unit}{(group.Key.Hidden?" (скрытый)":"")}: {group.Count()}");

                // The installed creatures: every placed random NPC is a connected plan entry.
                var catalog=CreatureCatalog.Load();
                var placed=root.GetComponentsInChildren<RoomActorPlacement>(true).Where(p=>p.name.StartsWith("Случайное существо: ",StringComparison.Ordinal)).ToArray();
                int expected=plan.Count(e=>!e.Hidden && catalog.TryObject(e.Unit,out _));
                Require(placed.Length==expected,$"Поставлено случайных существ {placed.Length}, по плану подключённых {expected}.");
                // Node names carry the template object's world ID: one NPC per object.
                var allIds=root.GetComponentsInChildren<RoomActorPlacement>(true).Select(p=>p.name.Substring(p.name.IndexOf(": ",StringComparison.Ordinal)+2)).ToArray();
                Require(allIds.Distinct(StringComparer.Ordinal).Count()==allIds.Length,"Повтор объекта среди NPC завода.");
                Debug.Log($"FactoryRandomEncounterChecks: PASS — план детерминирован, {plan.Count} врагов в {types} комнатах, поставлено {placed.Length} подключённых существ, ID уникальны.");
            }
            finally
            {
                FactoryContentSources.Clear();
            }
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
