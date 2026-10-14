using System;
using System.Diagnostics;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // Diagnostics: cost of the collision-geometry steps when one factory door changes state (seed 104).
    //   batch -executeMethod Remains.Editor.DoorToggleBench.RunBatch
    public static class DoorToggleBench
    {
        // FactoryLevelAuthoring.Check alone (its RunBatch also regenerates assets).
        public static void RunAdapterCheck()
        {
            try {FactoryLevelAuthoring.Check();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        // The candidate-grid edges equal the all-pairs Join result for a sample of surfaces.
        private static void CheckEdges(Type prepared,Remains.Domain.RoomLayout room)
        {
            const System.Reflection.BindingFlags any=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            var instance=Activator.CreateInstance(prepared,any,null,new object[]{room},null);
            var surfaces=(Array)prepared.GetField("surfaces",any).GetValue(instance);
            var edges=(Array)prepared.GetField("edges",any).GetValue(instance);
            int staticCount=(int)prepared.GetField("staticCount",any).GetValue(instance);
            var join=prepared.GetMethod("Join",any);
            var random=new System.Random(104);int checkedSurfaces=0,links=0;
            for(int sample=0;sample<300;sample++)
            {
                int i=random.Next(staticCount);
                var expected=new System.Collections.Generic.List<string>();
                for(int j=0;j<staticCount;j++)
                {
                    if(i==j) continue;
                    var args=new object[]{surfaces.GetValue(i),surfaces.GetValue(j),0d,0d};
                    if((bool)join.Invoke(null,args)) expected.Add($"{j}:{args[2]}:{args[3]}");
                }
                var actual=new System.Collections.Generic.List<string>();
                foreach(var edge in (Array)edges.GetValue(i))
                {
                    var type=edge.GetType();
                    actual.Add($"{type.GetField("Target",any).GetValue(edge)}:{type.GetField("Left",any).GetValue(edge)}:{type.GetField("Right",any).GetValue(edge)}");
                }
                if(string.Join(";",expected)!=string.Join(";",actual))
                    throw new InvalidOperationException($"Связи опоры {i} отличаются от полного перебора: ожидалось {string.Join(";",expected)}, получено {string.Join(";",actual)}.");
                checkedSurfaces++;links+=expected.Count;
            }
            Debug.Log($"Стенд дверей: связи опор совпали с полным перебором для {checkedSurfaces} поверхностей ({links} связей).");
        }

        public static void RunBatch()
        {
            FactoryLevelRoot root=null;
            try
            {
                Anatomy.Load();WeaponCatalog.Load();
                var catalog=Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey);
                var world=new WorldState {worldId="door-toggle-bench",seed=104};
                FactoryPreparation.Create(world,catalog,0);
                root=FactoryLevelRoot.Create(world,"factory:0");root.gameObject.SetActive(false);
                root.InstantiateRooms();
                var room=root.Prepared.Physics;
                var authored=room.platforms;
                var doors=world.doors.Where(d=>d.level=="factory:0").ToList();
                Debug.Log($"Стенд дверей: поверхностей {authored.Length}, дверей на уровне {doors.Count}.");
                for(int round=0;round<3;round++)
                {
                    var t=Stopwatch.StartNew();
                    var composed=DoorGeometry.Compose(authored,doors,"factory:0");double compose=t.Elapsed.TotalMilliseconds;t.Restart();
                    room.platforms=composed;RoomSurfaces.Prepare(room);double prepare=t.Elapsed.TotalMilliseconds;t.Restart();
                    var solids=room.platforms.Where(s=>!s.oneWay).Select(s=>(s.x,s.y,s.width,s.height))
                        .OrderBy(s=>s.x).ThenBy(s=>s.y).ThenBy(s=>s.width).ThenBy(s=>s.height).ToArray();double sort=t.Elapsed.TotalMilliseconds;t.Restart();
                    var prepared=typeof(ActorBodySupport).GetNestedType("PreparedRoom",System.Reflection.BindingFlags.NonPublic);
                    Activator.CreateInstance(prepared,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,null,new object[]{room},null);
                    double support=t.Elapsed.TotalMilliseconds;
                    if(round==0) CheckEdges(prepared,room);
                    Debug.Log($"Стенд дверей, круг {round}: состав {compose:F1} мс, индекс {prepare:F1} мс, сортировка препятствий {sort:F1} мс ({solids.Length}), " +
                        $"пересборка опор тел {support:F1} мс.");
                }
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(root!=null) UnityEngine.Object.DestroyImmediate(root.gameObject);}
        }
    }
}
