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
using UnityEngine.Experimental.Rendering;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Full-factory Editor comparison. Same fine geometry, complete source shapes, cache budget and fill.
    // Nothing installs this experiment into Player or the content prefabs.
    public static class StationaryPolarStudy
    {
        private const int Width=1280,Height=672,Cycles=12;
        private const long Budget=64L*1024*1024;
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/light-polar-20261103"));
        [Serializable] private sealed class Pair
        {
            public string action;
            public int cycle,rayBuilds,polarBuilds,affectedSources;
            public bool rayFirst;
            public double rayMs,polarMs,fillMs,productionMs;
            public long fillCells;
            public RectInt polarRect;
        }
        [Serializable] private sealed class Summary
        {
            public string action;
            public int pairs,polarWins;
            public double rayMedian,rayP90,polarMedian,polarP90,fillMedian,productionMedian,pairedRatio;
        }
        [Serializable] private sealed class FullPair
        {
            public string action;
            public int cycle,polarBuilds;
            public bool productionFirst;
            public double productionMs,polarMs;
            public long fillCells;
        }
        [Serializable] private sealed class FullSummary
        {
            public string action;
            public int pairs,polarWins;
            public double productionMedian,productionP90,polarMedian,polarP90,pairedRatio;
        }
        [Serializable] private sealed class Room
        {
            public string id;
            public Rect world;
            public double[] rayFrames,polarFrames;
        }
        [Serializable] private sealed class Report
        {
            public string startedUtc,finishedUtc,device,api,method,validation,lamp,door;
            public int seed=104,sources,shapePoints,longEmitters,gc0,gc1,gc2;
            public float pitch=.05f,fill=.08f;
            public long cacheBudget=Budget,rayCacheBytes,polarCacheBytes,polarScratchBytes,sumBytes,sceneBytes,fillGpuBytes,fillCpuBytes;
            public long productionSceneBytes,productionSumBytes,productionCacheBytes,productionBounceBytes;
            public long cameraFillBytes=(long)Width*Height*16,cameraCompositeBytes=(long)Width*Height*8*5;
            public double rayBuildMs,polarBuildMs,productionBuildMs,fillBuildMs,prepareMs;
            public Pair[] events,transients;
            public Summary[] summaries;
            public FullPair[] fullEvents;
            public FullSummary[] fullSummaries;
            public Room[] rooms;
        }
        private sealed class Context : IDisposable
        {
            internal readonly LevelLight Ray,Data;
            internal readonly StationaryPolarLight Polar;
            internal readonly StationaryGridFill Fill;
            internal readonly FactoryLevelView View;
            internal readonly WorldState World;
            internal readonly LevelLightScene.Door[] RayDoors,PolarDoors;
            internal readonly List<string> Changed=new();
            internal readonly Texture2D Fence=new(1,1,TextureFormat.RGBAHalf,false,true);
            internal string Lamp,Door;
            internal int DoorIndex;
            internal int LampIndex;
            internal Context(FactoryLevelView view,WorldState world)
            {
                View=view;World=world;
                // Two independently authored scenes; no mutable Enabled records or door buffers are shared.
                Ray=view.CreateLevelLight(Budget);Data=view.CreateLevelLight(Budget);
                Fill=new StationaryGridFill(Data.Scene,Data.Sources.All);
                Data.Direct.Dispose();Data.Bounce.Dispose(); // The data wrapper retains ownership of its fine Scene.
                Polar=new StationaryPolarLight(Data.Scene,Data.Sources,Budget);
                RayDoors=Ray.Scene.Doors.Values.ToArray();PolarDoors=Data.Scene.Doors.Values.ToArray();
                Lamp=Ray.Sources.All.Where(s=>s.Enabled && s.Kind==LevelLightSourceKind.Lamp && s.Id.StartsWith("factory:0:1,1:",StringComparison.Ordinal)).OrderBy(s=>s.Id,StringComparer.Ordinal).First().Id;
                Ray.Sources.TryGet(Lamp,out LampIndex);
                Door=RayDoors.Where(d=>d.Blocks && d.Id.StartsWith("factory:0:1,1:",StringComparison.Ordinal)).OrderBy(d=>d.Id,StringComparer.Ordinal).First().Id;
                DoorIndex=Array.FindIndex(RayDoors,d=>d.Id==Door);
            }
            internal void RayEvent(int action)
            {
                if(action<2){Ray.Sources.All[LampIndex].Enabled=action==1;Ray.Direct.SourceChanged(LampIndex);}
                else
                {
                    int index=Array.FindIndex(RayDoors,d=>d.Id==Door);var d=RayDoors[index];
                    RayDoors[index]=new(d.Id,d.Bounds,d.Opacity,action==3);
                    var rectangles=Ray.Scene.SetDoors(RayDoors,Changed);Ray.Direct.OccludersChanged(Changed,rectangles);
                }
                int guard=0;while(Ray.Direct.HasWork){Ray.Direct.Step(64);if(++guard>100000)throw new InvalidOperationException("Прямой эталон не завершил событие.");}
                Ray.Direct.WaitForMeasurements();
            }
            internal void PolarEvent(int action)
            {
                if(action<2)Polar.SetLamp(Lamp,action==1);
                else
                {
                    int index=Array.FindIndex(PolarDoors,d=>d.Id==Door);var d=PolarDoors[index];
                    PolarDoors[index]=new(d.Id,d.Bounds,d.Opacity,action==3);
                    var rectangles=Data.Scene.SetDoors(PolarDoors,Changed);Polar.OccludersChanged(Changed,rectangles);
                }
            }
            internal void FillEvent(int action)
            {if(action<2)Fill.SetLamp(Lamp,action==1);else Fill.SetDoor(Door,action==3);}
            internal void ProductionEvent(int action)
            {
                var light=View.Light;
                if(action<2)
                {
                    if(!light.Sources.TryGet(Lamp,out int index))throw new InvalidOperationException("В production нет лампы "+Lamp);
                    light.Sources.All[index].Enabled=action==1;light.Direct.SourceChanged(index);
                }
                else
                {
                    var doors=light.Scene.Doors.Values.ToArray();int index=Array.FindIndex(doors,d=>d.Id==Door);var d=doors[index];
                    doors[index]=new(d.Id,d.Bounds,d.Opacity,action==3);light.SetDoors(doors);
                }
                int guard=0;while(light.HasWork){light.Step(64,64);if(++guard>100000)throw new InvalidOperationException("Production не завершил событие.");}
                light.Direct.WaitForMeasurements();
                View.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
            }
            internal void Wait(Texture texture)
            {
                var old=RenderTexture.active;
                try{RenderTexture.active=(RenderTexture)texture;Fence.ReadPixels(new Rect(0,0,1,1),0,0,false);}
                finally{RenderTexture.active=old;}
            }
            public void Dispose(){Polar?.Dispose();Fill?.Dispose();Ray?.Dispose();Data?.Dispose();Object.DestroyImmediate(Fence);}
        }
        public static void RunBatch()
        {
            GameObject visual=null,cameraObject=null;Context c=null;RenderTexture target=null;Texture2D image=null;
            try
            {
                Directory.CreateDirectory(Output);bool smoke=Environment.GetCommandLineArgs().Contains("-polar-stationary-smoke");
                Debug.Log(StationaryPolarChecks.Run());
                var report=new Report{startedUtc=DateTime.UtcNow.ToString("O"),device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                    method="Editor seed104, все постоянные источники, исходная fine-геометрия10/м и все1–8точек формы. Только direct-алгоритм меняется: луч на каждый texel против полярных префиксов5см/локального поля10см. Цвет, atan(width*weight), дальность и старый спад последних10% одинаковы. Кэши обоих direct64МиБ. Заполнение0,08 одинаковое; fillMs отдельно включает локальную сетку, реконструкцию изменённой области окна и композит. Это время НЕ включено в events.rayMs/polarMs. Отдельная fullEvents-серия измеряет событие целиком: production Direct+Bounce+Present против polar+grid+локальная реконструкция+композит, с чередованием порядка и единым финальным fence каждого варианта. ProductionMs — дополнительный независимый штатный Direct256МиБ+Bounce, другая модель заполнения.12пар, порядок ray/polar чередуется; production также чередуется по месту в цикле, fill следует после изменения fine-геометрии.2прогрева. Wall-clock с GPU completion1pixel, не GPU timestamps и не FPS Player. Cached-frame — камера с заранее готовым полем, свет не пересчитывается; изображения/генерация/валидация вне замеров. Для вспышек/движения одинаковые1точечные источники без отражений и без обновления fill. Внешнюю нагрузку не создаём/не меняем. Отчёт хранит все выборки; память — payload полей/кэша/scratch, не общий процесс. Runtime и Player не переключены."};
                Anatomy.Load();WeaponCatalog.Load();var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");var authoring=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));var layout=authoring.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",layout,world);var view=visual.GetComponent<FactoryLevelView>();view.ConfigureLegacyLightingForChecks();var factory=visual.GetComponent<FactoryLevelRoot>();view.Synchronize(layout,world,1);
                var clock=Stopwatch.StartNew();c=new Context(view,world);report.prepareMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();c.Fill.BuildAll();c.Wait(c.Fill.Texture);report.fillBuildMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();c.Ray.Direct.BuildAll();c.Ray.Direct.WaitForMeasurements();c.Wait(c.Ray.Direct.Sum);report.rayBuildMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();c.Polar.BuildAll();c.Wait(c.Polar.Sum);report.polarBuildMs=clock.Elapsed.TotalMilliseconds;
                Debug.Log($"Стационарный полярный свет: построено {c.Polar.Sources.Count} источников, ray={report.rayBuildMs:F1}мс, polar={report.polarBuildMs:F1}мс.");
                clock.Restart();view.Focus(new Vector2(layout.spawnX,layout.spawnY));report.productionBuildMs=clock.Elapsed.TotalMilliseconds;
                report.sources=c.Polar.Sources.Count;report.shapePoints=c.Polar.Sources.All.Sum(s=>s.Samples.Length);
                report.longEmitters=c.Polar.Sources.All.Count(s=>Math.Max(s.Bounds.width,s.Bounds.height)>3*Math.Max(.001f,Math.Min(s.Bounds.width,s.Bounds.height)));
                report.lamp=c.Lamp;report.door=c.Door;report.sumBytes=c.Polar.SumBytes;report.sceneBytes=c.Data.Scene.Bytes;
                cameraObject=new GameObject("Editor: сравнение стационарного полярного света");var camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.aspect=(float)Width/Height;camera.allowHDR=false;camera.allowMSAA=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;image=new Texture2D(Width,Height,TextureFormat.RGBA32,false);
                var rooms=new List<Room>();float cell=(float)factory.Prepared.Grid.Level.Cell;
                foreach(var (x,y,id) in new[]{(1,1,"hall"),(1,9,"office"),(2,11,"door"),(0,0,"entry")})
                {
                    if(smoke && rooms.Count>0)break;
                    var p=factory.Prepared.Grid.Rooms.Single(r=>r.Cell.X==x && r.Cell.Y==y);
                    var room=new Rect(p.Column*cell,(factory.Prepared.Grid.Level.Height-p.Row-p.Template.Height)*cell,p.Template.Width*cell,p.Template.Height*cell);
                    var area=CameraRect(room);view.Focus(area.center);view.SetViewArea(area);camera.transform.position=new(area.center.x,area.center.y,-10);camera.orthographicSize=area.height*.5f;
                    using var fill=new GridFillReconstruction(Width,Height);BuildFill(c,fill,area);fill.ReadDiagnostics("stationary:"+id);
                    using var composite=new FactoryWispComposite(area,Width,Height);
                    // Complete the first render's deferred artwork/atlas uploads before either image/timing.
                    Draw(composite,c.Ray.Direct.Sum,c.Ray.Scene.WorldRect,fill.Texture);camera.Render();camera.Render();c.Wait(target);
                    Draw(composite,c.Ray.Direct.Sum,c.Ray.Scene.WorldRect,fill.Texture);Capture(camera,target,image,"stationary-"+id+"-ray.png");
                    Draw(composite,c.Polar.Sum,c.Data.Scene.WorldRect,fill.Texture);Capture(camera,target,image,"stationary-"+id+"-polar.png");
                    // The actual shipped renderer is a third visual reference, not the equal-fill algorithm pair.
                    PublishProduction(view.Light);
                    Capture(camera,target,image,"stationary-"+id+"-production.png");
                    var result=new Room{id=id,world=area};MeasureFrames(c,camera,target,composite,fill,result,smoke?2:Cycles);rooms.Add(result);
                }
                report.rooms=rooms.ToArray();
                var eventArea=rooms[0].world;view.SetViewArea(eventArea);
                using var eventFill=new GridFillReconstruction(Width,Height);BuildFill(c,eventFill,eventArea);
                using var eventComposite=new FactoryWispComposite(eventArea,Width,Height);
                var events=new List<Pair>();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
                for(int cycle=-2;cycle<(smoke?1:Cycles);cycle++)for(int action=0;action<4;action++)
                {
                    var pair=new Pair{cycle=cycle,action=new[]{"lamp-off","lamp-on","door-open","door-close"}[action],rayFirst=(cycle&1)==0};
                    if(!pair.rayFirst)pair.productionMs=Time(c,c.View.Light.Direct.Sum,()=>c.ProductionEvent(action));
                    if(pair.rayFirst){pair.rayMs=RayTime();pair.polarMs=PolarTime();}else{pair.polarMs=PolarTime();pair.rayMs=RayTime();}
                    if(pair.rayFirst)pair.productionMs=Time(c,c.View.Light.Direct.Sum,()=>c.ProductionEvent(action));
                    // Reconstruction must see the fine geometry of THIS event, never the preceding door state.
                    pair.fillMs=FillTime();
                    pair.fillCells=c.Fill.LastGridCells;pair.polarRect=c.Polar.LastComposedRect;pair.affectedSources=c.Polar.LastAffectedSources;
                    if(cycle>=0)events.Add(pair);
                    double RayTime(){int b=c.Ray.Direct.Builds;double ms=Time(c,c.Ray.Direct.Sum,()=>c.RayEvent(action));pair.rayBuilds=c.Ray.Direct.Builds-b;return ms;}
                    double PolarTime(){int b=c.Polar.Builds;double ms=Time(c,c.Polar.Sum,()=>c.PolarEvent(action));pair.polarBuilds=c.Polar.Builds-b;return ms;}
                    double FillTime()=>Time(c,eventComposite.Front,()=>{c.FillEvent(action);ReconstructDirty(c,eventFill,eventArea);Draw(eventComposite,c.Polar.Sum,c.Data.Scene.WorldRect,eventFill.Texture);});
                }
                report.events=events.ToArray();Debug.Log("Стационарный свет: парные события завершены.");
                var fullEvents=new List<FullPair>();
                for(int cycle=-2;cycle<(smoke?1:Cycles);cycle++)for(int action=0;action<4;action++)
                {
                    var pair=new FullPair{cycle=cycle,action=new[]{"lamp-off","lamp-on","door-open","door-close"}[action],productionFirst=(cycle&1)==0};
                    if(pair.productionFirst){pair.productionMs=Production();pair.polarMs=Polar();}else{pair.polarMs=Polar();pair.productionMs=Production();}
                    pair.polarBuilds=c.Polar.LastBuilds;pair.fillCells=c.Fill.LastGridCells;if(cycle>=0)fullEvents.Add(pair);
                    double Production()=>Time(c,c.View.Light.Direct.Sum,()=>c.ProductionEvent(action));
                    double Polar()=>Time(c,eventComposite.Front,()=>{c.PolarEvent(action);c.FillEvent(action);ReconstructDirty(c,eventFill,eventArea);Draw(eventComposite,c.Polar.Sum,c.Data.Scene.WorldRect,eventFill.Texture);});
                }
                report.fullEvents=fullEvents.ToArray();
                report.fullSummaries=report.fullEvents.GroupBy(p=>p.action).Select(g=>new FullSummary{action=g.Key,pairs=g.Count(),polarWins=g.Count(p=>p.polarMs<p.productionMs),productionMedian=Q(g.Select(p=>p.productionMs),.5),productionP90=Q(g.Select(p=>p.productionMs),.9),polarMedian=Q(g.Select(p=>p.polarMs),.5),polarP90=Q(g.Select(p=>p.polarMs),.9),pairedRatio=Q(g.Select(p=>p.productionMs/p.polarMs),.5)}).ToArray();
                report.transients=MeasureTransients(c,rooms[0].world,smoke?2:Cycles);
                report.gc0=GC.CollectionCount(0)-g0;report.gc1=GC.CollectionCount(1)-g1;report.gc2=GC.CollectionCount(2)-g2;
                report.rayCacheBytes=c.Ray.Direct.CacheBytes;report.polarCacheBytes=c.Polar.CacheBytes;report.polarScratchBytes=c.Polar.ScratchBytes;
                report.fillGpuBytes=c.Fill.GpuBytes;report.fillCpuBytes=c.Fill.CpuBytes;
                report.productionSceneBytes=view.Light.Scene.Bytes;report.productionSumBytes=view.Light.Direct.SumBytes;
                report.productionCacheBytes=view.Light.Direct.CacheBytes;report.productionBounceBytes=view.Light.Bounce.Bytes;
                report.summaries=report.events.Concat(report.transients).GroupBy(p=>p.action).Select(g=>Summarize(g.Key,g.ToArray())).ToArray();
                File.WriteAllText(Path.Combine(Output,smoke?"stationary-smoke-unvalidated.json":"stationary-unvalidated.json"),JsonUtility.ToJson(report,true));
                report.validation=Validate(c,rooms[0].world,smoke);
                report.finishedUtc=DateTime.UtcNow.ToString("O");
                File.WriteAllText(Path.Combine(Output,smoke?"stationary-smoke.json":"stationary-report.json"),JsonUtility.ToJson(report,true));
                File.Copy(Path.Combine(Application.dataPath,"Remains/Editor/LightGridStudy/StationaryPolarPreview.html.txt"),Path.Combine(Output,"stationary.html"),true);
                Debug.Log("Стационарный полярный свет: PASS. "+report.validation);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally{c?.Dispose();foreach(var o in new Object[]{target,image,cameraObject,visual})if(o!=null)Object.DestroyImmediate(o);FactoryContentSources.Clear();}
        }
        private static void BuildFill(Context c,GridFillReconstruction fill,Rect area)
            =>fill.Build(c.Fill.Texture,c.Fill.Scene.Texture,c.Fill.Scene.WorldRect,c.Data.Scene.Texture,c.Data.Scene.WorldRect,area);
        private static void ReconstructDirty(Context c,GridFillReconstruction fill,Rect area)
        {
            var coarse=c.Fill.LastChangedRect;float t=c.Fill.Scene.Texel;
            Rect dirty=new(c.Fill.Scene.WorldRect.x+coarse.x*t-.6f,c.Fill.Scene.WorldRect.y+coarse.y*t-.6f,coarse.width*t+1.2f,coarse.height*t+1.2f);
            fill.BuildRegion(c.Fill.Texture,c.Fill.Scene.Texture,c.Fill.Scene.WorldRect,c.Data.Scene.Texture,c.Data.Scene.WorldRect,area,OutputClip(dirty,area));
        }
        private static void Draw(FactoryWispComposite composite,Texture direct,Rect rect,Texture fill)
        {composite.Begin(direct,rect,fill,1);composite.Resolve();composite.Publish();}
        private static void PublishProduction(LevelLight light)
        {
            var r=light.Scene.WorldRect;
            Shader.SetGlobalTexture("_RemainsRadiance",light.Direct.Sum);Shader.SetGlobalTexture("_RemainsBackgroundRadiance",light.Direct.Sum);
            Shader.SetGlobalVector("_RemainsRadianceRect",new Vector4(r.x,r.y,r.width,r.height));Shader.SetGlobalFloat("_RemainsRadianceTexel",light.Scene.Texel);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",1);
            Shader.SetGlobalTexture("_RemainsIndirect",light.Bounce.Indirect);
            Shader.SetGlobalVector("_RemainsIndirectRect",new Vector4(r.x,r.y,light.Bounce.Width*BounceLight.Divisor*light.Scene.Texel,light.Bounce.Height*BounceLight.Divisor*light.Scene.Texel));
            Shader.SetGlobalFloat("_RemainsIndirectEnabled",1);
        }
        private static double Time(Context c,Texture target,Action action)
        {c.Wait(target);var clock=Stopwatch.StartNew();action();c.Wait(target);return clock.Elapsed.TotalMilliseconds;}
        private static Rect CameraRect(Rect r)
        {float h=Mathf.Max(r.height+.6f,(r.width+.6f)*Height/Width);return new Rect(r.center-new Vector2(h*Width/Height,h)*.5f,new Vector2(h*Width/Height,h));}
        private static void Capture(Camera camera,RenderTexture target,Texture2D image,string name)
        {var previous=RenderTexture.active;try{camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Width,Height),0,0,false);File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());}finally{RenderTexture.active=previous;}}
        private static void MeasureFrames(Context c,Camera camera,RenderTexture target,FactoryWispComposite composite,GridFillReconstruction fill,Room room,int count)
        {
            var ray=new List<double>();var polar=new List<double>();
            for(int cycle=-2;cycle<count;cycle++)foreach(bool isPolar in (cycle&1)==0?new[]{false,true}:new[]{true,false})
            {
                // Compose/publish outside the camera timing: stationary cached frames do not rebuild lighting.
                Draw(composite,isPolar?c.Polar.Sum:c.Ray.Direct.Sum,c.Data.Scene.WorldRect,fill.Texture);c.Wait(composite.Front);
                double value=Time(c,target,camera.Render);if(cycle>=0)(isPolar?polar:ray).Add(value);
            }
            room.rayFrames=ray.ToArray();room.polarFrames=polar.ToArray();
        }
        private static Pair[] MeasureTransients(Context c,Rect area,int count)
        {
            var pixels=RasterPolarWisp.ReadFloatsForValidation(c.Data.Scene.Texture);var points=new List<Vector2>();
            for(int y=0;y<20;y++)for(int x=0;x<20;x++)
            {
                var p=new Vector2(area.x+(x+.5f)*area.width/20,area.y+(y+.5f)*area.height/20);
                if(!c.Data.Scene.WorldRect.Contains(p))continue;
                int ix=Mathf.FloorToInt((p.x-c.Data.Scene.WorldRect.x)*c.Data.Scene.Resolution),iy=Mathf.FloorToInt((p.y-c.Data.Scene.WorldRect.y)*c.Data.Scene.Resolution);
                if(pixels[iy*c.Data.Scene.Width+ix]<.001f)points.Add(p);
            }
            if(points.Count<2)throw new InvalidOperationException("Для временных источников не найдено свободных позиций.");
            int size=Mathf.CeilToInt(2*8*c.Data.Scene.Resolution)+8;var target=new RenderTexture(size,size,0,GraphicsFormat.R16G16B16A16_SFloat){enableRandomWrite=true};target.Create();
            var result=new List<Pair>();
            try
            {
                foreach(bool moving in new[]{false,true})for(int i=-2;i<count;i++)
                {
                    var pos=points[moving?(i+2)*7%points.Count:points.Count/2];float reach=moving?8:4;
                    var rect=c.Data.Scene.Texels(new Rect(pos-Vector2.one*reach,Vector2.one*reach*2));
                    var source=new LevelLightSource("bench-transient",LevelLightSourceKind.Lamp,new[]{new Vector4(pos.x,pos.y,1,0)},new Color(12,8,4),.2f,reach,new Rect(pos-Vector2.one*.01f,Vector2.one*.02f),rect,true);
                    var pair=new Pair{cycle=i,action=moving?"moving-light":"muzzle-flash",rayFirst=(i&1)==0};
                    if(pair.rayFirst){pair.rayMs=Time(c,target,()=>c.Ray.Direct.BuildTransient(source,target));pair.polarMs=Time(c,target,()=>c.Polar.BuildTransient(source,target));}
                    else{pair.polarMs=Time(c,target,()=>c.Polar.BuildTransient(source,target));pair.rayMs=Time(c,target,()=>c.Ray.Direct.BuildTransient(source,target));}
                    if(i>=0)result.Add(pair);
                }
            }
            finally{Object.DestroyImmediate(target);}
            return result.ToArray();
        }
        private static string Validate(Context c,Rect area,bool smoke)
        {
            var absorption=RasterPolarWisp.ReadFloatsForValidation(c.Data.Scene.Texture);
            var longest=c.Polar.Sources.All.Where(s=>s.Enabled).OrderByDescending(s=>Math.Max(s.Bounds.width,s.Bounds.height)).First();
            var map=c.Polar.ValidateMapAgainstCpu(longest.Id,absorption,53,.006f);
            Debug.Log("Полярная карта длинного источника: "+longest.Id+", точек="+longest.Samples.Length+", проверок="+map.Samples);
            using var incremental=new GridFillReconstruction(Width,Height);using var full=new GridFillReconstruction(Width,Height);BuildFill(c,incremental,area);
            var gridReference=new GridLightField(c.Fill.Field.Width,c.Fill.Field.Height,c.Fill.Field.CellSize,c.Fill.Absorption);
            for(int action=0;action<4;action++)
            {
                c.RayEvent(action);c.PolarEvent(action);c.FillEvent(action);
                gridReference.Solve(c.Fill.SeedIndex.Query(new RectInt(0,0,c.Fill.Field.Width,c.Fill.Field.Height)));
                for(int i=0;i<gridReference.Pixels.Length;i++)if(!gridReference.Pixels[i].Equals(c.Fill.Field.Pixels[i]))throw new InvalidOperationException("Локальное заполнение отличается от полного, событие="+action+", клетка="+i);
                ReconstructDirty(c,incremental,area);
                BuildFill(c,full,area);Compare(PolarShadowField.Read(incremental.Texture),PolarShadowField.Read(full.Texture),0,"Локальная реконструкция "+action);
                // Validate an OPEN door as well as the restored baseline; do not hide a stale event by closing it.
                if(action>=2)
                {
                    var local=PolarShadowField.Read((RenderTexture)c.Polar.Sum);c.Polar.BuildAll();
                    Compare(local,PolarShadowField.Read((RenderTexture)c.Polar.Sum),0,"Локальная полярная сумма "+action);
                }
            }
            return $"PASS: CPU/GPU-префиксы длинного источника ({map.Samples} проб, {longest.Samples.Length} точек); локальное grid/fill против полного после4событий; открытая и закрытая дверь: локальная полярная сумма побитно равна новой полной. Неподвижная картинка и физическая мягкость требуют визуальной оценки. Разрушение стен/Player/анимированные тени не проверялись.";
        }
        private static RectInt OutputClip(Rect dirty,Rect world)
        {
            int x0=Mathf.Clamp(Mathf.FloorToInt((dirty.xMin-world.x)*Width/world.width),0,Width),x1=Mathf.Clamp(Mathf.CeilToInt((dirty.xMax-world.x)*Width/world.width),0,Width);
            int y0=Mathf.Clamp(Mathf.FloorToInt((dirty.yMin-world.y)*Height/world.height),0,Height),y1=Mathf.Clamp(Mathf.CeilToInt((dirty.yMax-world.y)*Height/world.height),0,Height);return new RectInt(x0,y0,x1-x0,y1-y0);
        }
        private static void Compare(Color[] a,Color[] b,float tolerance,string label)
        {
            if(a.Length!=b.Length)throw new InvalidOperationException(label+": размеры отличаются.");
            for(int i=0;i<a.Length;i++)for(int channel=0;channel<4;channel++)if(!float.IsFinite(a[i][channel]) || !float.IsFinite(b[i][channel]) || Mathf.Abs(a[i][channel]-b[i][channel])>tolerance)
                throw new InvalidOperationException($"{label}: пиксель={i}, канал={channel}, local={a[i]}, full={b[i]}, допуск={tolerance}.");
        }
        private static double Q(IEnumerable<double> values,double q)
        {var sorted=values.OrderBy(x=>x).ToArray();return sorted[(int)Math.Ceiling(q*sorted.Length)-1];}
        private static Summary Summarize(string action,Pair[] pairs)=>new(){action=action,pairs=pairs.Length,polarWins=pairs.Count(p=>p.polarMs<p.rayMs),rayMedian=Q(pairs.Select(p=>p.rayMs),.5),rayP90=Q(pairs.Select(p=>p.rayMs),.9),polarMedian=Q(pairs.Select(p=>p.polarMs),.5),polarP90=Q(pairs.Select(p=>p.polarMs),.9),fillMedian=Q(pairs.Select(p=>p.fillMs),.5),productionMedian=Q(pairs.Select(p=>p.productionMs),.5),pairedRatio=Q(pairs.Select(p=>p.rayMs/p.polarMs),.5)};
    }
}
