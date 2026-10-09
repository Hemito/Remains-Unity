using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Remains.Content;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Remains.Editor
{
    // Preview of room templates (stage B). Menu: builds the selected template into the open
    // scene. Batch (graphics device required, no window): renders the plant templates next to
    // the shipped prefabs of the same rooms, plus optional external *.room.json files (not
    // imported into the project), with the land's far background:
    //   -executeMethod Remains.Editor.RoomTemplatePreview.RenderBatch -roomPreviewOut <dir> [-roomPreviewFiles <a;b>] [-roomPreviewUnlit]
    // -roomPreviewUnlit: templates only, no lights at all (plain colours, "<name>-albedo.png") — input
    // for the reference light tracer (RoomConvert trace --albedo-image).
    public static class RoomTemplatePreview
    {
        private const string Definitions="Assets/Remains/Rooms/Original/definitions.json";
        private const string Lands="Assets/Remains/Rooms/Original/lands.json";
        private const string Lighting="Assets/Remains/Rooms/Original/room-lighting.json";
        private static readonly (string template,string prefab)[] Rooms=
        {
            ("Assets/Remains/Rooms/Original/plant/tsekh1.room.json","Assets/Remains/RoomDrafts/FactoryWorkshop18.prefab"),
            ("Assets/Remains/Rooms/Original/plant/vkhod2.room.json","Assets/Remains/RoomDrafts/FactoryEntry46.prefab"),
        };

        private static bool unlit;

        [MenuItem("Remains/Комнаты/Предпросмотр выбранного шаблона")]
        public static void PreviewSelected()
        {
            var template=Selection.activeObject as RoomTemplateAsset;
            if(template==null) {Debug.LogWarning("Выберите ассет шаблона комнаты (*.room.json) в окне Project.");return;}
            var view=Build(template.Room,Vector2.zero);
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
                unlit=Array.IndexOf(Environment.GetCommandLineArgs(),"-roomPreviewUnlit")>=0;
                foreach(var (templatePath,prefabPath) in Rooms)
                {
                    var template=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(templatePath)??throw new InvalidOperationException("Нет шаблона "+templatePath);
                    string name=Path.GetFileName(templatePath).Replace(".room.json","");
                    RenderRoom(template.Room,Path.Combine(outDir,name+(unlit?"-albedo.png":"-template.png")));
                    if(unlit) continue;

                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)??throw new InvalidOperationException("Нет префаба "+prefabPath);
                    UnityEngine.Object.Instantiate(prefab);
                    Render(Size(template.Room),Path.Combine(outDir,name+"-prefab.png"),null);
                }
                string files=Argument("-roomPreviewFiles");
                if(!string.IsNullOrEmpty(files))
                    foreach(string file in files.Split(';'))
                    {
                        var room=RoomSerializer.Read(File.ReadAllText(file,Encoding.UTF8),file);
                        RenderRoom(room,Path.Combine(outDir,room.Template.Replace(':','_')+".png"));
                    }
                Debug.Log("Предпросмотр комнат: "+outDir);
            }
            catch(Exception error) {Debug.LogError("Предпросмотр комнат прерван: "+error);code=1;}
            EditorApplication.Exit(code);
        }

        private static void RenderRoom(RoomFile room,string file)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Build(room,Vector2.zero);
            Render(Size(room),file,camera=>FarBackground(room,camera));
        }

        private static Vector2 Size(RoomFile room)=>new Vector2((float)(room.Width*room.Cell),(float)(room.Height*room.Cell));

        private static GameObject Build(RoomFile room,Vector2 origin)
        {
            var lightingAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(Lighting)??throw new InvalidOperationException("Нет таблицы света "+Lighting);
            var table=RoomLightingTable.Read(lightingAsset.text,Lighting);
            var view=RoomTerrainView.Build(room,LoadDefinitions(),LoadLands(),LoadArt(),origin,table.ShadowEdge);
            if(!unlit) RoomLightingView.Build(room,LoadDefinitions(),LoadLands(),table,origin,view.transform.parent);
            return view;
        }

        // Original: the land's fon, "fonDefault" when the land names none.
        private static void FarBackground(RoomFile room,Camera camera)
        {
            var issues=new List<string>();
            string fon=LoadLands().Option(room,"fon",issues);
            foreach(string issue in issues) Debug.LogWarning(issue);
            if(string.IsNullOrEmpty(fon)) fon="fonDefault";
            var texture=LoadArt().Texture(TileManifest.TextureId(fon));
            var material=WorldArtworkMaterial.Create(texture);
            material.SetFloat("_Emission",1);
            FarBackgroundView.Create(camera,texture,material);
        }

        private static RoomDefinitionCatalog LoadDefinitions()
        {
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>(Definitions)??throw new InvalidOperationException("Нет каталога "+Definitions);
            return RoomDefinitionCatalog.Read(asset.text,Definitions);
        }

        private static RoomLandCatalog LoadLands()
        {
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>(Lands)??throw new InvalidOperationException("Нет каталога земель "+Lands);
            return RoomLandCatalog.Read(asset.text,Lands);
        }

        private static RoomTileArtAsset LoadArt()=>AssetDatabase.LoadAssetAtPath<RoomTileArtAsset>(RoomTileArtAuthoring.AssetPath)
            ??throw new InvalidOperationException("Нет набора графики клеток: выполните «Remains/Комнаты/Обновить набор графики клеток».");

        private static void Render(Vector2 size,string file,Action<Camera> setup)
        {
            const int pixelsPerMeter=50;
            int width=Mathf.RoundToInt(size.x*pixelsPerMeter),height=Mathf.RoundToInt(size.y*pixelsPerMeter);
            var cameraObject=new GameObject("Камера предпросмотра");
            var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=size.y/2;
            camera.transform.position=new Vector3(size.x/2,size.y/2,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.12f,.14f,1);
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            camera.targetTexture=target;camera.aspect=(float)width/height;
            setup?.Invoke(camera);
            TickLighting();
            // 2D shadow culling compares casters with each light's position cached by the previous
            // frame (new lights sit at the origin until then): a warm-up frame first.
            camera.Render();
            camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            RenderTexture.active=previous;
            File.WriteAllBytes(file,image.EncodeToPNG());
            camera.targetTexture=null;
            UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        // Preview-only: a batch render has no player loop, so 2D lights (mesh in LateUpdate) and
        // shadow casters (shape in Update) are ticked once by hand before the single frame.
        private static void TickLighting()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach(var caster in root.GetComponentsInChildren<ShadowCaster2D>(true)) caster.Update();
                foreach(var light in root.GetComponentsInChildren<Light2D>(true)) light.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver);
            }
        }

        private static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++) if(args[i]==name) return args[i+1];
            return null;
        }
    }
}
