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
                    ActorBodySupport.InvalidateRoom(room);double support=t.Elapsed.TotalMilliseconds;
                    Debug.Log($"Стенд дверей, круг {round}: состав {compose:F1} мс, индекс {prepare:F1} мс, сортировка препятствий {sort:F1} мс ({solids.Length}), " +
                        $"опоры тел {support:F1} мс.");
                }
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(root!=null) UnityEngine.Object.DestroyImmediate(root.gameObject);}
        }
    }
}
