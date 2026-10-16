using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // Session-owned, inactive authored sources. Prepare is a loading operation;
    // Require only reads already prepared sources and must never generate in a frame loop.
    public static class FactoryContentSources
    {
        private sealed class Entry
        {
            public FactoryLevelRoot root;
            public int seed;
            public string definition,fingerprint,layout,lootLedger;
            public bool Matches(WorldState world,WorldFactory factory)=>root!=null && seed==world.seed &&
                definition==factory.definition && fingerprint==factory.contentFingerprint && layout==factory.layout &&
                (world.contentPatches.Contains(root.Prepared.Level+":"+FactoryContainerAdapter.Patch) ||
                 lootLedger==Remains.Serialization.StateJson.ToJson(world.factoryLootLimits));
        }
        private static readonly Dictionary<(string world,string level),Entry> sources=new();
        public static bool IsGenerated(WorldState world,string level)=>
            world!=null && WorldRooms.Definition(world,level)==FactoryCatalogAsset.DefinitionId;
        public static void Prepare(WorldState world,IEnumerable<string> levels)
        {
            foreach(string level in levels) Prepare(world,level);
        }
        public static void Prepare(WorldState world,string level)
        {
            if(!IsGenerated(world,level)) return;
            if(string.IsNullOrWhiteSpace(world.worldId)) throw new InvalidOperationException("Подготовка завода: не задан ID мира.");
            var factory=WorldFactories.Find(world,level)??throw new InvalidOperationException($"Мир {world.worldId}: отсутствует раскладка {level}.");
            var key=(world.worldId,level);
            if(sources.TryGetValue(key,out var current) && current.Matches(world,factory))
            {
                if(current.root.Catalog.ContentFingerprint!=factory.contentFingerprint)
                    throw new InvalidOperationException($"Мир {world.worldId}: изменился контент подготовленного завода {level}.");
                return;
            }
            // Create validates the stored selection and content before replacing a source.
            var root=FactoryLevelRoot.Create(world,level);
            try
            {
                root.name=$"Подготовленные данные {world.worldId}: {level}";
                root.gameObject.SetActive(false);
                root.InstantiateRooms();
                if(!root.TryGetComponent<FactoryDoorAdapter>(out var doors))
                    throw new InvalidOperationException("Нет сериализованного адаптера дверей корня: "+level);
                doors.Install(root);
                if(!root.TryGetComponent<FactoryPropAdapter>(out var props))
                    throw new InvalidOperationException("Нет сериализованного адаптера физических объектов корня: "+level);
                props.Install(root);
                if(!root.TryGetComponent<FactoryContainerAdapter>(out var containers))throw new InvalidOperationException("Нет сериализованного адаптера контейнеров завода: "+level);
                containers.Install(root,world);
                FactoryMineAdapter.Install(root);
                FactoryCreatureAdapter.Install(root);
                sources[key]=new Entry {root=root,seed=world.seed,definition=factory.definition,fingerprint=factory.contentFingerprint,layout=factory.layout,
                    lootLedger=Remains.Serialization.StateJson.ToJson(world.factoryLootLimits)};
            }
            catch {Release(root);throw;}
            if(current!=null) Release(current.root);
        }
        public static RoomAuthoring Require(WorldState world,string level)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            var factory=WorldFactories.Find(world,level)??throw new InvalidOperationException($"Мир {world.worldId}: отсутствует раскладка {level}.");
            if(!sources.TryGetValue((world.worldId,level),out var entry) || !entry.Matches(world,factory))
                throw new InvalidOperationException($"Завод ещё не подготовлен или изменился: world={world.worldId}, level={level}. Выполните явную подготовку при загрузке.");
            return entry.root.Authoring;
        }
        public static void Clear()
        {
            foreach(var entry in sources.Values) Release(entry.root);
            sources.Clear();
        }
        private static void Release(FactoryLevelRoot root)
        {if(root!=null) {root.gameObject.SetActive(false);if(Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);else UnityEngine.Object.DestroyImmediate(root.gameObject);}}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()=>Clear();
    }
}
