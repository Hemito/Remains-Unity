using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Isolated authoring study. Does not install a runtime light implementation or alter lighting content.
    public static class LightGridStudy
    {
        private const int Divisor=2,ImageWidth=1280,ImageHeight=672;
        private static readonly string Output=Path.GetFullPath("../.reference/light-grid-color-20261103");
        [Serializable] private sealed class Measurement
        {
            public string device,api,limitations;
            public int seed=104,width,height,gridWidth,gridHeight,sources,enabled,seedCells,unseeded;
            public int colorGroups;
            public double prepareMs,directMs,bounceMs,readbackMs,gridPrepareMs,gridSolveMs,gridUploadMs;
            public double currentLampOffMs,gridLampOffMs,currentDoorOpenMs,gridDoorOpenMs;
            public double[] currentLampOffSamples,gridLampOffSamples,currentLampOnSamples,gridLampOnSamples;
            public double[] currentDoorOpenSamples,gridDoorOpenSamples,currentDoorCloseSamples,gridDoorCloseSamples;
            public long lampUpdatedCells,doorUpdatedCells;
            public int doorReadbackTexels,totalCells;
            public long gridGeometryBytes;
            public long currentSceneBytes,currentDirectBytes,currentCacheBytes,currentBounceBytes,gridCpuBytes,gridTextureBytes;
            public float minSourceRadiance,maxSourceRadiance,maxSeedRadiance;
            public string lamp,door;
        }

        public static void RunBatch()
        {
            GameObject visual=null,cameraRoot=null;
            LevelLight benchmark=null;
            GridLightTexture gridTexture=null;
            var fence=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            try
            {
                Directory.CreateDirectory(Output);
                Debug.Log(GridLightField.RunSelfChecks());
                Anatomy.Load();WeaponCatalog.Load();
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");
                var authoring=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));
                var room=authoring.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();var factory=visual.GetComponent<FactoryLevelRoot>();
                view.Synchronize(room,world,1);
                var report=new Measurement{device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                    limitations="Editor batch; CPU submission + GPU completion wall time, not GPU timestamps. Events: median of 3, all samples included. Local bounded rebuild/readback/upload. Scalar intensity per RGB8-normalized hue group; componentwise max of decoded group colours. Overall brightness clamped, hue preserved within RGB8 quantization. No actor shadows/AO. Grid geometry GPU bytes separate. Payloads only, excludes indices, headers, driver/artwork."};
                var clock=Stopwatch.StartNew();benchmark=view.CreateLevelLight();report.prepareMs=clock.Elapsed.TotalMilliseconds;
                if(Environment.GetCommandLineArgs().Contains("-grid-hybrid-study") || Environment.GetCommandLineArgs().Contains("-grid-hybrid-fill-preview"))
                {
                    clock.Restart();
                    using var hybrid=new GridDirectHybrid(benchmark.Scene,benchmark.Sources.All);
                    double hybridPrepareMs=clock.Elapsed.TotalMilliseconds;
                    benchmark.Dispose();benchmark=null;
                    HybridLightStudy.Run(view,factory,world,room,hybrid,hybridPrepareMs,Path.GetFullPath("../.reference/light-grid-hybrid-20261103"));
                    EditorApplication.Exit(0);return;
                }
                GridLightDiagnostics.WriteColorInventory(benchmark.Sources.All,Path.Combine(Output,"source-colors.json"));
                if(Environment.GetCommandLineArgs().Contains("-grid-colors-only")) {EditorApplication.Exit(0);return;}
                clock.Restart();benchmark.Direct.BuildAll();Fence(benchmark.Direct.Sum,fence);report.directMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();benchmark.Bounce.BuildAll();Fence(benchmark.Bounce.Indirect,fence);report.bounceMs=clock.Elapsed.TotalMilliseconds;
                benchmark.Direct.WaitForMeasurements();
                report.currentSceneBytes=benchmark.Scene.Bytes;report.currentDirectBytes=benchmark.Direct.SumBytes;
                report.currentCacheBytes=benchmark.Direct.CacheBytes;report.currentBounceBytes=benchmark.Bounce.Bytes;
                report.width=benchmark.Scene.Width;report.height=benchmark.Scene.Height;
                report.sources=benchmark.Sources.Count;report.enabled=benchmark.Sources.All.Count(s=>s.Enabled);
                report.minSourceRadiance=benchmark.Sources.All.Min(s=>s.Radiance.maxColorComponent);
                report.maxSourceRadiance=benchmark.Sources.All.Max(s=>s.Radiance.maxColorComponent);
                var allCells=new RectInt(0,0,report.width/Divisor,report.height/Divisor);
                clock.Restart();var absorption=GridLightTexture.ReadAbsorption(benchmark.Scene,Divisor,allCells);report.readbackMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();var field=new GridLightField(report.width/Divisor,report.height/Divisor,benchmark.Scene.Texel*Divisor,absorption);
                var seedsBySource=new Dictionary<string,GridLightSeed[]>(StringComparer.Ordinal);
                var seeds=Seeds(benchmark.Scene,benchmark.Sources.All,absorption,report,seedsBySource);
                var seedIndex=new GridLightSeeds(field.Width,seedsBySource);
                report.gridPrepareMs=clock.Elapsed.TotalMilliseconds;
                report.gridWidth=report.width/Divisor;report.gridHeight=report.height/Divisor;
                clock.Restart();field.Solve(seeds);report.gridSolveMs=clock.Elapsed.TotalMilliseconds;
                report.colorGroups=field.GroupCount;
                clock.Restart();gridTexture=new GridLightTexture(field);report.gridUploadMs=clock.Elapsed.TotalMilliseconds;
                report.gridCpuBytes=field.AllocatedBytes+absorption.LongLength*sizeof(float);
                report.gridTextureBytes=(long)report.gridWidth*report.gridHeight*8;
                report.gridGeometryBytes=benchmark.Scene.Bytes;report.totalCells=field.Width*field.Height;
                // Keep an independent absorption scene for grid door timings, release benchmark-only light fields.
                benchmark.Bounce.Dispose();benchmark.Direct.Dispose();

                // A separate view owns the production compositor. The benchmark never shares ownership of its scene.
                view.Focus(new Vector2(room.spawnX,room.spawnY));
                view.Light.Direct.WaitForMeasurements();
                if(Environment.GetCommandLineArgs().Contains("-grid-paired-performance"))
                {
                    PairedLightBenchmark.Run(view,benchmark.Scene,field,seedIndex,absorption,gridTexture,world,Output,
                        report.directMs+report.bounceMs,report.gridSolveMs+report.gridUploadMs,report.prepareMs,report.readbackMs+report.gridPrepareMs);
                    EditorApplication.Exit(0);return;
                }
                cameraRoot=new GameObject("Камера сравнения клеточного света");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.aspect=(float)ImageWidth/ImageHeight;camera.allowHDR=false;camera.allowMSAA=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.07f,.08f);
                var grid=factory.Prepared.Grid;float cell=(float)grid.Level.Cell;
                var doors=world.doors.Where(d=>d.level=="factory:0").ToArray();long revision=1;
                // Reuse validation buffers: allocating a full reference between timing samples would cause GC noise.
                var referenceField=new GridLightField(field.Width,field.Height,field.CellSize,absorption);
                foreach(var (x,y,name) in new[]{(1,1,"hall"),(1,9,"office"),(2,11,"door"),(0,0,"entry")})
                {
                    var placement=grid.Rooms.Single(r=>r.Cell.X==x && r.Cell.Y==y);
                    var bounds=new Rect(placement.Column*cell,(grid.Level.Height-placement.Row-placement.Template.Height)*cell,placement.Template.Width*cell,placement.Template.Height*cell);
                    camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);
                    camera.orthographicSize=Mathf.Max(bounds.height*.5f,bounds.width/(2*camera.aspect));
                    float h=2*camera.orthographicSize;
                    view.SetViewArea(new Rect(bounds.center.x-h*camera.aspect*.5f,bounds.center.y-h*.5f,h*camera.aspect,h));view.Focus(bounds.center);
                    SavePair(view,camera,gridTexture.Texture,name);
                    if(name!="hall") continue;
                    GridLightDiagnostics.WriteColorReport(field,absorption,benchmark.Scene,benchmark.Sources.All,seedsBySource,Path.Combine(Output,"color-check.json"));
                    report.lamp=view.Light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && s.Enabled && s.Id.StartsWith("factory:0:1,1:",StringComparison.Ordinal))
                        .Select(s=>s.Id).OrderBy(s=>s,StringComparer.Ordinal).First();
                    report.currentLampOffSamples=new double[3];report.gridLampOffSamples=new double[3];
                    report.currentLampOnSamples=new double[3];report.gridLampOnSamples=new double[3];
                    var lampCells=seedIndex.Bounds(report.lamp);
                    for(int repeat=0;repeat<3;repeat++)
                    {
                        clock.Restart();view.SyncLamps(new[]{report.lamp},++world.lampRevision);FinishCurrent(view,fence);report.currentLampOffSamples[repeat]=clock.Elapsed.TotalMilliseconds;
                        clock.Restart();seedIndex.SetEnabled(report.lamp,false);
                        var affected=field.Update(seedIndex.Query(field.AffectedRegion(lampCells)),lampCells);gridTexture.Update(field,affected);
                        report.gridLampOffSamples[repeat]=clock.Elapsed.TotalMilliseconds;report.lampUpdatedCells=field.RecomputedCells;
                        VerifyFull(field,referenceField,seedIndex.Query(allCells),"лампа выключена");
                        if(repeat==0) SavePair(view,camera,gridTexture.Texture,"hall-lamp-off");
                        clock.Restart();view.SyncLamps(Array.Empty<string>(),++world.lampRevision);FinishCurrent(view,fence);report.currentLampOnSamples[repeat]=clock.Elapsed.TotalMilliseconds;
                        clock.Restart();seedIndex.SetEnabled(report.lamp,true);
                        affected=field.Update(seedIndex.Query(field.AffectedRegion(lampCells)),lampCells);gridTexture.Update(field,affected);
                        report.gridLampOnSamples[repeat]=clock.Elapsed.TotalMilliseconds;
                        VerifyFull(field,referenceField,seeds,"лампа включена");
                    }
                    report.currentLampOffMs=Median(report.currentLampOffSamples);report.gridLampOffMs=Median(report.gridLampOffSamples);
                    var door=doors.Where(d=>d.id.StartsWith("factory:0:1,1:",StringComparison.Ordinal) && d.Blocks).OrderBy(d=>d.id,StringComparer.Ordinal).FirstOrDefault();
                    if(door==null) throw new InvalidOperationException("В зале seed104 не найдена закрытая дверь для сравнения.");
                    report.door=door.id;
                    var gridDoors=benchmark.Scene.Doors.Values.ToArray();int doorIndex=Array.FindIndex(gridDoors,d=>d.Id==door.id);
                    if(doorIndex<0) throw new InvalidOperationException("Дверь отсутствует в независимой сцене сетки: "+door.id);
                    var original=gridDoors[doorIndex];var doorCells=GridLightTexture.CoarseRect(benchmark.Scene,Divisor,original.Bounds);
                    report.doorReadbackTexels=doorCells.width*doorCells.height*Divisor*Divisor;
                    report.currentDoorOpenSamples=new double[3];report.gridDoorOpenSamples=new double[3];
                    report.currentDoorCloseSamples=new double[3];report.gridDoorCloseSamples=new double[3];
                    for(int repeat=0;repeat<3;repeat++)
                    {
                        clock.Restart();door.open=true;view.SyncDoors(doors,++revision);FinishCurrent(view,fence);report.currentDoorOpenSamples[repeat]=clock.Elapsed.TotalMilliseconds;
                        clock.Restart();gridDoors[doorIndex]=new LevelLightScene.Door(original.Id,original.Bounds,original.Opacity,false);
                        UpdateDoorGrid(benchmark.Scene,gridDoors,doorCells,field,seedIndex,absorption,gridTexture);
                        report.gridDoorOpenSamples[repeat]=clock.Elapsed.TotalMilliseconds;report.doorUpdatedCells=field.RecomputedCells;
                        VerifyFull(field,referenceField,seeds,"дверь открыта");
                        if(repeat==0)
                        {
                            RequireAbsorption(benchmark.Scene,absorption,allCells);gridTexture.Validate(field);
                            SavePair(view,camera,gridTexture.Texture,"hall-door-open");
                        }
                        clock.Restart();door.open=false;view.SyncDoors(doors,++revision);FinishCurrent(view,fence);report.currentDoorCloseSamples[repeat]=clock.Elapsed.TotalMilliseconds;
                        clock.Restart();gridDoors[doorIndex]=original;
                        UpdateDoorGrid(benchmark.Scene,gridDoors,doorCells,field,seedIndex,absorption,gridTexture);
                        report.gridDoorCloseSamples[repeat]=clock.Elapsed.TotalMilliseconds;
                        VerifyFull(field,referenceField,seeds,"дверь закрыта");
                    }
                    report.currentDoorOpenMs=Median(report.currentDoorOpenSamples);report.gridDoorOpenMs=Median(report.gridDoorOpenSamples);
                }
                ControlledCases();
                string previous=Path.GetFullPath("../.reference/light-grid-local-20261103/hall-grid.png");
                if(File.Exists(previous)) File.Copy(previous,Path.Combine(Output,"hall-before-color.png"),true);
                else Debug.LogWarning("Сравнение цвета: отсутствует прошлый кадр "+previous+"; показ старого варианта пропущен.");
                File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(report,true));
                WritePage(report);
                Debug.Log("Клеточный свет: PASS; сравнение и измерения: "+Output+"\n"+JsonUtility.ToJson(report,true));
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                benchmark?.Dispose();
                gridTexture?.Dispose();
                foreach(var obj in new Object[]{visual,cameraRoot,fence}) if(obj!=null) Object.DestroyImmediate(obj);
                FactoryContentSources.Clear();
            }
        }

        private static double Median(double[] values)=>values.OrderBy(v=>v).ElementAt(values.Length/2);
        private static void FinishCurrent(FactoryLevelView view,Texture2D fence)
        {
            Fence(view.Light.Bounce.Indirect,fence);
            // Complete subscription callbacks too; the next door must use the settled subscriber index.
            view.Light.Direct.WaitForMeasurements();
        }
        private static void UpdateDoorGrid(LevelLightScene scene,LevelLightScene.Door[] doors,RectInt cells,GridLightField field,
            GridLightSeeds seeds,float[] absorption,GridLightTexture texture)
        {
            scene.SetDoors(doors,null);
            var patch=GridLightTexture.ReadAbsorption(scene,Divisor,cells);
            GridLightTexture.PatchAbsorption(absorption,field.Width,cells,patch);
            var affected=field.Update(seeds.Query(field.AffectedRegion(cells)),cells);
            texture.Update(field,affected);
        }
        private static void VerifyFull(GridLightField actual,GridLightField reference,IReadOnlyList<GridLightSeed> seeds,string context)
        {
            reference.Solve(seeds);
            for(int i=0;i<actual.Pixels.Length;i++) if(!actual.Pixels[i].Equals(reference.Pixels[i]))
                throw new InvalidOperationException($"Локальная сетка, {context}: клетка {i} {actual.Pixels[i]} отличается от полного расчёта {reference.Pixels[i]}.");
            Debug.Log($"Локальная сетка: {context} совпадает с полной пересборкой; обработано {actual.RecomputedCells} из {actual.Pixels.Length} клеток.");
        }
        private static void RequireAbsorption(LevelLightScene scene,float[] patched,RectInt allCells)
        {
            var reference=GridLightTexture.ReadAbsorption(scene,Divisor,allCells);
            for(int i=0;i<reference.Length;i++) if(reference[i]!=patched[i]) throw new InvalidOperationException("Локальное поглощение отличается от полной карты в клетке "+i);
            Debug.Log("Локальный readback двери совпал с полной картой поглощения.");
        }

        private static List<GridLightSeed> Seeds(LevelLightScene scene,IReadOnlyList<LevelLightSource> sources,float[] absorption,Measurement report,Dictionary<string,GridLightSeed[]> bySource=null)
        {
            var placement=GridLightPlacement.Build(scene,sources,absorption,Divisor);
            if(bySource!=null) foreach(var pair in placement.BySource) bySource.Add(pair.Key,pair.Value);
            if(report!=null) {report.seedCells=placement.All.Count;report.unseeded=placement.Unseeded;report.maxSeedRadiance=placement.MaxRadiance;}
            return placement.All;
        }
        private static void Fence(Texture texture,Texture2D pixel)
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=(RenderTexture)texture;pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
        }
        private static void SavePair(FactoryLevelView view,Camera camera,Texture grid,string name)
        {
            view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
            Save(camera,Path.Combine(Output,name+"-current.png"));
            SaveField(view,camera,grid,view.Light.Scene,view.Light.Scene.Texel*Divisor,Path.Combine(Output,name+"-grid.png"));
        }
        internal static void SaveField(FactoryLevelView view,Camera camera,Texture field,LevelLightScene scene,float texelSize,string path)
        {
            var front=Shader.GetGlobalTexture("_RemainsRadiance");var back=Shader.GetGlobalTexture("_RemainsBackgroundRadiance");
            var oldRect=Shader.GetGlobalVector("_RemainsRadianceRect");float texel=Shader.GetGlobalFloat("_RemainsRadianceTexel");
            float enabled=Shader.GetGlobalFloat("_RemainsRadianceEnabled"),background=Shader.GetGlobalFloat("_RemainsBackgroundRadianceEnabled"),indirect=Shader.GetGlobalFloat("_RemainsIndirectEnabled");
            try
            {
                var rect=scene.WorldRect;
                Shader.SetGlobalTexture("_RemainsRadiance",field);Shader.SetGlobalTexture("_RemainsBackgroundRadiance",field);
                Shader.SetGlobalVector("_RemainsRadianceRect",new Vector4(rect.x,rect.y,rect.width,rect.height));
                Shader.SetGlobalFloat("_RemainsRadianceTexel",texelSize);
                Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsIndirectEnabled",0);
                Save(camera,path);
            }
            finally
            {
                // Present may skip an unchanged signature, so calling it again is insufficient to restore globals.
                Shader.SetGlobalTexture("_RemainsRadiance",front);Shader.SetGlobalTexture("_RemainsBackgroundRadiance",back);
                Shader.SetGlobalVector("_RemainsRadianceRect",oldRect);Shader.SetGlobalFloat("_RemainsRadianceTexel",texel);
                Shader.SetGlobalFloat("_RemainsRadianceEnabled",enabled);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",background);Shader.SetGlobalFloat("_RemainsIndirectEnabled",indirect);
            }
        }
        internal static void Save(Camera camera,string path)
        {
            var target=RenderTexture.GetTemporary(ImageWidth,ImageHeight,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var image=new Texture2D(ImageWidth,ImageHeight,TextureFormat.RGBA32,false);
            try
            {
                camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,ImageWidth,ImageHeight),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
        }
        private static void WritePage(Measurement report)
        {
            string html="<!doctype html><html lang='ru'><meta charset='utf-8'><title>Сравнение света — сохранение цвета</title><style>body{background:#20252b;color:#eee;font:17px system-ui;margin:24px}section{margin:28px 0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}img{width:100%}pre{white-space:pre-wrap}h3{margin:8px 0}</style><h1>Сетка света — затухание с сохранением цвета</h1><p>В основных парах слева текущий игровой гибрид, справа исправленная сетка. Камера, материалы и ambient одинаковые. Игровая EXE не менялась.</p><p>Сетка 20 см, яркость 0…15 с 16 подуровнями. Каждый оттенок сохраняет RGB-пропорции, затухает только его общая яркость; цвет округляется до RGB8. Группы смешиваются максимумом RGB, без бесконечного сложения. Плавная выборка, общий максимальный радиус 12 м; индивидуальные радиусы заменены общим. Теней персонажа и AO в пробе нет.</p>";
            if(File.Exists(Path.Combine(Output,"hall-before-color.png")))
                html+="<h2>Исправление синевы</h2><div class='pair'><div><h3>Предыдущая сетка</h3><img src='hall-before-color.png'></div><div><h3>Затухание с сохранением цвета</h3><img src='hall-grid.png'></div></div>";
            foreach(string name in new[]{"hall","hall-lamp-off","hall-door-open","office","door","entry"})
                html+=$"<section><h2>{name}</h2><div class='pair'><div><h3>Текущий</h3><img src='{name}-current.png'></div><div><h3>Общая сетка</h3><img src='{name}-grid.png'></div></div></section>";
            html+="<h2>Контрольные сцены</h2><p>Здесь показано само поле света на одинаковой условной белой поверхности, без окружающего света. Слева тот же игровой DirectLight + BounceLight, справа сетка. Все изображения используют одну линейную шкалу 0…1 с переводом в sRGB; превышение единицы обрезается одинаково. Цветные точки — источники, тёмные клетки — непроницаемые стены.</p>";
            foreach(string name in new[]{"corner","sealed","colored","one","many","weak-after-off"})
                html+=$"<section><h3>{name}</h3><div class='pair'><img src='test-{name}-current.png'><img src='test-{name}-grid.png'></div></section>";
            html+="<h2>Локальные события полного завода seed104</h2><p>Медиана трёх повторов в Editor; отправка команд плюс ожидание GPU, не GPU timestamps и не FPS Player. Обе схемы обновляют только область влияния. У сетки нет полного readback или полной загрузки текстуры при событии.</p><p><a href='color-check.json'>Проверка прежнего синего пятна: источник и сохранённые пропорции RGB</a>.</p><pre>"+JsonUtility.ToJson(report,true)+"</pre></html>";
            File.WriteAllText(Path.Combine(Output,"index.html"),html);
        }

        private static void ControlledCases()
        {
            const int w=100,h=60;const float size=.2f;
            foreach(string name in new[]{"corner","sealed","colored","one","many","weak-after-off"})
            {
                var alpha=new float[w*h];var walls=new float[w,h];var positions=new List<(int x,int y,Color color)>();
                for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                {
                    bool solid=x==0 || y==0 || x==w-1 || y==h-1 ||
                        (name=="corner" && x==50 && y<43) || (name=="sealed" && x==50);
                    alpha[y*w+x]=walls[x,y]=solid?1:0;
                }
                if(name=="colored") {positions.Add((25,30,Color.red));positions.Add((50,30,Color.green));positions.Add((75,30,Color.blue));}
                else if(name=="many") for(int y=20;y<=40;y+=5) for(int x=35;x<=65;x+=5) positions.Add((x,y,Color.white));
                else if(name=="weak-after-off") positions.Add((58,30,new Color(.15f,.2f,.4f)));
                else positions.Add((name=="corner" || name=="sealed"?35:50,30,Color.white));
                var sources=new List<LevelLightSource>();var seeds=new List<GridLightSeed>();
                for(int i=0;i<positions.Count;i++)
                {
                    var (x,y,color)=positions[i];var p=new Vector2((x+.5f)*size,(y+.5f)*size);
                    sources.Add(new LevelLightSource("study:"+i,LevelLightSourceKind.Lamp,new[]{new Vector4(p.x,p.y,1,0)},color,1,12,
                        new Rect(p-Vector2.one*.1f,Vector2.one*.2f),new RectInt(0,0,w,h),true));
                    seeds.Add(new GridLightSeed(y*w+x,color*(2*Mathf.Atan(.5f))));
                }
                using var scene=new LevelLightScene(walls,Vector2.zero,5,Array.Empty<LevelLightScene.ObjectSprite>());
                using var direct=new DirectLight(scene,new LevelLightSources(sources,w,h));
                direct.BuildAll();using var bounce=new BounceLight(scene,direct,.5f);bounce.BuildAll();
                var current=ReadColors(direct.Sum);var indirect=ReadColors(bounce.Indirect);
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) current[y*w+x]+=indirect[(y/2)*(w/2)+x/2];
                var field=new GridLightField(w,h,size,alpha);field.Solve(seeds);
                WriteField(current,w,h,alpha,positions,Path.Combine(Output,"test-"+name+"-current.png"));
                WriteField(field.Pixels,w,h,alpha,positions,Path.Combine(Output,"test-"+name+"-grid.png"));
                if(name=="sealed" && field.Pixels[30*w+70].maxColorComponent!=0) throw new InvalidOperationException("Контрольная стена пропустила клеточный свет.");
            }
        }
        private static Color[] ReadColors(Texture source)
        {
            var previous=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=(RenderTexture)source;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);return image.GetPixels();}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static void WriteField(Color[] field,int w,int h,float[] alpha,List<(int x,int y,Color color)> positions,string path)
        {
            var pixels=new Color[field.Length];
            for(int i=0;i<field.Length;i++) pixels[i]=alpha[i]>=.99f?new Color(.06f,.07f,.08f,1):new Color(Mathf.Clamp01(field[i].r),Mathf.Clamp01(field[i].g),Mathf.Clamp01(field[i].b),1).gamma;
            foreach(var (x,y,color) in positions) pixels[y*w+x]=color;
            var image=new Texture2D(w,h,TextureFormat.RGBA32,false);
            try {image.SetPixels(pixels);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {Object.DestroyImmediate(image);}
        }
    }
}
