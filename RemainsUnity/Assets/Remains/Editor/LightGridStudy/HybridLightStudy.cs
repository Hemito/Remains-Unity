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
                    if(name=="hall")
                    {
                        hybrid.Compose(0);LightGridStudy.SaveField(view,camera,hybrid.Texture,hybrid.Scene,hybrid.Scene.Texel,Path.Combine(output,"hall-hybrid-fill0.png"));
                        hybrid.Compose(.16f);LightGridStudy.SaveField(view,camera,hybrid.Texture,hybrid.Scene,hybrid.Scene.Texel,Path.Combine(output,"hall-hybrid-fill16.png"));
                        hybrid.Compose();
                    }
                }
                context=CreateContext(view,hybrid,world,fence);
                SetCameraArea(view,camera,hallBounds);view.Focus(hallBounds.center);FinishCurrent(view,fence);SetCameraArea(view,camera,hallBounds);
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
            var html=new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Направленный свет и слабое клеточное заполнение</title><style>body{background:#20252b;color:#eee;font:17px system-ui;margin:24px}section{margin:28px 0}.triple{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.pair{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}img{width:100%}pre{white-space:pre-wrap}h3{margin:8px 0}td,th{border:1px solid #667;padding:8px}table{border-collapse:collapse}select{font:inherit;padding:6px}@media(max-width:1000px){.triple,.pair{grid-template-columns:1fr}}</style><h1>Направленный свет + слабое клеточное заполнение</h1><p>Изолированная Editor-проба; игровая EXE и runtime не переключались. Во всех тройках одни и те же комната, камера, материалы и ambient.</p><p>Слева штатный Direct + Bounce. В центре общая сетка. Справа направленный Direct с геометрией20см и максимум4 точками источника, мягким ограничением яркости и слабым заполнением max(direct,0.08 × grid), без Bounce. Это разные модели света, а не одинаковое изображение с другими настройками качества. Персонаж, его тени, вспышки и движущиеся источники здесь не проверяются.</p><label>Комната <select id='room'><option value='hall'>Зал</option><option value='office'>Кабинет</option><option value='door'>Комната с дверью</option><option value='entry'>Вход</option></select></label><section class='triple'>");
            foreach(var variant in new[]{("current","Текущий свет"),("grid","Общая сетка"),("hybrid","Направленный + слабая сетка")})
                html.Append($"<div><h3>{variant.Item2}</h3><img id='{variant.Item1}' src='hall-{variant.Item1}.png'></div>");
            html.Append("</section><h2>Сколько даёт заполнение сеткой</h2><p>Один и тот же направленный свет в зале. Ниже заполнение отключено, затем0.08 и0.16. Картинка0.08 совпадает с правой колонкой выше.</p><section class='triple'><div><h3>Без заполнения</h3><img src='hall-hybrid-fill0.png'></div><div><h3>Заполнение0.08</h3><img src='hall-hybrid.png'></div><div><h3>Заполнение0.16</h3><img src='hall-hybrid-fill16.png'></div></section><h2>Локальные события полного завода104</h2><p>Один прогревочный и6 измеряемых циклов;24 пары /48 операций. Порядок текущий/новый чередуется; каждая операция завершена, включая GPU. Это время светового события при имеющейся нагрузке ПК, не FPS и не стоимость обычного кадра. Снимки и полные эталоны выполнены вне измеряемых пар.</p><table><tr><th>Событие</th><th>Текущий: медиана / p90, мс</th><th>Новый: медиана / p90, мс</th><th>Медиана парных отношений</th></tr>");
            foreach(var s in report.summaries) html.Append($"<tr><td>{Label(s.action)}</td><td>{N(s.currentMedianMs)} / {N(s.currentP90Ms)}</td><td>{N(s.hybridMedianMs)} / {N(s.hybridP90Ms)}</td><td>{N(s.medianPairedCurrentOverHybrid)}×</td></tr>");
            html.Append($"</table><p>Разовая независимая подготовка нового варианта {N(report.hybridPrepareMs)}мс; его первый BuildAll с завершением GPU {N(report.hybridInitialBuildMs)}мс. Первый Focus текущего варианта с подготовкой света и окна изображения {N(report.currentInitialFocusBuildMs)}мс. Общая подготовка игрового уровня не включена. Это по одному измерению, не распределение загрузки.</p><p>GPU payload текущего варианта {N(report.currentTotalGpuBytes/1048576.0)}МиБ (сцена, Direct, кэш, Bounce), нового {N(report.hybridGpuBytes/1048576.0)}МиБ. CPU payload нового {N(report.hybridCpuBytes/1048576.0)}МиБ; CPU production не измерен. Это перечисленные ресурсы активного зала, не вся память игры.</p><p>GC0/1/2 за измеряемую серию: {report.measuredGc0}/{report.measuredGc1}/{report.measuredGc2}.</p><p>{WebUtility.HtmlEncode(report.validation)}</p><details><summary>Сырые измерения, модель и ограничения</summary><pre>{WebUtility.HtmlEncode(JsonUtility.ToJson(report,true))}</pre></details><p><a href='report.json'>Отчёт JSON</a></p><script>document.getElementById('room').onchange=e=>['current','grid','hybrid'].forEach(id=>document.getElementById(id).src=e.target.value+'-'+id+'.png')</script></html>");
            File.WriteAllText(Path.Combine(output,"index.html"),html.ToString());
        }
    }
}
