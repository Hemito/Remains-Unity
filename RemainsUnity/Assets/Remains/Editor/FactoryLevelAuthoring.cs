using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryLevelAuthoring
    {
        public const string PrefabPath="Assets/Remains/Resources/Content/FactoryLevelRoot.prefab";
        [MenuItem("Remains/Комнаты/Подготовить корень игрового завода")]
        public static void Generate()
        {
            var stage=UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if(stage!=null && stage.assetPath==PrefabPath)
                throw new InvalidOperationException("Закройте редактирование префаба корня завода перед его подготовкой.");
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath)
                ??throw new InvalidOperationException("Сначала подготовьте каталог игрового завода.");
            var root=new GameObject("Завод");
            try
            {
                var room=root.AddComponent<RoomAuthoring>();
                var container=new GameObject("Комнаты").transform;container.SetParent(root.transform,false);
                root.AddComponent<FactoryLevelRoot>().Configure(catalog,room,container);
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool saved);
                if(!saved) throw new IOException("Не записан корень завода: "+PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void RunBatch()
        {
            int code=0;
            try {Generate();Check();}
            catch(Exception error) {Debug.LogError("Подготовка корня завода прервана: "+error);code=1;}
            EditorApplication.Exit(code);
        }
        public static void Check()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
            var world=new WorldState{seed=104};
            FactoryPreparation.Create(world,catalog,0);
            var root=FactoryLevelRoot.Create(world,"factory:0");
            try
            {
                var geometry=root.Authoring.CreateLayout(false);
                if(geometry.platforms.Length!=root.Prepared.Physics.platforms.Length || root.Objects.Count==0 ||
                    root.Objects.Select(o=>o.Id).Distinct(StringComparer.Ordinal).Count()!=root.Objects.Count)
                    throw new InvalidOperationException("Геометрия или ID объектов корня не совпали с подготовленными данными.");
                root.InstantiateRooms();
                if(root.Rooms.childCount!=84) throw new InvalidOperationException("Число комнат корня не совпало с раскладкой.");
                var identity=FactoryContentIdentity.Capture(world);
                var copy=StateJson.Copy(world);
                if(!identity.Matches(copy)) throw new InvalidOperationException("Идентичность заводов изменилась после копирования мира.");
                copy.seed++;
                if(identity.Matches(copy)) throw new InvalidOperationException("Изменение сида мира не обнаружено.");
                copy.seed=world.seed;
                copy.factories[0].layout+=" ";
                if(identity.Matches(copy)) throw new InvalidOperationException("Изменение сохранённой раскладки не обнаружено.");
                world.actors.Add(new ActorState{id="factory-check",player=true,connected=true,level="factory:0"});
                int loads=0;
                using var simulation=new SimulationGeometry(level=>{loads++;return root.Authoring.CreateLayout(false);});
                simulation.Reconcile(world);
                world.factories[0].layout+=" ";
                simulation.Reconcile(world);
                if(loads!=2) throw new InvalidOperationException("Кэш физики не обновился при смене сохранённой раскладки.");
                Debug.Log($"Корень игрового завода: PASS; комнат {root.Rooms.childCount}, объектов {root.Objects.Count}, геометрия из общей сетки; кэш обнаруживает смену раскладки.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root.gameObject);}
        }
    }
}
