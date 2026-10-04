using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // Staged replacement: saves and gameplay content migrate before publishing this as Levels/hub.
    public static class ConnectedTrainingHubAuthoring
    {
        private const string Path="Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab";
        public static void Create()
        {
            if(AssetDatabase.LoadMainAssetAtPath(Path)!=null) throw new InvalidOperationException("Соединённый хаб уже существует; ручные правки нельзя стирать импортом.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/Resources/Levels/hub.prefab"));
            try
            {
                root.name="Хаб с оригинальным стрельбищем — подготовка игровой интеграции";
                var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                var annex=root.transform.Find("Тир");
                if(annex==null || Mathf.Abs(layout.right-61.6f)>.001f) throw new InvalidOperationException("Исходная временная пристройка изменилась; требуется проверка миграции.");
                UnityEngine.Object.DestroyImmediate(annex.gameObject);
                foreach(var light in root.GetComponentsInChildren<RoomLightAuthoring>())
                    if(light.Read(room).id.StartsWith("range-",StringComparison.Ordinal)) UnityEngine.Object.DestroyImmediate(light.gameObject);
                // Restore the four lower-wall cells removed for the temporary annex.
                var surface=new RoomSurface {x=37.6f,y=.8f,width=.8f,height=3.2f,texture="Hub/concrete"};
                var wallLayout=new RoomLayout {id="restored-hub-wall",left=37.6f,right=38.4f,bottom=0,top=4,
                    platforms=new[]{surface},ladders=Array.Empty<RoomLadder>(),artwork=new[]{
                        new RoomArtwork {x=37.6f,y=.8f,width=.8f,height=3.2f,texture="Hub/concrete",order=-4,repeat=true,shade=.78f},
                        new RoomArtwork {x=37.5625f,y=.8f,width=.075f,height=3.2f,texture="Hub/rim",order=-3,repeat=true,shade=.78f}}};
                var wall=RoomVisual.Build(wallLayout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});
                RoomArtworkAssets.Save(wall,"Assets/Remains/RoomDrafts","RestoredHubWallArtwork");
                wall.name="Восстановленная нижняя стена хаба";wall.transform.SetParent(root.transform,false);
                var collision=new GameObject("Стена");collision.transform.SetParent(wall.transform,false);
                collision.AddComponent<RoomSurfaceAuthoring>().Configure(surface);
                var training=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/OriginalTrainingRoom.prefab"));
                training.name="Оригинальное стрельбище";training.transform.SetParent(root.transform,false);training.transform.localPosition=new Vector3(38.4f,0,0);
                UnityEngine.Object.DestroyImmediate(training.GetComponent<RoomAuthoring>());
                UnityEngine.Object.DestroyImmediate(training.GetComponent<RoomLightingAuthoring>());
                layout.right=76;layout.top=20;room.Configure(layout);
                Check(root);PrefabUtility.SaveAsPrefabAsset(root,Path);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Verify();
        }
        private static void Check(GameObject root)
        {
            Anatomy.Load();var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
            if(root.GetComponentsInChildren<RoomAuthoring>().Length!=1 || root.GetComponentsInChildren<RoomActorPlacement>().Length!=5 ||
                root.GetComponentsInChildren<RoomContainerPlacement>().Length!=5) throw new InvalidOperationException("Соединённая комната потеряла содержимое или содержит отдельные корни геометрии.");
            var profile=root.GetComponent<RoomLightingAuthoring>().CreateProfile(room);
            if(profile.lights.Count(l=>l.id.StartsWith("training-lamp-",StringComparison.Ordinal))!=7 ||
                profile.lights.Any(l=>l.id.StartsWith("range-",StringComparison.Ordinal))) throw new InvalidOperationException("Источники временного тира не заменены.");
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var actor=new ActorState {id="original-training-passage",x=35,y=7.2f};var movement=new MovementController(layout);
                for(int i=0;i<Mathf.RoundToInt(3/dt);i++) movement.Step(actor,Vector2.right,false,dt);
                if(actor.x<42 || Mathf.Abs(actor.y-7.2f)>.001f) throw new InvalidOperationException($"Стык хаб→тир заблокирован: dt={dt}, position={actor.Position}.");
                for(int i=0;i<Mathf.RoundToInt(3/dt);i++) movement.Step(actor,Vector2.left,false,dt);
                if(actor.x>37 || Mathf.Abs(actor.y-7.2f)>.001f) throw new InvalidOperationException($"Стык тир→хаб заблокирован: dt={dt}, position={actor.Position}.");
            }
            Debug.Log("Исходный проход хаб/тир: движение в обе стороны на высоте 7,2 м при 30/60/144 Гц прошло, без смены уровня и телепорта.");
        }
        public static void Verify()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            var camera=new GameObject("Проверка соединённого хаба").AddComponent<Camera>();
            try
            {
                Check(root);var room=root.GetComponent<RoomAuthoring>();
                root.AddComponent<RoomLighting>().Initialize(room.CreateLayout(),root.GetComponent<RoomLightingAuthoring>().CreateProfile(room));
                camera.orthographic=true;camera.orthographicSize=10.5f;camera.transform.position=new Vector3(38.4f,10,-10);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.08f,.09f);
                Remains.Runtime.NetworkFrameCapture.Save(camera,System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/tests/connected-training-hub.png")),width:7680,height:2100);
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera.gameObject);}
        }
    }
}
