using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Editor-only comparison harness. The passed hybrid remains owned by its caller.
    public static class HybridLightStudy
    {
        private const int ImageWidth=1280,ImageHeight=672,WarmupCycles=1,MeasuredCycles=6;
        private const long CacheBudget=64L*1024*1024;
        private static readonly string[] Events={"lamp-off","lamp-on","door-open","door-close"};
        [Serializable] private sealed class Sample
        {
            public double totalMs;
            public long gridCells;
            public int directBuilds,gc0,gc1,gc2;
            public RectInt composedRect;
        }
        [Serializable] private sealed class Pair
        {
            public int cycle;
            public string action;
            public bool currentFirst;
            public Sample current,hybrid;
            public double currentOverHybrid;
        }
        [Serializable] private sealed class Summary
        {
            public string action;
            public int pairs,hybridWins,currentFirstPairs,hybridFirstPairs;
            public double currentMedianMs,currentP90Ms,hybridMedianMs,hybridP90Ms,medianPairedCurrentOverHybrid;
            public double currentFirstMedianRatio,hybridFirstMedianRatio;
        }
        [Serializable] private sealed class Report
        {
            public string startedUtc,finishedUtc,device,api,lamp,door,method,validation;
            public int seed,currentWidth,currentHeight,hybridWidth,hybridHeight,sources,enabledSources;
            public int warmupCycles=WarmupCycles,measuredCycles=MeasuredCycles,measuredPairs=MeasuredCycles*4,measuredOperations=MeasuredCycles*8;
            public int measuredGc0,measuredGc1,measuredGc2;
            public float hybridTexel,fillGain;
            public double hybridPrepareMs,hybridInitialBuildMs,currentInitialFocusBuildMs,validationMs;
            public long currentSceneGpuBytes,currentDirectGpuBytes,currentCacheGpuBytes,currentBounceGpuBytes,currentTotalGpuBytes;
            public long hybridGpuBytes,hybridCpuBytes;
            public Pair[] warmup,pairs;
            public Summary[] summaries;
        }
        private sealed class Context
        {
            public FactoryLevelView View;
            public GridDirectHybrid Hybrid;
            public WorldState World;
            public DoorState[] Doors;
            public DoorState Door;
            public string Lamp;
            public string[] BaselineDark,Broken,OffDark;
            public Texture2D Fence;
            public bool NeedsRestoration;
            public long LampRevision,DoorRevision;
        }

        public static void Run(FactoryLevelView view,FactoryLevelRoot factory,WorldState world,RoomLayout room,
            GridDirectHybrid hybrid,double prepareMs,string output)
        {
            if(view==null || factory==null || world==null || room==null || hybrid==null)
                throw new ArgumentException("Сравнение гибридного света: отсутствуют подготовленные данные.");
            if(Application.isPlaying) throw new InvalidOperationException("Сравнение гибридного света запускается вне Play Mode.");
            if(view.Light!=null) throw new InvalidOperationException("Сравнение гибридного света: production-свет уже построен; начальное время будет недостоверным.");
            Directory.CreateDirectory(output);
            var report=new Report{startedUtc=DateTime.UtcNow.ToString("O"),device=SystemInfo.graphicsDeviceName,
                api=SystemInfo.graphicsDeviceType.ToString(),seed=world.seed,hybridPrepareMs=prepareMs,
                hybridWidth=hybrid.Scene.Width,hybridHeight=hybrid.Scene.Height,hybridTexel=hybrid.Scene.Texel,fillGain=.08f,
                method="Editor batch, полный завод seed104; внешняя нагрузка ПК не создаётся и не меняется. Один прогревочный и6 измеряемых циклов: выключить/включить лампу, открыть/закрыть дверь;24 пары,48 операций. Порядок вариантов чередуется по циклам. Каждая операция завершается GPU fence и callbacks DirectLight; PNG и полные эталоны между измерениями отсутствуют. Время wall-clock включает ожидание GPU, это не GPU timestamps, FPS или обычный кадр Player. Текущий вариант: штатные Direct+Bounce. Новый: направленный Direct на геометрии20см, до4 точек источника, кэш64МиБ, мягкое ограничение яркости и max(direct,0.08*grid), без Bounce. Простая сетка на кадрах — то же поле без направленного Direct. Размеры и модель различаются намеренно. Нет персонажа и проверки его теней. Память: перечисленные массивы/текстуры, без CLR/driver overhead, artwork, общих структур уровня, буферов камеры и проверочных эталонов. CPU-структуры production не измерены. Разовый currentInitialFocusBuildMs включает Focus с подготовкой production-света/окна изображения; hybridPrepareMs относится к независимой подготовке нового варианта после общей сборки уровня. Это одиночные начальные измерения, не распределение загрузки."};
            var cameraRoot=new GameObject("Камера сравнения направленного и клеточного света");
            var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;
            camera.aspect=(float)ImageWidth/ImageHeight;camera.allowHDR=false;camera.allowMSAA=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.07f,.08f);
            var fence=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            Context context=null;Exception failure=null;
            try
            {
                var clock=Stopwatch.StartNew();hybrid.BuildAll();Fence(hybrid.Texture,fence);hybrid.Direct.WaitForMeasurements();
                report.hybridInitialBuildMs=clock.Elapsed.TotalMilliseconds;
                var grid=factory.Prepared.Grid;float cell=(float)grid.Level.Cell;Rect hallBounds=default;
                foreach(var (x,y,name) in new[]{(1,1,"hall"),(1,9,"office"),(2,11,"door"),(0,0,"entry")})
                {
                    var placement=grid.Rooms.Single(r=>r.Cell.X==x && r.Cell.Y==y);
                    var bounds=new Rect(placement.Column*cell,(grid.Level.Height-placement.Row-placement.Template.Height)*cell,
                        placement.Template.Width*cell,placement.Template.Height*cell);
                    SetCameraArea(view,camera,bounds);
                    clock.Restart();view.Focus(bounds.center);FinishCurrent(view,fence);
                    if(name=="hall") {report.currentInitialFocusBuildMs=clock.Elapsed.TotalMilliseconds;hallBounds=bounds;}
                    // First Focus creates the view and replaces its area with DefaultView. Restore the actual
                    // camera crop before Present; otherwise only the first production image has a wrong field.
                    SetCameraArea(view,camera,bounds);
                    SaveTriple(view,camera,hybrid,output,name);
                    SaveFillVariants(view,camera,hybrid,output,name);
                    if(name=="hall")
                    {
                        hybrid.Compose(0);LightGridStudy.SaveField(view,camera,hybrid.Texture,hybrid.Scene,hybrid.Scene.Texel,Path.Combine(output,"hall-hybrid-fill0.png"));
                        hybrid.Compose();
                    }
                }
                context=CreateContext(view,hybrid,world,fence);
                SetCameraArea(view,camera,hallBounds);view.Focus(hallBounds.center);FinishCurrent(view,fence);SetCameraArea(view,camera,hallBounds);
                context.NeedsRestoration=true;
                view.SyncLamps(context.OffDark,++context.LampRevision,context.Broken);FinishCurrent(view,fence);hybrid.SetLamp(context.Lamp,false);
                SaveTriple(view,camera,hybrid,output,"hall-lamp-off");SaveFillVariants(view,camera,hybrid,output,"hall-lamp-off");
                Restore(context);
                context.Door.open=true;view.SyncDoors(context.Doors,++context.DoorRevision);FinishCurrent(view,fence);hybrid.SetDoor(context.Door.id,false);
                SaveTriple(view,camera,hybrid,output,"hall-door-open");SaveFillVariants(view,camera,hybrid,output,"hall-door-open");
                Restore(context);context.NeedsRestoration=false;
                if(Environment.GetCommandLineArgs().Contains("-grid-hybrid-fill-preview"))
                {
                    string previousReport=Path.Combine(output,"report.json");
                    if(!File.Exists(previousReport)) throw new FileNotFoundException("Для обновления кадров нужен предыдущий отчёт гибрида.",previousReport);
                    var measured=JsonUtility.FromJson<Report>(File.ReadAllText(previousReport));
                    WritePage(measured,output);
                    Debug.Log("Сравнения заполнения: PASS; шесть мест/состояний, текущий свет и0.08/0.16. Старые измерения сохранены, бенчмарк не повторялся.");
                    return;
                }
                report.lamp=context.Lamp;report.door=context.Door.id;
                report.currentWidth=view.Light.Scene.Width;report.currentHeight=view.Light.Scene.Height;
                report.sources=hybrid.Sources.Count;report.enabledSources=hybrid.Sources.All.Count(s=>s.Enabled);
                report.warmup=new Pair[WarmupCycles*4];report.pairs=new Pair[MeasuredCycles*4];
                for(int cycle=0;cycle<WarmupCycles;cycle++) RunCycle(context,cycle,report.warmup,cycle*4);
                int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
                for(int cycle=0;cycle<MeasuredCycles;cycle++) RunCycle(context,cycle,report.pairs,cycle*4);
                report.measuredGc0=GC.CollectionCount(0)-gc0;report.measuredGc1=GC.CollectionCount(1)-gc1;report.measuredGc2=GC.CollectionCount(2)-gc2;
                report.currentSceneGpuBytes=view.Light.Scene.Bytes;report.currentDirectGpuBytes=view.Light.Direct.SumBytes;
                report.currentCacheGpuBytes=view.Light.Direct.CacheBytes;report.currentBounceGpuBytes=view.Light.Bounce.Bytes;
                report.currentTotalGpuBytes=report.currentSceneGpuBytes+report.currentDirectGpuBytes+report.currentCacheGpuBytes+report.currentBounceGpuBytes;
                report.hybridGpuBytes=hybrid.GpuBytes;report.hybridCpuBytes=hybrid.CpuBytes;
                clock.Restart();Validate(context);report.validationMs=clock.Elapsed.TotalMilliseconds;context.NeedsRestoration=false;
                report.validation="PASS: исходные состояния лампы и двери восстановлены в обеих схемах; поглощение совпадает с полным чтением сцены; локальная сетка равна полной; GPU-сетка равна CPU в RGBAHalf; локальная сумма Direct побитно равна новой полной сборке Direct с теми же coarse-геометрией и источниками. Проверки выполнены после измерений, их стоимость исключена из парных результатов.";
                report.summaries=Events.Select(action=>Summarize(action,report.pairs.Where(p=>p.action==action).ToArray())).ToArray();
                report.finishedUtc=DateTime.UtcNow.ToString("O");
                File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));WritePage(report,output);
                Debug.Log("Направленный свет с клеточным заполнением: PASS;24 пары,48 операций; результаты "+output);
            }
            catch(Exception error) {failure=error;throw;}
            finally
            {
                try {if(context!=null && context.NeedsRestoration) Restore(context);}
                catch(Exception restoreError)
                {
                    if(failure==null) throw;
                    Debug.LogError($"Сравнение гибридного света: не удалось восстановить лампу {context.Lamp} и дверь {context.Door.id}: {restoreError}");
                }
                finally {Object.DestroyImmediate(cameraRoot);Object.DestroyImmediate(fence);}
            }
        }

        private static Context CreateContext(FactoryLevelView view,GridDirectHybrid hybrid,WorldState world,Texture2D fence)
        {
            if(view.Light.Sources.Count!=hybrid.Sources.Count || view.Light.Sources.All.Select(s=>s.Id).Except(hybrid.Sources.All.Select(s=>s.Id),StringComparer.Ordinal).Any())
                throw new InvalidOperationException("Сравнение гибридного света: каталоги источников имеют разные ID.");
            var lamp=view.Light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && s.Enabled && s.Id.StartsWith("factory:0:1,1:",StringComparison.Ordinal))
                .OrderBy(s=>s.Id,StringComparer.Ordinal).FirstOrDefault();
            if(lamp==null) throw new InvalidOperationException("Сравнение гибридного света: в зале нет включённой лампы.");
            if(!hybrid.Sources.TryGet(lamp.Id,out int index) || !hybrid.Sources.All[index].Enabled)
                throw new InvalidOperationException("Сравнение гибридного света: источники не согласованы, лампа "+lamp.Id);
            var doors=world.doors.Where(d=>d.level=="factory:0").ToArray();
            var door=doors.Where(d=>d.Blocks && d.id.StartsWith("factory:0:1,1:",StringComparison.Ordinal)).OrderBy(d=>d.id,StringComparer.Ordinal).FirstOrDefault();
            if(door==null || !hybrid.Scene.Doors.TryGetValue(door.id,out var other) || !other.Blocks)
                throw new InvalidOperationException("Сравнение гибридного света: в зале отсутствует одинаковая закрытая дверь.");
            var dark=view.Light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && !s.Enabled).Select(s=>s.Id).ToArray();
            var broken=world.lamps.Where(l=>l.level=="factory:0" && l.broken).Select(l=>l.id).ToArray();
            if(broken.Except(dark,StringComparer.Ordinal).Any()) throw new InvalidOperationException("Разбитые лампы отсутствуют в исходном списке погашенных.");
            return new Context{View=view,Hybrid=hybrid,World=world,Doors=doors,Door=door,Lamp=lamp.Id,BaselineDark=dark,Broken=broken,
                OffDark=dark.Concat(new[]{lamp.Id}).ToArray(),Fence=fence,LampRevision=Math.Max(world.lampRevision,view.AppliedLampRevision),DoorRevision=view.AppliedDoorRevision};
        }
        private static void SaveFillVariants(FactoryLevelView view,Camera camera,GridDirectHybrid hybrid,string output,string name)
        {
            hybrid.Compose(.08f);LightGridStudy.SaveField(view,camera,hybrid.Texture,hybrid.Scene,hybrid.Scene.Texel,Path.Combine(output,name+"-hybrid-fill08.png"));
            hybrid.Compose(.16f);LightGridStudy.SaveField(view,camera,hybrid.Texture,hybrid.Scene,hybrid.Scene.Texel,Path.Combine(output,name+"-hybrid-fill16.png"));
            hybrid.Compose();
        }
        private static void SaveTriple(FactoryLevelView view,Camera camera,GridDirectHybrid hybrid,string output,string name)
        {
            view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
            LightGridStudy.Save(camera,Path.Combine(output,name+"-current.png"));
            LightGridStudy.SaveField(view,camera,hybrid.GridTexture.Texture,hybrid.Scene,hybrid.Scene.Texel,Path.Combine(output,name+"-grid.png"));
            LightGridStudy.SaveField(view,camera,hybrid.Texture,hybrid.Scene,hybrid.Scene.Texel,Path.Combine(output,name+"-hybrid.png"));
        }
        private static void SetCameraArea(FactoryLevelView view,Camera camera,Rect bounds)
        {
            camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);
            camera.orthographicSize=Mathf.Max(bounds.height*.5f,bounds.width/(2*camera.aspect));
            float height=2*camera.orthographicSize;
            view.SetViewArea(new Rect(bounds.center.x-height*camera.aspect*.5f,bounds.center.y-height*.5f,height*camera.aspect,height));
        }
        private static void RunCycle(Context c,int cycle,Pair[] result,int offset)
        {
            bool currentFirst=(cycle&1)==0;
            for(int action=0;action<4;action++)
            {
                c.NeedsRestoration=true;
                var pair=new Pair{cycle=cycle+1,action=Events[action],currentFirst=currentFirst};
                if(currentFirst) {pair.current=MeasureCurrent(c,action);pair.hybrid=MeasureHybrid(c,action);}
                else {pair.hybrid=MeasureHybrid(c,action);pair.current=MeasureCurrent(c,action);}
                if(pair.hybrid.totalMs<=0) throw new InvalidOperationException("Сравнение гибридного света: нулевая длительность события.");
                pair.currentOverHybrid=pair.current.totalMs/pair.hybrid.totalMs;result[offset+action]=pair;
            }
        }
        private static Sample MeasureCurrent(Context c,int action)
        {
            int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2),builds=c.View.Light.Direct.Builds;
            var clock=Stopwatch.StartNew();
            if(action<2) c.View.SyncLamps(action==0?c.OffDark:c.BaselineDark,++c.LampRevision,c.Broken);
            else {c.Door.open=action==2;c.View.SyncDoors(c.Doors,++c.DoorRevision);}
            FinishCurrent(c.View,c.Fence);
            return new Sample{totalMs=clock.Elapsed.TotalMilliseconds,directBuilds=c.View.Light.Direct.Builds-builds,
                gc0=GC.CollectionCount(0)-gc0,gc1=GC.CollectionCount(1)-gc1,gc2=GC.CollectionCount(2)-gc2};
        }
        private static Sample MeasureHybrid(Context c,int action)
        {
            int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
            var clock=Stopwatch.StartNew();
            if(action<2) c.Hybrid.SetLamp(c.Lamp,action==1);else c.Hybrid.SetDoor(c.Door.id,action==3);
            return new Sample{totalMs=clock.Elapsed.TotalMilliseconds,gridCells=c.Hybrid.LastGridCells,directBuilds=c.Hybrid.LastDirectBuilds,
                composedRect=c.Hybrid.LastComposedRect,gc0=GC.CollectionCount(0)-gc0,gc1=GC.CollectionCount(1)-gc1,gc2=GC.CollectionCount(2)-gc2};
        }
        private static void Restore(Context c)
        {
            c.View.SyncLamps(c.BaselineDark,++c.LampRevision,c.Broken);c.Door.open=false;c.View.SyncDoors(c.Doors,++c.DoorRevision);FinishCurrent(c.View,c.Fence);
            c.Hybrid.SetLamp(c.Lamp,true);c.Hybrid.SetDoor(c.Door.id,true);c.Hybrid.Compose();
        }
        private static void Validate(Context c)
        {
            if(c.Door.open || !c.Hybrid.Scene.Doors[c.Door.id].Blocks || !c.View.Light.Scene.Doors[c.Door.id].Blocks ||
                !c.Hybrid.Sources.TryGet(c.Lamp,out int hybridIndex) || !c.Hybrid.Sources.All[hybridIndex].Enabled ||
                !c.View.Light.Sources.TryGet(c.Lamp,out int currentIndex) || !c.View.Light.Sources.All[currentIndex].Enabled)
                throw new InvalidOperationException("Сравнение гибридного света: цикл не восстановил исходные состояния.");
            var all=new RectInt(0,0,c.Hybrid.Field.Width,c.Hybrid.Field.Height);
            var absorption=GridLightTexture.ReadAbsorption(c.Hybrid.Scene,1,all);
            for(int i=0;i<absorption.Length;i++) if(absorption[i]!=c.Hybrid.Absorption[i])
                throw new InvalidOperationException("Поглощение гибрида отличается от полной сцены, клетка "+i);
            var reference=new GridLightField(c.Hybrid.Field.Width,c.Hybrid.Field.Height,c.Hybrid.Field.CellSize,absorption);
            reference.Solve(c.Hybrid.SeedIndex.Query(all));
            for(int i=0;i<reference.Pixels.Length;i++) if(!reference.Pixels[i].Equals(c.Hybrid.Field.Pixels[i]))
                throw new InvalidOperationException("Локальная сетка гибрида отличается от полного расчёта, клетка "+i);
            c.Hybrid.GridTexture.Validate(c.Hybrid.Field);
            using var direct=new DirectLight(c.Hybrid.Scene,c.Hybrid.Sources,CacheBudget);
            direct.BuildAll();Fence(direct.Sum,c.Fence);direct.WaitForMeasurements();
            RequireSameHalf(c.Hybrid.Direct.Sum,direct.Sum);
            ValidateComposite(c.Hybrid);
        }
        private static void ValidateComposite(GridDirectHybrid hybrid)
        {
            var inputs=new[]{hybrid.Direct.Sum,hybrid.GridTexture.Texture,hybrid.Texture};
            var images=new Texture2D[3];var previous=RenderTexture.active;float maximumError=0;
            try
            {
                for(int n=0;n<images.Length;n++)
                {
                    var image=new Texture2D(inputs[n].width,inputs[n].height,TextureFormat.RGBAHalf,false,true);images[n]=image;
                    RenderTexture.active=(RenderTexture)inputs[n];image.ReadPixels(new Rect(0,0,image.width,image.height),0,0,false);
                }
                var d=images[0].GetRawTextureData<ushort>();var g=images[1].GetRawTextureData<ushort>();var result=images[2].GetRawTextureData<ushort>();
                for(int i=0;i<result.Length;i++)
                {
                    float value=Mathf.Max(0,Mathf.HalfToFloat(d[i]));
                    float expected=i%4==3?1:Mathf.Max(Mathf.Min(value,1)+1-Mathf.Exp(-Mathf.Max(0,value-1)),hybrid.FillGain*Mathf.Max(0,Mathf.HalfToFloat(g[i])));
                    float error=Mathf.Abs(expected-Mathf.HalfToFloat(result[i]));maximumError=Mathf.Max(maximumError,error);
                    if(!float.IsFinite(error) || error>.0025f) throw new InvalidOperationException($"Композит гибрида отличается от CPU-формулы: клетка {i/4}, канал {i%4}, ошибка {error}.");
                }
                Debug.Log($"Композит гибрида совпал с CPU-формулой на всём поле; максимальная ошибка RGBAHalf {maximumError}.");
            }
            finally {RenderTexture.active=previous;foreach(var image in images) if(image!=null) Object.DestroyImmediate(image);}
        }
        private static void RequireSameHalf(Texture actual,Texture expected)
        {
            if(actual.width!=expected.width || actual.height!=expected.height) throw new InvalidOperationException("Размеры сравниваемых Direct-полей различаются.");
            var a=new Texture2D(actual.width,actual.height,TextureFormat.RGBAHalf,false,true);
            var b=new Texture2D(expected.width,expected.height,TextureFormat.RGBAHalf,false,true);var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=(RenderTexture)actual;a.ReadPixels(new Rect(0,0,a.width,a.height),0,0,false);
                RenderTexture.active=(RenderTexture)expected;b.ReadPixels(new Rect(0,0,b.width,b.height),0,0,false);
                var x=a.GetRawTextureData<ushort>();var y=b.GetRawTextureData<ushort>();
                for(int i=0;i<x.Length;i++) if(x[i]!=y[i]) throw new InvalidOperationException($"Локальный Direct гибрида отличается от полной сборки: клетка {i/4}, канал {i%4}, half {x[i]} / {y[i]}.");
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
        }
        private static void FinishCurrent(FactoryLevelView view,Texture2D fence)
        {
            if(view.Light.HasWork) throw new InvalidOperationException("Сравнение гибридного света: production-свет не завершил синхронную работу.");
            Fence(view.Light.Bounce.Indirect,fence);view.Light.Direct.WaitForMeasurements();
        }
        private static void Fence(Texture texture,Texture2D pixel)
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=(RenderTexture)texture;pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
        }
        private static double Median(double[] values)
        {var sorted=values.OrderBy(v=>v).ToArray();return (sorted[(sorted.Length-1)/2]+sorted[sorted.Length/2])*.5;}
        private static double P90(double[] values)
        {var sorted=values.OrderBy(v=>v).ToArray();return sorted[(int)Math.Ceiling(sorted.Length*.9)-1];}
        private static Summary Summarize(string action,Pair[] pairs)=>new()
        {
            action=action,pairs=pairs.Length,hybridWins=pairs.Count(p=>p.hybrid.totalMs<p.current.totalMs),
            currentFirstPairs=pairs.Count(p=>p.currentFirst),hybridFirstPairs=pairs.Count(p=>!p.currentFirst),
            currentMedianMs=Median(pairs.Select(p=>p.current.totalMs).ToArray()),currentP90Ms=P90(pairs.Select(p=>p.current.totalMs).ToArray()),
            hybridMedianMs=Median(pairs.Select(p=>p.hybrid.totalMs).ToArray()),hybridP90Ms=P90(pairs.Select(p=>p.hybrid.totalMs).ToArray()),
            medianPairedCurrentOverHybrid=Median(pairs.Select(p=>p.currentOverHybrid).ToArray()),
            currentFirstMedianRatio=Median(pairs.Where(p=>p.currentFirst).Select(p=>p.currentOverHybrid).ToArray()),
            hybridFirstMedianRatio=Median(pairs.Where(p=>!p.currentFirst).Select(p=>p.currentOverHybrid).ToArray())
        };
        private static string N(double value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
        private static string Label(string action)=>action=="lamp-off"?"Выключить лампу":action=="lamp-on"?"Включить лампу":action=="door-open"?"Открыть дверь":"Закрыть дверь";
        private static void WritePage(Report report,string output)
        {
            var html=new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Текущий свет и заполнение 0,08 / 0,16</title><style>body{background:#20252b;color:#eee;font:17px system-ui;margin:24px}section{margin:24px 0}.triple{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}img{width:100%}pre{white-space:pre-wrap}h3{margin:8px 0}td,th{border:1px solid #667;padding:8px}table{border-collapse:collapse}select,button{font:inherit;padding:8px}button[aria-pressed=true]{background:#83c8fa}.switch{display:flex;gap:10px;flex-wrap:wrap;margin:12px 0}.large{max-width:1280px}@media(max-width:1000px){.triple{grid-template-columns:1fr}}</style><h1>Текущий свет / гибрид 0,08 / гибрид 0,16</h1><p>Во всех тройках одинаковые место, камера, материалы и фоновое освещение. Меняется только схема света и сила заполнения. Игровая EXE не менялась.</p><label>Место или состояние <select id='room'><option value='entry' selected>Вход — стена с вентиляторами</option><option value='hall'>Зал</option><option value='office'>Кабинет</option><option value='door'>Комната с дверью</option><option value='hall-lamp-off'>Зал — лампа выключена</option><option value='hall-door-open'>Зал — дверь открыта</option></select></label><section class='triple'>");
            foreach(var variant in new[]{("current","Текущий игровой свет"),("hybrid-fill08","Направленный + заполнение 0,08"),("hybrid-fill16","Направленный + заполнение 0,16")})
                html.Append($"<div><h3>{variant.Item2}</h3><img id='{variant.Item1}' alt='{variant.Item2}' src='entry-{variant.Item1}.png?v=fill-compare-2'></div>");
            html.Append("</section><details><summary>Крупное переключение в одном месте</summary><p>Переключайте варианты: изображение остаётся на той же позиции, чтобы легче увидеть изменение теней.</p><div class='switch'><button data-suffix='current' aria-pressed='false'>Текущий свет</button><button data-suffix='hybrid-fill08' aria-pressed='true'>Заполнение 0,08</button><button data-suffix='hybrid-fill16' aria-pressed='false'>Заполнение 0,16</button></div><img class='large' id='large' alt='Крупное сравнение выбранного места' src='entry-hybrid-fill08.png?v=fill-compare-2'></details><p>0,08 и 0,16 используют один и тот же направленный свет (геометрия 20 см, до четырёх точек формы). Меняется только слабый нижний предел: max(direct, коэффициент × grid). Это не увеличение всей яркости на 8% или 16%. Отдельного Bounce у нового варианта нет; персонажи и их тени в этой пробе не проверяются.</p><h2>Измерения основного варианта 0,08</h2><p>Таблица относится к отдельному предыдущему бенчмарку: один прогревочный и шесть измеряемых циклов, 24 пары. При обновлении кадров она не переснималась. Время варианта 0,16 отдельно не измерялось. Это завершённые световые события, не FPS игры.</p><table><tr><th>Событие</th><th>Текущий: медиана / p90, мс</th><th>Гибрид 0,08: медиана / p90, мс</th><th>Медиана парных отношений</th></tr>");
            foreach(var s in report.summaries)
                html.Append($"<tr><td>{Label(s.action)}</td><td>{N(s.currentMedianMs)} / {N(s.currentP90Ms)}</td><td>{N(s.hybridMedianMs)} / {N(s.hybridP90Ms)}</td><td>{N(s.medianPairedCurrentOverHybrid)}×</td></tr>");
            html.Append($"</table><p>Учтённая GPU-память: текущий {N(report.currentTotalGpuBytes/1048576.0)} МиБ, гибрид {N(report.hybridGpuBytes/1048576.0)} МиБ. У гибрида также {N(report.hybridCpuBytes/1048576.0)} МиБ CPU-массивов; CPU production не измерен. Это перечисленные ресурсы, не вся память игры.</p><details><summary>Исходный отчёт бенчмарка и ограничения</summary><pre>{WebUtility.HtmlEncode(JsonUtility.ToJson(report,true))}</pre></details><p><a href='report.json'>Отчёт JSON</a></p>");
            html.Append("<script>let suffix='hybrid-fill08';const room=document.getElementById('room');function update(){for(const s of ['current','hybrid-fill08','hybrid-fill16'])document.getElementById(s).src=room.value+'-'+s+'.png?v=fill-compare-2';document.getElementById('large').src=room.value+'-'+suffix+'.png?v=fill-compare-2';for(const b of document.querySelectorAll('[data-suffix]'))b.setAttribute('aria-pressed',String(b.dataset.suffix===suffix));}room.onchange=update;for(const b of document.querySelectorAll('[data-suffix]'))b.onclick=()=>{suffix=b.dataset.suffix;update();};update();</script></html>");
            File.WriteAllText(Path.Combine(output,"index.html"),html.ToString());
        }
    }
}