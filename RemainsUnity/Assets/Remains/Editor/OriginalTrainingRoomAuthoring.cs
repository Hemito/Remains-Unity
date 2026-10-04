using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // Draft content import only. Connecting it to the live hub is a separate migration.
    public static class OriginalTrainingRoomAuthoring
    {
        public static void Import()
        {
            const string folder="Assets/Remains/RoomDrafts";
            const string path=folder+"/OriginalTrainingRoom.prefab";
            if(AssetDatabase.LoadMainAssetAtPath(path)!=null)
                throw new InvalidOperationException("Черновик стрельбища уже существует; повторный импорт не должен стирать правки.");
            AssetDatabase.Refresh();
            foreach(string file in Directory.GetFiles("Assets/Remains/Resources/Original/Room/TrainingRoom"))
            {
                if(AssetImporter.GetAtPath(file) is not TextureImporter texture) continue;
                texture.textureCompression=TextureImporterCompression.Uncompressed;
                texture.maxTextureSize=8192;texture.mipmapEnabled=false;texture.alphaIsTransparency=true;
                texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Bilinear;texture.SaveAndReimport();
            }
            if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Remains","RoomDrafts");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var input=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/original/training-room-layout.json"));
            var layout=JsonUtility.FromJson<RoomLayout>(File.ReadAllText(input));
            var root=new GameObject("Оригинальное стрельбище — геометрия, без интерактивных объектов");
            var light=new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()};
            try
            {
                var authoring=RoomDraftGeometry.Create(root,layout);
                var visual=RoomVisual.Build(layout,light);visual.name="Оформление";
                RoomArtworkAssets.Save(visual,folder,"TrainingRoomArtwork");visual.transform.SetParent(root.transform,false);
                authoring.UseAuthoredArtwork();
                var compiled=authoring.CreateLayout();
                if(compiled.platforms.Length!=layout.platforms.Length || compiled.ladders.Length!=2 || compiled.artwork.Length!=layout.artwork.Length)
                    throw new InvalidOperationException("Импорт потерял геометрию или оформление стрельбища.");
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
                root.AddComponent<RoomLighting>().Initialize(layout,light);
                Render(root,layout);
                Debug.Log($"Черновик стрельбища: поверхностей {compiled.platforms.Length}, лестниц 2, слоёв {compiled.artwork.Length}. Объекты и соединение с хабом ещё не импортированы.");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);}
        }
        internal static void Render(GameObject room,RoomLayout layout,string fileName="original-training-room-draft.png")
        {
            var camera=new GameObject("Камера проверки стрельбища").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=10.5f;camera.transform.position=new Vector3(19.2f,10,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.09f,.1f);
            var target=new RenderTexture(3840,2100,24);target.Create();camera.targetTexture=target;
            var pixels=new Texture2D(3840,2100,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,3840,2100),0,0);pixels.Apply();
                var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/"+fileName));
                File.WriteAllBytes(output,pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}
