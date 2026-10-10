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
    // -roomPreviewRadiance: lamps by RoomRadianceField (radiance cascades) instead of URP lamp lights.
    // -roomPreviewPrefabs: the generated room prefabs (RoomPrefabGenerator), normal and mirrored, over the
    // template's walls from a one-room level grid (FactoryGridBuilder; edge walls become the land border),
    // unlit: "<name>-prefab-n.png" / "<name>-prefab-m.png".
    // -factoryPreview [-factorySeed 1 -factorySize 2x3]: a generated factory of that size (FactoryLayoutGenerator)
    // as the level would be prepared: walls from the composed grid (FactoryGridBuilder), room prefab instances
    // (RoomPrefabGenerator), their sprites into the radiance field: "factory-<seed>-<W>x<D>.png".
    // -roomPreviewDummy: test only — training dummies (sprite of Prefabs/Actors/training-dummy) stand
    // in the room as light absorbers/receivers, to check actor shadows.
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

        private static bool unlit,radiance,dummy;
        private const string Prefabs="Assets/Remains/Rooms/Original/Prefabs/plant";
        private const string DummyPrefab="Assets/Remains/Prefabs/Actors/training-dummy.prefab";
        // Test positions (m, foot point) per template: between the lamps of the lower rooms.
        private static readonly Dictionary<string,Vector2[]> DummySpots=new()
        {
            ["remains:plant:vkhod2"]=new[]{new Vector2(25.6f,0.8f),new Vector2(31f,0.8f)},
            ["remains:plant:tsekh1"]=new[]{new Vector2(5.2f,0.8f)},
            ["remains:plant:lighttest"]=new[]{new Vector2(9.6f,0.8f)},
        };
        private const int RadianceSettleUpdates=8;

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
                radiance=Array.IndexOf(Environment.GetCommandLineArgs(),"-roomPreviewRadiance")>=0;
                dummy=Array.IndexOf(Environment.GetCommandLineArgs(),"-roomPreviewDummy")>=0;
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-factoryPreview")>=0)
                {
                    RenderFactory(outDir);
                    EditorApplication.Exit(0);
                    return;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-roomPreviewPrefabs")>=0)
                {
                    foreach(var (templatePath,_) in Rooms) RenderPrefabs(templatePath,outDir);
                    Debug.Log("Предпросмотр префабов комнат: "+outDir);
                    EditorApplication.Exit(0);
                    return;
                }
                foreach(var (templatePath,prefabPath) in Rooms)
                {
                    var template=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(templatePath)??throw new InvalidOperationException("Нет шаблона "+templatePath);
                    string name=Path.GetFileName(templatePath).Replace(".room.json","");
                    RenderRoom(template.Room,Path.Combine(outDir,name+(unlit?"-albedo.png":radiance?"-radiance.png":"-template.png")));
                    if(unlit || radiance) continue;

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

        private static void RenderPrefabs(string templatePath,string outDir)
        {
            var template=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(templatePath)??throw new InvalidOperationException("Нет шаблона "+templatePath);
            var room=template.Room;
            string name=Path.GetFileName(templatePath).Replace(".room.json","");
            foreach(bool mirror in new[]{false,true})
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var layout=new FactoryLayout{Land=RoomLandCatalog.LandOf(room.Template),Width=1,Depth=1};
                layout.Cells.Add(new FactoryCell{Template=room.Template,Mirror=mirror});
                var grid=FactoryGridBuilder.Build(layout,new Dictionary<string,RoomFile>{[room.Template]=room},LoadDefinitions(),
                    FactoryGridBuilder.BorderSolid(LoadLands(),layout.Land,LoadDefinitions()),closeOuter:false);
                foreach(string issue in grid.Issues) Debug.LogWarning(issue);
                var level=grid.Level;
                level.Template=room.Template;   // land and room options of this one room
                foreach(var option in room.Options) level.Options[option.Key]=option.Value;
                var view=RoomTerrainView.Build(level,LoadDefinitions(),LoadLands(),LoadArt(),Vector2.zero,0);
                string prefabPath=$"{Prefabs}/{name}{(mirror?"-m":"")}.prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)??throw new InvalidOperationException("Нет префаба "+prefabPath);
                ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).transform.SetParent(view.transform,false);
                Shader.SetGlobalFloat("_RemainsEraseEnabled",0);
                Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
                Render(Size(room),Path.Combine(outDir,$"{name}-prefab-{(mirror?"m":"n")}.png"),camera=>FarBackground(room,camera));
            }
        }

        private static void RenderFactory(string outDir)
        {
            ulong seed=ulong.Parse(Argument("-factorySeed")??"1",System.Globalization.CultureInfo.InvariantCulture);
            string[] size=(Argument("-factorySize")??"2x3").Split('x');
            int width=int.Parse(size[0]),depth=int.Parse(size[1]);
            var files=new Dictionary<string,string>(StringComparer.Ordinal);   // template -> file name (assets unload with the new scene)
            var rooms=new Dictionary<string,RoomFile>(StringComparer.Ordinal);
            foreach(string guid in AssetDatabase.FindAssets("t:"+nameof(RoomTemplateAsset),new[]{"Assets/Remains/Rooms/Original/plant"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var asset=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(path);
                files.Add(asset.Template,Path.GetFileName(path).Replace(".room.json",""));
                rooms.Add(asset.Template,asset.Room);
            }
            var definitions=LoadDefinitions();var lands=LoadLands();
            var lightingAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(Lighting)??throw new InvalidOperationException("Нет таблицы света "+Lighting);
            var table=RoomLightingTable.Read(lightingAsset.text,Lighting);
            var layout=FactoryLayoutGenerator.Generate(new List<RoomFile>(rooms.Values),"plant",seed,0,width,depth);
            var grid=FactoryGridBuilder.Build(layout,rooms,definitions,FactoryGridBuilder.BorderSolid(lands,"plant",definitions),closeOuter:true);
            foreach(string issue in grid.Issues) Debug.LogWarning("Сетка завода: "+issue);
            var level=grid.Level;
            float cell=(float)level.Cell,levelHeight=level.Height*cell;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var art=LoadArt();   // after the new scene: it unloads assets loaded before
            Shader.SetGlobalFloat("_RemainsEraseEnabled",0);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
            // Room cell (Column, Row from the top) -> bottom-left offset in the level, y up.
            Vector2 Offset(FactoryRoomPlacement p)=>new Vector2(p.Column*cell,levelHeight-(p.Row+p.Template.Height)*cell);
            var backdrops=new List<(RoomFile,Vector2)>();
            foreach(var p in grid.Rooms) backdrops.Add((p.Template,Offset(p)));
            var view=RoomTerrainView.Build(level,definitions,lands,art,Vector2.zero,table.ShadowEdge,null,backdrops);
            var sprites=new List<RoomSpritePlacement>();
            var erases=new List<(Sprite,Matrix4x4)>();
            foreach(var p in grid.Rooms)
            {
                string name=files[p.Cell.Template];
                string prefabPath=$"{Prefabs}/{name}{(p.Cell.Mirror?"-m":"")}.prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)??throw new InvalidOperationException("Нет префаба "+prefabPath);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(view.transform,false);
                instance.transform.localPosition=Offset(p);
                foreach(var authored in instance.GetComponentsInChildren<RoomSpriteAuthoring>())
                {
                    var renderer=authored.GetComponent<SpriteRenderer>();
                    var matrix=authored.transform.localToWorldMatrix;
                    sprites.Add(new RoomSpritePlacement{Definition=definitions.Require(authored.Definition,prefabPath),Sprite=renderer.sprite,Frame=authored.Frame,LocalToWorld=matrix});
                    if(authored.Erase!=null) erases.Add((authored.Erase,matrix));
                }
            }
            var levelSize=new Vector2(level.Width*cell,levelHeight);
            if(erases.Count>0) new GameObject("Стирание фона уровня").AddComponent<RoomDecorResources>().BuildErase(erases,Vector2.zero,levelSize,Resources.Load<Shader>(RoomDecorView.EraseShaderResource));
            RoomLightingView.Build(level,definitions,lands,table,Vector2.zero,null,false);
            var field=RoomRadianceField.Build(level,definitions,lands,table,Vector2.zero,null,sprites);
            for(int i=0;i<RadianceSettleUpdates;i++) field.Render();
            string file=Path.Combine(outDir,$"factory-{seed}-{width}x{depth}.png");
            Render(levelSize,file,camera=>FarBackground(rooms[layout.Cell(0,0).Template],camera));
            Debug.Log($"Предпросмотр завода: {file}; комнат {grid.Rooms.Count}, картинок {sprites.Count}, окон {erases.Count}.");
        }

        private static Vector2 Size(RoomFile room)=>new Vector2((float)(room.Width*room.Cell),(float)(room.Height*room.Cell));

        private static GameObject Build(RoomFile room,Vector2 origin)
        {
            var lightingAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(Lighting)??throw new InvalidOperationException("Нет таблицы света "+Lighting);
            var table=RoomLightingTable.Read(lightingAsset.text,Lighting);
            var view=RoomTerrainView.Build(room,LoadDefinitions(),LoadLands(),LoadArt(),origin,table.ShadowEdge);
            Shader.SetGlobalFloat("_RemainsEraseEnabled",0);
            var sprites=new List<RoomSpritePlacement>();
            var glow=new Dictionary<string,double>();
            foreach(var e in table.Glow) glow.Add(e.Key,e.Value.Threshold);
            RoomDecorView.Build(room,LoadDefinitions(),LoadLands(),LoadArt(),RoomPrefabGenerator.LoadWithoutPicture(),origin,view.transform,sprites,glow);
            if(dummy) PlaceDummies(room,view.transform,sprites);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
            if(!unlit) RoomLightingView.Build(room,LoadDefinitions(),LoadLands(),table,origin,view.transform.parent,!radiance);
            if(radiance) MeasureRadiance(RoomRadianceField.Build(room,LoadDefinitions(),LoadLands(),table,origin,view.transform.parent,sprites),room.Template);
            return view;
        }

        // Test: training dummy sprites as actors ("unit": actor transmittance in the lighting table).
        private static void PlaceDummies(RoomFile room,Transform parent,List<RoomSpritePlacement> sprites)
        {
            if(!DummySpots.TryGetValue(room.Template,out var spots)) {Debug.LogWarning($"Манекен: нет тестовых мест для {room.Template}.");return;}
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefab)?.GetComponent<SpriteRenderer>()??throw new InvalidOperationException("Нет спрайта манекена в "+DummyPrefab);
            var material=new Material(Resources.Load<Shader>("Presentation/WorldSprite")){name="Манекен"};
            var definition=new RoomDefinition{Id="remains:test:training-dummy",Kind=RoomDefinitionKind.Object,ObjectType="unit",ObjectSize=2};
            foreach(var spot in spots)
            {
                var obj=new GameObject("Манекен (тест)");
                obj.transform.SetParent(parent,false);
                obj.transform.localPosition=spot;
                var renderer=obj.AddComponent<SpriteRenderer>();
                renderer.sprite=source.sprite;
                renderer.sharedMaterial=material;
                renderer.sortingOrder=RoomTerrainView.ObjectOrder;
                sprites.Add(new RoomSpritePlacement{Definition=definition,Sprite=source.sprite,Frame=1,LocalToWorld=obj.transform.localToWorldMatrix});
            }
            Debug.Log($"Манекен: {spots.Length} шт. в {room.Template}, спрайт {source.sprite.name} {source.sprite.bounds.size.x:0.##}×{source.sprite.bounds.size.y:0.##} м, точка {source.sprite.pivot}.");
        }

        // Diagnostics: GPU cost of one field update (30 updates, then a readback waits for the GPU).
        private static void MeasureRadiance(RoomRadianceField field,string template)
        {
            // Settle the bounce light (one bounce per update), like a few game frames.
            for(int i=0;i<RadianceSettleUpdates;i++) field.Render();
            Sync();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            const int runs=30;
            for(int i=0;i<runs;i++) field.Render();
            Sync();
            Debug.Log($"Поле излучения {template}: {watch.Elapsed.TotalMilliseconds/runs:0.00} мс на пересчёт (среднее из {runs}, с ожиданием GPU).");
        }

        private static void Sync()
        {
            var probe=Shader.GetGlobalTexture("_RemainsRadiance") as RenderTexture;
            var previous=RenderTexture.active;RenderTexture.active=probe;
            var pixel=new Texture2D(1,1,TextureFormat.RGBAHalf,false);
            pixel.ReadPixels(new Rect(0,0,1,1),0,0);
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(pixel);
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
