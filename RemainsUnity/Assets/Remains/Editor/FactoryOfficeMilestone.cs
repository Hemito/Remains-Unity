using System.IO;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryOfficeMilestone
    {
        public static void ImportAndBuild()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            var filecab=FactoryFileCabinetAuthoring.Run();var catalog=EntityAssetCatalog.Load();
            catalog.AddProp(filecab);EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();PropCatalog.Load();
            FactoryOfficeDraft.Import();ConnectAndBuild();
        }
        public static void ConnectAndBuild()
        {
            FactoryOfficeConnectionAuthoring.Connect();
            UpdatePreviewCamera();
            PrototypeBuild.Build();
        }
        public static void AlignOfficeLightsAndBuild()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            var root=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();int count=0;
                foreach(var marker in root.GetComponentsInChildren<RoomLightAuthoring>())
                {
                    var light=marker.Read(room);
                    if(light.mask!="Original/Room/Hub/light-mask-light3-1") continue;
                    var art=marker.GetComponentInChildren<RoomArtworkAuthoring>();var canvas=art.Read(room);
                    if(Mathf.Abs(light.x-canvas.x-canvas.width/2)>.001f || Mathf.Abs(light.y-canvas.y-canvas.height/2-.09f)>.001f ||
                        light.maskWidth!=6.17f || light.maskHeight!=3.77f)
                        throw new System.InvalidOperationException("Офисный light3 изменён вручную или регистрация уже исправлена: "+light.id);
                    light.y-=.09f;light.maskY-=.09f;
                    art.transform.SetParent(root.transform,true);marker.Configure(light,light.maskTexture);art.transform.SetParent(marker.transform,true);count++;
                }
                if(count!=2) throw new System.InvalidOperationException("Нужны ровно две первоначальные офисные light3.");
                FactoryOfficeConnectionChecks.CheckOfficeLights(room);
                PrefabUtility.SaveAsPrefabAsset(root,FactoryOfficeDraft.Path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            PrototypeBuild.Build();
        }
        private static void UpdatePreviewCamera()
        {
            const string path="Assets/Remains/Scenes/FactoryAuthoring.unity";
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var camera=Object.FindFirstObjectByType<Camera>();
            if(camera==null) throw new System.InvalidOperationException("Нет камеры предпросмотра связного завода.");
            camera.transform.position=new Vector3(FactoryOfficeConnectionAuthoring.Right/2,10,-10);
            camera.orthographicSize=FactoryOfficeConnectionAuthoring.Right/3.6f+.3f;
            EditorSceneManager.SaveScene(scene,path);
        }
        public static void Preview()
        {
            RoomAuthoringScenes.Verify();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FactoryOfficeDraft.Path));
            var room=root.GetComponent<RoomAuthoring>();RoomLightingPreview.Refresh(room);
            var camera=new GameObject("Предпросмотр офисов").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=11;
            camera.transform.position=new Vector3(19.2f,10,-10);camera.backgroundColor=new Color(.08f,.09f,.1f);
            var target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/FactoryOffice53-preview.png")),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                Object.DestroyImmediate(pixels);Object.DestroyImmediate(target);RoomLightingPreview.Clear();
            }
            Debug.Log("Предпросмотр сохранённого офисного модуля: 1920×1080, исходные текстуры и авторский свет.");
        }
    }
}
