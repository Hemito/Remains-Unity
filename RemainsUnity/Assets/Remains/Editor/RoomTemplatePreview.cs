using System;
using System.IO;
using Remains.Content;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // Preview of a room template's terrain (stage B3). Menu: builds it into the open scene.
    // Batch (graphics device required, no window): renders templates and the shipped prefabs
    // of the same rooms to PNG for side-by-side comparison:
    //   -executeMethod Remains.Editor.RoomTemplatePreview.RenderBatch -roomPreviewOut <dir>
    public static class RoomTemplatePreview
    {
        private const string Definitions="Assets/Remains/Rooms/Original/definitions.json";
        private const string Lands="Assets/Remains/Rooms/Original/lands.json";
        private static readonly (string template,string prefab)[] Rooms=
        {
            ("Assets/Remains/Rooms/Original/plant/tsekh1.room.json","Assets/Remains/RoomDrafts/FactoryWorkshop18.prefab"),
            ("Assets/Remains/Rooms/Original/plant/vkhod2.room.json","Assets/Remains/RoomDrafts/FactoryEntry46.prefab"),
        };

        [MenuItem("Remains/Комнаты/Предпросмотр выбранного шаблона")]
        public static void PreviewSelected()
        {
            var template=Selection.activeObject as RoomTemplateAsset;
            if(template==null) {Debug.LogWarning("Выберите ассет шаблона комнаты (*.room.json) в окне Project.");return;}
            var view=Build(template,Vector2.zero);
            Undo.RegisterCreatedObjectUndo(view,"Предпросмотр комнаты");
            Selection.activeObject=view;
        }

        public static void RenderBatch()
        {
            int code=0;
            try
            {
                string outDir=Argument("-roomPreviewOut")??throw new ArgumentException("Нет аргумента -roomPreviewOut <папка>.");
                Directory.CreateDirectory(outDir);
                RoomTileArtAuthoring.Build();
                foreach(var (templatePath,prefabPath) in Rooms)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                    var template=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(templatePath)??throw new InvalidOperationException("Нет шаблона "+templatePath);
                    var room=template.Room;
                    var size=new Vector2((float)(room.Width*room.Cell),(float)(room.Height*room.Cell));
                    Build(template,Vector2.zero);
                    string name=Path.GetFileName(templatePath).Replace(".room.json","");
                    Render(size,Vector2.zero,Path.Combine(outDir,name+"-template.png"));

                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)??throw new InvalidOperationException("Нет префаба "+prefabPath);
                    UnityEngine.Object.Instantiate(prefab);
                    Render(size,Vector2.zero,Path.Combine(outDir,name+"-prefab.png"));
                    Debug.Log($"Предпросмотр {name}: {outDir}");
                }
            }
            catch(Exception error) {Debug.LogError("Предпросмотр комнат прерван: "+error);code=1;}
            EditorApplication.Exit(code);
        }

        private static GameObject Build(RoomTemplateAsset template,Vector2 origin)
        {
            var definitionsAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(Definitions)??throw new InvalidOperationException("Нет каталога "+Definitions);
            var art=AssetDatabase.LoadAssetAtPath<RoomTileArtAsset>(RoomTileArtAuthoring.AssetPath)
                ??throw new InvalidOperationException("Нет набора графики клеток: выполните «Remains/Комнаты/Обновить набор графики клеток».");
            var landsAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(Lands)??throw new InvalidOperationException("Нет каталога земель "+Lands);
            return RoomTerrainView.Build(template.Room,RoomDefinitionCatalog.Read(definitionsAsset.text,Definitions),RoomLandCatalog.Read(landsAsset.text,Lands),art,origin);
        }

        private static void Render(Vector2 size,Vector2 origin,string file)
        {
            const int pixelsPerMeter=50;
            int width=Mathf.RoundToInt(size.x*pixelsPerMeter),height=Mathf.RoundToInt(size.y*pixelsPerMeter);
            var cameraObject=new GameObject("Камера предпросмотра");
            var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=size.y/2;
            camera.transform.position=new Vector3(origin.x+size.x/2,origin.y+size.y/2,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.12f,.14f,1);
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            camera.targetTexture=target;camera.aspect=(float)width/height;
            camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            RenderTexture.active=previous;
            File.WriteAllBytes(file,image.EncodeToPNG());
            camera.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++) if(args[i]==name) return args[i+1];
            return null;
        }
    }
}
