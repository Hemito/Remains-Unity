using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class PublishOriginalTrainingHub
    {
        public static void Publish()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            var root=PrefabUtility.LoadPrefabContents("Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab");
            try
            {
                root.name="Хаб";
                PrefabUtility.SaveAsPrefabAsset(root,"Assets/Remains/Resources/Levels/hub.prefab");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            var scene=EditorSceneManager.OpenScene("Assets/Remains/Scenes/HubAuthoring.unity",OpenSceneMode.Single);
            foreach(var old in scene.GetRootGameObjects().Where(o=>o.GetComponent<RoomAuthoring>()!=null)) UnityEngine.Object.DestroyImmediate(old);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(RoomAuthoring.Load("hub").gameObject);
            var camera=scene.GetRootGameObjects().Select(o=>o.GetComponent<Camera>()).Single(c=>c!=null);
            camera.transform.position=new Vector3(38.4f,10,-10);camera.orthographicSize=22;
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            VerifyAndBuild();
        }
        public static void VerifyAndBuild()
        {
            Anatomy.Load();var layout=RoomLayout.Load("hub");
            foreach(float x in new[]{40f,46f,56f,60f})
            {
                var actor=new ActorState {id="old-annex-"+x,level="hub",x=x,y=.8f,health=63,mana=42};
                actor.inventory.Add(SharedInventory.Create("heal",3));string item=actor.inventory[0].id;
                SavedActorPlacement.Repair(actor,layout,"проверка переноса старой пристройки");
                if(!ActorTerrainMotion.IsClear(layout,Anatomy.Movement(actor),actor.Position) ||
                    !ActorTerrainMotion.Supported(layout,Anatomy.Movement(actor),actor.Position) || actor.health!=63 || actor.mana!=42 || actor.inventory[0].id!=item)
                    throw new InvalidOperationException("Не восстановлена сохранённая позиция старой пристройки: "+x);
            }
            TrainingContentMigrationChecks.Run();TrainingGameplayPreparation.Verify();
            PrototypeBuild.Build();
        }
    }
}
