using System;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Adds the template level (HubTemplateLevel: camp rbl room_0_0 + training room room_1_0), the level view and its
    // camera focus to the hub prefab; gameplay content of the prefab is left as is. Re-running replaces the setup.
    //   batch -executeMethod Remains.Editor.HubTemplateAuthoring.RunBatch
    public static class HubTemplateAuthoring
    {
        public const string HubPrefab="Assets/Remains/Resources/Levels/hub.prefab";
        private static readonly string[] Rooms={"room_0_0","room_1_0"};
        private const string ContainerName="Комнаты шаблона";

        [MenuItem("Remains/Комнаты/Подключить хаб к шаблонам и новому свету")]
        public static void Generate()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath)
                ??throw new InvalidOperationException("Нет каталога игрового завода: "+FactoryCatalogAuthoring.AssetPath);
            var templates=Rooms.Select(r=>AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>($"Assets/Remains/Rooms/Original/rbl/{r}.room.json")
                ??throw new InvalidOperationException("Нет шаблона хаба "+r)).ToArray();
            var prefabs=Rooms.Select(r=>AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Remains/Rooms/Original/Prefabs/rbl/{r}.prefab")?.GetComponent<RoomTemplateInstance>()
                ??throw new InvalidOperationException("Нет префаба комнаты хаба "+r)).ToArray();
            var root=PrefabUtility.LoadPrefabContents(HubPrefab);
            try
            {
                var old=root.transform.Find(ContainerName);   // Editor authoring: replace the previous setup
                if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var container=new GameObject(ContainerName).transform;container.SetParent(root.transform,false);
                if(!root.TryGetComponent<HubTemplateLevel>(out var hub)) hub=root.AddComponent<HubTemplateLevel>();
                hub.Configure(catalog,templates,prefabs,container);
                if(!root.TryGetComponent<FactoryLevelView>(out var view)) view=root.AddComponent<FactoryLevelView>();
                if(!root.TryGetComponent<FactoryLightFocus>(out var focus)) focus=root.AddComponent<FactoryLightFocus>();
                focus.Configure(view);
                PrefabUtility.SaveAsPrefabAsset(root,HubPrefab,out bool saved);
                if(!saved) throw new System.IO.IOException("Не записан префаб хаба "+HubPrefab);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            Debug.Log("Хаб подключён к шаблонам: "+string.Join(", ",Rooms)+".");
        }

        public static void RunBatch()
        {
            int code=0;
            try {Generate();}
            catch(Exception error) {Debug.LogError("Подключение хаба прервано: "+error);code=1;}
            EditorApplication.Exit(code);
        }
    }
}
