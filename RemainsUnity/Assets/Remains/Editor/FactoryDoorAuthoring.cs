using System;
using System.IO;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryDoorAuthoring
    {
        private const string Folder="Assets/Remains/Resources/Presentation/Doors";
        private const string Draft="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
        public static void InstallContentVersion()
        {
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                if(root.GetComponent<RoomDoorContent>()!=null) throw new InvalidOperationException("Версия дверей уже задана.");
                root.AddComponent<RoomDoorContent>().Configure("base:factory-entry-46:doors-v1");
                var layout=RoomDoorContent.Read(root.GetComponent<RoomAuthoring>(),"factory:0");
                if(layout.doors.Length!=3) throw new InvalidOperationException("Потеряна расстановка трёх дверей.");
                var world=new Remains.Domain.WorldState();DoorContentInstaller.Apply(world,layout);
                if(world.doors.Count!=3) throw new InvalidOperationException("Не установлены двери черновика.");
                PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            ContractChecks.Run();
            Debug.Log("Расстановка FactoryEntry46: три авторские двери с версией и независимыми состояниями мира.");
        }
        public static void Import()
        {
            if(AssetDatabase.IsValidFolder(Folder)) throw new InvalidOperationException("Префабы дверей уже существуют; авторские правки не заменены.");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            foreach(string file in Directory.GetFiles("Assets/Remains/Resources/Original/Doors","*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(file);
                importer.textureType=TextureImporterType.Default;importer.maxTextureSize=8192;
                importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            }
            Create("door1",1.6f,80f/232,"wood_door_open","wood_door_close");
            CompleteImport();
        }
        public static void CompleteImport()
        {
            AssetDatabase.Refresh();
            if(AssetDatabase.LoadAssetAtPath<DoorAsset>(Folder+"/door1.asset")==null || AssetDatabase.LoadAssetAtPath<DoorAsset>(Folder+"/door2.asset")!=null)
                throw new InvalidOperationException("Продолжение импорта ожидает готовую door1 и отсутствие door2.");
            Create("door2",1.6f,.5f,"metal_door_open","metal_door_close");
            Create("door3",2.4f,.5f,"big_door_close","big_door_close");
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                Place(root,"door1","aaChLmLMEdgLxJEr",29,17,"door:wood");
                Place(root,"door2","K9s9yoNo9KR4GVb3",14,2,"floor");
                Place(root,"door3","YdDyzvJujerru2kS",11,23,"floor");
                PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Verify();
        }
        private static void Create(string id,float height,float pivotX,string opening,string closing)
        {
            Sprite Sprite(string state)
            {
                var existing=AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{id}-{state}.asset");
                if(existing!=null) return existing;
                var texture=Resources.Load<Texture2D>("Original/Doors/"+id+"-"+state);
                if(texture==null) throw new InvalidOperationException("Нет изображения двери "+id+" "+state);
                var sprite=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(pivotX,0),200,0,SpriteMeshType.FullRect);
                AssetDatabase.CreateAsset(sprite,$"{Folder}/{id}-{state}.asset");return sprite;
            }
            var asset=ScriptableObject.CreateInstance<DoorAsset>();
            asset.Configure(id,new Vector2(.8f,height),Sprite("closed"),Sprite("open"),
                Resources.Load<AudioClip>("Original/Doors/"+opening),Resources.Load<AudioClip>("Original/Doors/"+closing));
            AssetDatabase.CreateAsset(asset,$"{Folder}/{id}.asset");
            var material=WorldArtworkMaterial.Create(asset.Closed.texture);AssetDatabase.CreateAsset(material,$"{Folder}/{id}.mat");
            var root=new GameObject(id);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=ActorVisualLayers.WorldObjectOrder;
                root.AddComponent<DoorView>().Configure(asset,renderer);
                PrefabUtility.SaveAsPrefabAsset(root,$"{Folder}/{id}.prefab");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Place(GameObject root,string id,string source,int x,int y,string material)
        {
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{id}.prefab"),root.transform);
            instance.name=$"Дверь {id} · {source}";
            instance.transform.localPosition=new Vector3((x+.5f)*.8f,(25-y-1)*.8f,0);
            instance.AddComponent<RoomDoorAuthoring>().Configure("original:plant:46:"+source,false,material);
        }
        public static void Verify()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Draft));
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                var doors=root.GetComponentsInChildren<RoomDoorAuthoring>();
                if(doors.Length!=3) throw new InvalidOperationException("В черновике нужны три исходные двери.");
                foreach(var door in doors)
                {
                    var state=door.Read(room,"factory:0");var view=door.GetComponent<DoorView>();
                    var position=view.transform.position;view.Show(true);
                    if(view.Artwork.sprite!=view.Definition.Opened || view.transform.position!=position) throw new InvalidOperationException("Открытие сместило дверь.");
                    view.Show(false);
                    if(Mathf.Abs(state.width-.8f)>.0001f || Mathf.Abs(view.Artwork.bounds.min.x-state.x)>.001f ||
                        Mathf.Abs(view.Artwork.bounds.min.y-state.y)>.001f || Mathf.Abs(view.Artwork.bounds.size.y-state.height)>.001f)
                        throw new InvalidOperationException("Графика и проём двери расходятся: "+state.id);
                }
                root.AddComponent<RoomLighting>().Initialize(layout,root.GetComponent<RoomLightingAuthoring>().CreateProfile(room));
                OriginalTrainingRoomAuthoring.Render(root,layout,"factory-entry-46-doors-closed.png");
                foreach(var door in doors) door.GetComponent<DoorView>().Show(true);
                OriginalTrainingRoomAuthoring.Render(root,layout,"factory-entry-46-doors-open.png");
                Debug.Log("Двери черновика: PASS — 3 префаба/SO, исходные позиции, неизменный pivot и закрытый/открытый рендер.");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);}
            ContractChecks.Run();
        }
    }
}
