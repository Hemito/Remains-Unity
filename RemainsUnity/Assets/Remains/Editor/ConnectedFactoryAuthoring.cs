using System;
using System.Linq;
using Remains.Content;
using Remains.Runtime;
using Remains.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class ConnectedFactoryAuthoring
    {
        public const string Path="Assets/Remains/RoomDrafts/ConnectedFactory46And18.prefab";
        public static void Assemble()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            if(AssetDatabase.LoadMainAssetAtPath(Path)!=null || RoomCatalogAsset.Load().Entries.Any(e=>e.id==LegacyFactoryFixture.DefinitionId))
                throw new InvalidOperationException("Связный завод уже сохранён; повторная сборка не заменяет авторские правки.");
            var root=new GameObject("Завод · вход2 и цех1 · один уровень");
            try
            {
                var entry=Module(root,"Assets/Remains/RoomDrafts/FactoryEntry46.prefab",0);
                var workshop=Module(root,FactoryWorkshopDraft.Path,38.4f);
                var layout=entry.GetComponent<RoomAuthoring>().CreateLayout();layout.id="factory-connected-46-18";layout.right=76.8f;
                var room=root.AddComponent<RoomAuthoring>();room.Configure(layout);room.UseAuthoredArtwork();
                var light=entry.GetComponent<RoomLightingAuthoring>().CreateProfile(entry.GetComponent<RoomAuthoring>());
                var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(light.ambient);lighting.SetBackgroundAmbientScale(light.backgroundAmbientScale);
                root.AddComponent<RoomDoorContent>().Configure("base:factory-connected-46-18:doors-v1");
                root.AddComponent<RoomPhysicalContent>().Configure("base:factory-connected-46-18:physical-v1");
                root.AddComponent<RoomNpcContent>().Configure("base:factory-connected-46-18:npcs-v1");
                ConnectedFactoryChecks.Check(room);
                var saved=PrefabUtility.SaveAsPrefabAsset(root,Path).GetComponent<RoomAuthoring>();
                var catalog=RoomCatalogAsset.Load();catalog.Configure(catalog.Entries.Append(new RoomCatalogAsset.Entry {id=LegacyFactoryFixture.DefinitionId,room=saved}).ToArray());
                EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            var camera=new GameObject("Камера предпросмотра").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=22;
            camera.transform.position=new Vector3(38.4f,10,-10);camera.backgroundColor=new Color(.08f,.09f,.1f);
            EditorSceneManager.SaveScene(scene,"Assets/Remains/Scenes/FactoryAuthoring.unity");
            ContractChecks.Run();Debug.Log("Связный завод: вход46 + цех18, стык x=38,4 на y=10,4; новая игра использует общий уровень, старые определения сохранены.");
        }
        private static GameObject Module(GameObject root,string path,float x)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(source==null) throw new InvalidOperationException("Нет модуля связного завода: "+path);
            var module=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);module.transform.localPosition=new Vector3(x,0,0);return module;
        }
    }
}
