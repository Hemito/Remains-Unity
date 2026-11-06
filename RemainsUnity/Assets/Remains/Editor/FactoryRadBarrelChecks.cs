using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Seed104 factory: every template radbarrel becomes a 100 kg physical body of its picture, and worlds saved at an earlier
    // physical version receive only what they lack (v2-loot-v1: the barrels; v1: containers and barrels).
    //   batch -executeMethod Remains.Editor.FactoryRadBarrelChecks.RunBatch
    public static class FactoryRadBarrelChecks
    {
        private const string Level="factory:0";
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            PropCatalog.Load();
            var definition=PropCatalog.Get(FactoryRadBarrelAuthoring.Id);
            Require(definition.mass==100 && definition.radioactivity==3 && definition.movable && definition.hasPhysicalBody &&
                Mathf.Approximately(definition.width,.8f) && Mathf.Approximately(definition.height,1.14f),"Определение бочки разошлось с AllData/рисунком");
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
            var world=new WorldState {seed=104,mechanismVersion=1,worldId="radbarrel-check"};
            FactoryPreparation.Create(world,catalog,0);
            var root=FactoryLevelRoot.Create(world,Level);
            try
            {
                root.InstantiateRooms();
                var props=root.GetComponent<FactoryPropAdapter>();props.Install(root);
                root.GetComponent<FactoryContainerAdapter>().Install(root,world);
                var layout=RoomPhysicalContent.Read(root.Authoring,Level);
                var barrels=layout.props.Where(p=>p.definition==FactoryRadBarrelAuthoring.Id).Select(p=>p.id).ToArray();
                int placed=root.Objects.Count(o=>o.Source.Type=="remains:obj:"+FactoryRadBarrelAuthoring.Id);
                Require(placed>0 && barrels.Length==placed && props.LaterAdditions.Count==placed,$"Поставлены не все бочки: в раскладке {placed}, в расстановке {barrels.Length}");
                var ordinary=layout.props.Where(p=>p.definition!=FactoryRadBarrelAuthoring.Id).Select(p=>p.id).ToArray();
                var containers=layout.containers.Select(c=>c.id).ToArray();
                Require(layout.patch==Level+":"+FactoryContainerAdapter.LaterPatch && layout.expansion.previousPatch==Level+":"+FactoryContainerAdapter.Patch &&
                    layout.expansion.additions.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(barrels.OrderBy(id=>id,StringComparer.Ordinal)) &&
                    layout.expansion.earlierVersions.Length==1 && layout.expansion.earlierVersions[0].previousPatch==Level+":"+FactoryPropAdapter.FirstPatch &&
                    layout.expansion.earlierVersions[0].additions.ToHashSet().SetEquals(containers.Concat(barrels)),"Неверная цепочка добавочного обновления physical-v3");

                PhysicalLayout Earlier(string patch,bool withContainers)
                {
                    var copy=StateJson.Copy(layout);copy.patch=Level+":"+patch;copy.expansion=null;
                    copy.props=copy.props.Where(p=>p.definition!=FactoryRadBarrelAuthoring.Id).ToArray();
                    if(!withContainers) {copy.containers=Array.Empty<ContainerState>();copy.loot=Array.Empty<ContainerLootBinding>();}
                    return copy;
                }
                var fresh=StateJson.Copy(world);
                Require(PhysicalContentInstaller.Apply(fresh,layout) && fresh.props.Count==ordinary.Length+barrels.Length && fresh.containers.Count==containers.Length,
                    "Новый мир не получил всю расстановку");
                // A world saved at v2-loot-v1 with one ordinary body gone: only the barrels arrive, the removed body stays removed.
                var saved=StateJson.Copy(world);
                PhysicalContentInstaller.Apply(saved,Earlier(FactoryContainerAdapter.Patch,true));
                string removed=saved.props[0].id;saved.props.RemoveAt(0);
                Require(PhysicalContentInstaller.Apply(saved,layout) && saved.props.Count==ordinary.Length-1+barrels.Length && saved.props.All(p=>p.id!=removed) &&
                    barrels.All(id=>saved.props.Exists(p=>p.id==id)) && saved.containers.Count==containers.Length &&
                    saved.contentPatches.Contains(layout.patch),"Мир версии v2-loot-v1 получил не только бочки");
                Require(!PhysicalContentInstaller.Apply(saved,layout),"Повторная установка physical-v3 изменила мир");
                var first=StateJson.Copy(world);
                PhysicalContentInstaller.Apply(first,Earlier(FactoryPropAdapter.FirstPatch,false));
                Require(PhysicalContentInstaller.Apply(first,layout) && first.props.Count==ordinary.Length+barrels.Length && first.containers.Count==containers.Length,
                    "Мир версии physical-v1 не получил контейнеры и бочки");
                Debug.Log($"Радиоактивные бочки завода: PASS; seed104 — {barrels.Length} бочек по 100 кг 0,8×1,14 м (радиация не моделируется, TD-37); "+
                    "обновление v2-loot-v1 → v3 добавляет только бочки, physical-v1 → v3 — контейнеры и бочки, удалённое тело не возвращается.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root.gameObject);}
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("FactoryRadBarrelChecks: "+reason);}
    }
}
