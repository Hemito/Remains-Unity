using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Paired, completed event timings under the machine's EXISTING load. Never creates synthetic load.
    public static class PairedLightBenchmark
    {
        private const int WarmupCycles=2,MeasuredCycles=12;
        private static readonly string[] Events={"lamp-off","lamp-on","door-open","door-close"};
        [Serializable] private sealed class Sample
        {
            public double totalMs,processCpuMs,beforeFinalWaitMs,finalWaitMs;
            public double geometryReadbackMs,managedQuerySolveMs,uploadMs;
            public int gc0,gc1,gc2,uploadedCells,readbackTexels;
            public long recomputedCells;
        }
        [Serializable] private sealed class Pair
        {
            public int cycle;
            public string action;
            public bool currentFirst;
            public Sample current,grid;
            public double currentOverGrid;
        }
        [Serializable] private sealed class Summary
        {
            public string action;
            public int pairs,currentWins,gridWins,ties,currentFirstPairs,gridFirstPairs;
            public double currentMedianMs,currentP90Ms,gridMedianMs,gridP90Ms,medianPairedCurrentOverGrid;
            public double currentFirstMedianRatio,gridFirstMedianRatio,currentMedianProcessCpuMs,gridMedianProcessCpuMs;
            public int currentGc0,currentGc1,currentGc2,gridGc0,gridGc1,gridGc2;
        }
        [Serializable] private sealed class Report
        {
            public string startedUtc,finishedUtc,device,api,lamp,door,method,validation;
            public int seed,width,height,colorGroups,sources,enabledSources,warmupCycles,measuredCycles,measuredPairs,measuredVariantSamples;
            public double initialCurrentBuildMs,initialGridBuildMs,commonPrepareMs,gridDataPrepareMs;
            public long currentSceneGpuBytes,currentSumGpuBytes,currentCacheGpuBytes,currentBounceGpuBytes,currentTotalGpuBytes;
            public long gridCpuPayloadBytes,gridFieldGpuBytes,gridGeometryGpuBytes,gridTotalGpuBytes;
            public int measuredGc0,measuredGc1,measuredGc2;
            public Pair[] warmup,pairs;
            public Summary[] summaries;
        }
        private sealed class Context
        {
            public FactoryLevelView View;
            public LevelLightScene Scene;
            public GridLightField Field;
            public GridLightSeeds Seeds;
            public GridLightTexture Texture;
            public WorldState World;
            public DoorState[] Doors;
            public DoorState Door;
            public LevelLightScene.Door[] GridDoors;
            public LevelLightScene.Door OriginalGridDoor;
            public int GridDoorIndex,Divisor;
            public string Lamp;
            public string[] BaselineDark,BaselineBroken,OffDark;
            public float[] Absorption;
            public RectInt LampCells,DoorCells;
            public long DoorRevision;
            public Texture2D Fence;
            public Process Process;
            public bool NeedsRestoration;
        }

        public static void Run(FactoryLevelView view,LevelLightScene independentScene,GridLightField field,GridLightSeeds seedIndex,
            float[] absorption,GridLightTexture texture,WorldState world,string output,double initialCurrentBuildMs,
            double initialGridBuildMs,double commonPrepareMs,double gridDataPrepareMs)
        {
            if(view==null || view.Light==null || independentScene==null || field==null || seedIndex==null || absorption==null || texture==null || world==null)
                throw new ArgumentException("Парный тест света: отсутствуют подготовленные данные.");
            if(Application.isPlaying) throw new InvalidOperationException("Парный тест света предназначен для синхронного Editor batch, вне Play Mode.");
            string page=Path.Combine(output,"index.html");
            if(!File.Exists(page)) throw new FileNotFoundException("Парный тест света: сначала нужен существующий предпросмотр index.html.",page);
            var lamp=view.Light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && s.Enabled && s.Id.StartsWith("factory:0:1,1:",StringComparison.Ordinal))
                .OrderBy(s=>s.Id,StringComparer.Ordinal).FirstOrDefault();
            if(lamp==null) throw new InvalidOperationException("Парный тест света: в зале завода104 нет включённой лампы.");
            var doors=world.doors.Where(d=>d.level=="factory:0").ToArray();
            var door=doors.Where(d=>d.Blocks && d.id.StartsWith("factory:0:1,1:",StringComparison.Ordinal)).OrderBy(d=>d.id,StringComparer.Ordinal).FirstOrDefault();
            if(door==null) throw new InvalidOperationException("Парный тест света: в зале завода104 нет закрытой двери.");
            var gridDoors=independentScene.Doors.Values.ToArray();int gridDoorIndex=Array.FindIndex(gridDoors,d=>d.Id==door.id);
            if(gridDoorIndex<0 || !gridDoors[gridDoorIndex].Blocks) throw new InvalidOperationException("Парный тест света: независимая сцена не содержит такую же закрытую дверь "+door.id);
            int divisor=Mathf.RoundToInt(field.CellSize/independentScene.Texel);
            if(divisor<=0 || independentScene.Width!=field.Width*divisor || independentScene.Height!=field.Height*divisor || absorption.Length!=field.Pixels.Length)
                throw new InvalidOperationException("Парный тест света: размеры независимой геометрии и сетки не согласованы.");
            var baselineDark=view.Light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && !s.Enabled).Select(s=>s.Id).ToArray();
            var broken=world.lamps.Where(l=>l.level=="factory:0" && l.broken).Select(l=>l.id).ToArray();
            if(broken.Except(baselineDark,StringComparer.Ordinal).Any()) throw new InvalidOperationException("Парный тест света: разбитая лампа отсутствует в исходном списке погашенных.");
            var context=new Context{View=view,Scene=independentScene,Field=field,Seeds=seedIndex,Texture=texture,World=world,Doors=doors,Door=door,
                GridDoors=gridDoors,GridDoorIndex=gridDoorIndex,OriginalGridDoor=gridDoors[gridDoorIndex],Divisor=divisor,Lamp=lamp.Id,
                BaselineDark=baselineDark,BaselineBroken=broken,OffDark=baselineDark.Concat(new[]{lamp.Id}).ToArray(),Absorption=absorption,
                LampCells=seedIndex.Bounds(lamp.Id),DoorCells=GridLightTexture.CoarseRect(independentScene,divisor,gridDoors[gridDoorIndex].Bounds),
                DoorRevision=view.AppliedDoorRevision,Fence=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true),Process=Process.GetCurrentProcess()};
            Exception failure=null;
            try
            {
                view.Focus(lamp.Bounds.center);FinishCurrent(context);
                var report=new Report{startedUtc=DateTime.UtcNow.ToString("O"),device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                    lamp=lamp.Id,door=door.id,seed=world.seed,width=field.Width,height=field.Height,colorGroups=field.GroupCount,
                    sources=view.Light.Sources.Count,enabledSources=view.Light.Sources.All.Count(s=>s.Enabled),warmupCycles=WarmupCycles,measuredCycles=MeasuredCycles,
                    measuredPairs=MeasuredCycles*Events.Length,measuredVariantSamples=MeasuredCycles*Events.Length*2,
                    initialCurrentBuildMs=initialCurrentBuildMs,initialGridBuildMs=initialGridBuildMs,commonPrepareMs=commonPrepareMs,gridDataPrepareMs=gridDataPrepareMs,
                    warmup=new Pair[WarmupCycles*Events.Length],pairs=new Pair[MeasuredCycles*Events.Length],
                    method="Нагрузка ПК не создаётся и не меняется тестом. Editor batch, полный завод104. Два прогревочных и12 измеряемых циклов; каждый: лампа выключена/включена, дверь открыта/закрыта. Порядок текущий/сетка чередуется по циклам. Каждая операция завершается GPU fence и нужными callbacks. beforeFinalWaitMs текущего света включает внутренние ожидания драйвера в Sync*, это не чистое CPU-время. processCpuMs — изменение TotalProcessorTime всего процесса Unity, всех его потоков; оно может быть больше wall-time. geometryReadbackMs включает GPU-работу и ожидание чтения. GPU timestamps, обычные кадры, FPS и затраты отрисовки не измерялись. Световые модели дают разные изображения. Память: payload массивов/текстур без CLR/driver, источников/индексов, artwork, тестовых references и compositor камеры. Внешняя нагрузка и частоты записываются отдельно."};
                for(int cycle=0;cycle<WarmupCycles;cycle++) RunCycle(context,cycle,report.warmup,cycle*Events.Length);
                Debug.Log("Парный тест света: прогрев2/2 завершён; начинаются12 измеряемых циклов.");
                int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
                for(int cycle=0;cycle<MeasuredCycles;cycle++)
                {
                    RunCycle(context,cycle,report.pairs,cycle*Events.Length);
                    if((cycle+1)%4==0) Debug.Log($"Парный тест света: измерены {cycle+1}/{MeasuredCycles} циклов, {(cycle+1)*Events.Length} пар.");
                }
                report.measuredGc0=GC.CollectionCount(0)-gc0;report.measuredGc1=GC.CollectionCount(1)-gc1;report.measuredGc2=GC.CollectionCount(2)-gc2;
                ValidateRestoration(context);context.NeedsRestoration=false;
                report.validation="После измерений: лампа включена, дверь закрыта; полная карта поглощения равна локальным патчам; сетка равна полному расчёту; GPU-текстура равна CPU-полю в RGBAHalf.";
                report.summaries=Events.Select(action=>Summarize(action,report.pairs.Where(p=>p.action==action).ToArray())).ToArray();
                report.currentSceneGpuBytes=view.Light.Scene.Bytes;report.currentSumGpuBytes=view.Light.Direct.SumBytes;
                report.currentCacheGpuBytes=view.Light.Direct.CacheBytes;report.currentBounceGpuBytes=view.Light.Bounce.Bytes;
                report.currentTotalGpuBytes=report.currentSceneGpuBytes+report.currentSumGpuBytes+report.currentCacheGpuBytes+report.currentBounceGpuBytes;
                report.gridCpuPayloadBytes=field.AllocatedBytes+absorption.LongLength*4;report.gridFieldGpuBytes=texture.Bytes;
                report.gridGeometryGpuBytes=independentScene.Bytes;report.gridTotalGpuBytes=texture.Bytes+independentScene.Bytes;
                report.finishedUtc=DateTime.UtcNow.ToString("O");
                string json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(output,"paired-performance.json"),json);AppendPage(page,report,json);
                Debug.Log("Парный тест света: PASS; 48 пар,96 измерений; результаты: "+Path.Combine(output,"paired-performance.json"));
            }
            catch(Exception error) {failure=error;throw;}
            finally
            {
                try
                {
                    if(context.NeedsRestoration) Restore(context);
                }
                catch(Exception error)
                {
                    if(failure==null) throw;
                    Debug.LogError($"Парный тест света: не удалось восстановить лампу {context.Lamp} и дверь {context.Door.id}: {error}");
                }
                finally {Object.DestroyImmediate(context.Fence);context.Process.Dispose();}
            }
        }

        private static void RunCycle(Context context,int cycle,Pair[] destination,int offset)
        {
            bool currentFirst=(cycle&1)==0;
            for(int action=0;action<Events.Length;action++)
            {
                context.NeedsRestoration=true;
                var pair=new Pair{cycle=cycle+1,action=Events[action],currentFirst=currentFirst};
                if(currentFirst) {pair.current=MeasureCurrent(context,action);pair.grid=MeasureGrid(context,action);}
                else {pair.grid=MeasureGrid(context,action);pair.current=MeasureCurrent(context,action);}
                if(pair.grid.totalMs<=0) throw new InvalidOperationException("Парный тест света: нулевая длительность операции сетки.");
                pair.currentOverGrid=pair.current.totalMs/pair.grid.totalMs;destination[offset+action]=pair;
            }
        }
        private static Sample MeasureCurrent(Context c,int action)
        {
            var sample=new Sample();int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
            TimeSpan cpu=c.Process.TotalProcessorTime;long start=Stopwatch.GetTimestamp();
            if(action<2) c.View.SyncLamps(action==0?c.OffDark:c.BaselineDark,++c.World.lampRevision,c.BaselineBroken);
            else {c.Door.open=action==2;c.View.SyncDoors(c.Doors,++c.DoorRevision);}
            sample.beforeFinalWaitMs=Elapsed(start);long wait=Stopwatch.GetTimestamp();FinishCurrent(c);
            sample.finalWaitMs=Elapsed(wait);sample.totalMs=Elapsed(start);sample.processCpuMs=(c.Process.TotalProcessorTime-cpu).TotalMilliseconds;
            sample.gc0=GC.CollectionCount(0)-gc0;sample.gc1=GC.CollectionCount(1)-gc1;sample.gc2=GC.CollectionCount(2)-gc2;return sample;
        }
        private static Sample MeasureGrid(Context c,int action)
        {
            var sample=new Sample();int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
            TimeSpan cpu=c.Process.TotalProcessorTime;long start=Stopwatch.GetTimestamp();RectInt changed;
            if(action>=2)
            {
                var door=c.OriginalGridDoor;c.GridDoors[c.GridDoorIndex]=new LevelLightScene.Door(door.Id,door.Bounds,door.Opacity,action!=2);
                c.Scene.SetDoors(c.GridDoors,null);var patch=GridLightTexture.ReadAbsorption(c.Scene,c.Divisor,c.DoorCells);
                GridLightTexture.PatchAbsorption(c.Absorption,c.Field.Width,c.DoorCells,patch);changed=c.DoorCells;
                sample.geometryReadbackMs=Elapsed(start);sample.readbackTexels=changed.width*changed.height*c.Divisor*c.Divisor;
            }
            else changed=c.LampCells;
            long managed=Stopwatch.GetTimestamp();
            if(action<2) c.Seeds.SetEnabled(c.Lamp,action==1);
            var affected=c.Field.Update(c.Seeds.Query(c.Field.AffectedRegion(changed)),changed);
            sample.managedQuerySolveMs=Elapsed(managed);c.Texture.Update(c.Field,affected);
            sample.totalMs=Elapsed(start);sample.processCpuMs=(c.Process.TotalProcessorTime-cpu).TotalMilliseconds;
            sample.uploadMs=c.Texture.LastUploadMs;sample.finalWaitMs=c.Texture.LastWaitMs;
            sample.beforeFinalWaitMs=sample.geometryReadbackMs+sample.managedQuerySolveMs+sample.uploadMs;
            sample.recomputedCells=c.Field.RecomputedCells;sample.uploadedCells=c.Texture.UploadedCells;
            sample.gc0=GC.CollectionCount(0)-gc0;sample.gc1=GC.CollectionCount(1)-gc1;sample.gc2=GC.CollectionCount(2)-gc2;return sample;
        }
        private static void FinishCurrent(Context c)
        {
            if(c.View.Light.HasWork) throw new InvalidOperationException("Парный тест света: Sync не завершил синхронную работу текущего света.");
            var previous=RenderTexture.active;
            try {RenderTexture.active=(RenderTexture)c.View.Light.Bounce.Indirect;c.Fence.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
            c.View.Light.Direct.WaitForMeasurements();
        }
        private static void Restore(Context c)
        {
            c.View.SyncLamps(c.BaselineDark,++c.World.lampRevision,c.BaselineBroken);c.Door.open=false;c.View.SyncDoors(c.Doors,++c.DoorRevision);FinishCurrent(c);
            c.GridDoors[c.GridDoorIndex]=c.OriginalGridDoor;c.Scene.SetDoors(c.GridDoors,null);
            GridLightTexture.PatchAbsorption(c.Absorption,c.Field.Width,c.DoorCells,GridLightTexture.ReadAbsorption(c.Scene,c.Divisor,c.DoorCells));
            c.Seeds.SetEnabled(c.Lamp,true);
            var changed=Union(c.LampCells,c.DoorCells);var area=c.Field.Update(c.Seeds.Query(c.Field.AffectedRegion(changed)),changed);c.Texture.Update(c.Field,area);
        }
        private static void ValidateRestoration(Context c)
        {
            if(c.Door.open || !c.Scene.Doors[c.Door.id].Blocks || !c.View.Light.Sources.TryGet(c.Lamp,out int lamp) || !c.View.Light.Sources.All[lamp].Enabled)
                throw new InvalidOperationException("Парный тест света: цикл не восстановил исходные состояния.");
            var all=new RectInt(0,0,c.Field.Width,c.Field.Height);var geometry=GridLightTexture.ReadAbsorption(c.Scene,c.Divisor,all);
            for(int i=0;i<geometry.Length;i++) if(geometry[i]!=c.Absorption[i]) throw new InvalidOperationException("Парный тест света: локальное поглощение не совпало с полной сценой, клетка "+i);
            var reference=new GridLightField(c.Field.Width,c.Field.Height,c.Field.CellSize,c.Absorption);reference.Solve(c.Seeds.Query(all));
            for(int i=0;i<c.Field.Pixels.Length;i++) if(!c.Field.Pixels[i].Equals(reference.Pixels[i])) throw new InvalidOperationException("Парный тест света: локальное поле не совпало с полным, клетка "+i);
            c.Texture.Validate(c.Field);
        }
        private static RectInt Union(RectInt a,RectInt b)
        {int x=Math.Min(a.xMin,b.xMin),y=Math.Min(a.yMin,b.yMin);return new RectInt(x,y,Math.Max(a.xMax,b.xMax)-x,Math.Max(a.yMax,b.yMax)-y);}
        private static double Elapsed(long start)=>(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
        private static double Median(double[] values)
        {var sorted=values.OrderBy(v=>v).ToArray();return (sorted[(sorted.Length-1)/2]+sorted[sorted.Length/2])*.5;}
        private static double P90(double[] values)
        {var sorted=values.OrderBy(v=>v).ToArray();return sorted[(int)Math.Ceiling(sorted.Length*.9)-1];}
        private static Summary Summarize(string action,Pair[] pairs)
        {
            var a=pairs.Select(p=>p.current.totalMs).ToArray();var b=pairs.Select(p=>p.grid.totalMs).ToArray();
            return new Summary{action=action,pairs=pairs.Length,currentWins=pairs.Count(p=>p.current.totalMs<p.grid.totalMs),gridWins=pairs.Count(p=>p.grid.totalMs<p.current.totalMs),ties=pairs.Count(p=>p.grid.totalMs==p.current.totalMs),
                currentMedianMs=Median(a),currentP90Ms=P90(a),gridMedianMs=Median(b),gridP90Ms=P90(b),medianPairedCurrentOverGrid=Median(pairs.Select(p=>p.currentOverGrid).ToArray()),
                currentFirstPairs=pairs.Count(p=>p.currentFirst),gridFirstPairs=pairs.Count(p=>!p.currentFirst),
                currentFirstMedianRatio=Median(pairs.Where(p=>p.currentFirst).Select(p=>p.currentOverGrid).ToArray()),gridFirstMedianRatio=Median(pairs.Where(p=>!p.currentFirst).Select(p=>p.currentOverGrid).ToArray()),
                currentMedianProcessCpuMs=Median(pairs.Select(p=>p.current.processCpuMs).ToArray()),gridMedianProcessCpuMs=Median(pairs.Select(p=>p.grid.processCpuMs).ToArray()),
                currentGc0=pairs.Sum(p=>p.current.gc0),currentGc1=pairs.Sum(p=>p.current.gc1),currentGc2=pairs.Sum(p=>p.current.gc2),
                gridGc0=pairs.Sum(p=>p.grid.gc0),gridGc1=pairs.Sum(p=>p.grid.gc1),gridGc2=pairs.Sum(p=>p.grid.gc2)};
        }
        private static string N(double value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
        private static string Label(string action)=>action=="lamp-off"?"Выключить лампу":action=="lamp-on"?"Включить лампу":action=="door-open"?"Открыть дверь":"Закрыть дверь";
        private static void AppendPage(string path,Report report,string json)
        {
            var section=new StringBuilder("<section id=\"paired-performance\"><h2>Парное сравнение при текущей нагрузке ПК</h2><p>Нагрузка тестом не создавалась. Полный завод104;2 прогревочных и12 измеряемых циклов:48 пар,96 операций. В каждой паре одно и то же событие, порядок вариантов чередуется. Каждая операция завершена, включая финальное ожидание GPU. Обычный FPS и кадры игры не измерялись; изображения двух световых моделей различаются.</p><table><thead><tr><th>Событие</th><th>Текущий: медиана / p90, мс</th><th>Сетка: медиана / p90, мс</th><th>Медиана парных отношений текущий/сетка</th><th>Побед сетки из12</th></tr></thead><tbody>");
            foreach(var s in report.summaries) section.Append($"<tr><td>{Label(s.action)}</td><td>{N(s.currentMedianMs)} / {N(s.currentP90Ms)}</td><td>{N(s.gridMedianMs)} / {N(s.gridP90Ms)}</td><td>{N(s.medianPairedCurrentOverGrid)}×</td><td>{s.gridWins}</td></tr>");
            section.Append($"</tbody></table><p>Разовая подготовка: общая {N(report.commonPrepareMs)}мс; данные сетки {N(report.gridDataPrepareMs)}мс. Первичное построение: текущий {N(report.initialCurrentBuildMs)}мс; сетка {N(report.initialGridBuildMs)}мс. Эти значения переданы вызывающим harness.</p>");
            section.Append($"<p>Payload GPU текущего света: {N(report.currentTotalGpuBytes/1048576.0)}МиБ; сетки вместе с независимой геометрией: {N(report.gridTotalGpuBytes/1048576.0)}МиБ. CPU-массивы сетки: {N(report.gridCpuPayloadBytes/1048576.0)}МиБ. Размер объектов, индексов, artwork, буферов камеры и тестовых reference не включён. CPU-структуры текущего света отдельно не измерены.</p>");
            section.Append($"<p>За измеряемый этап: GC поколений0/1/2 — {report.measuredGc0}/{report.measuredGc1}/{report.measuredGc2}. В сырых данных есть порядок каждой пары, стадии, CPU-время всего процесса Unity и GC каждой операции. beforeFinalWaitMs текущего варианта включает внутренние ожидания драйвера; это не чистая CPU-работа. Внешняя нагрузка, частоты и видеопамять записываются отдельно.</p><p>{WebUtility.HtmlEncode(report.validation)}</p><details><summary>Все измерения и ограничения</summary><pre>{WebUtility.HtmlEncode(json)}</pre></details></section>");
            string html=File.ReadAllText(path);var existing=new Regex("<section\\s+id=[\"']paired-performance[\"']\\s*>[\\s\\S]*?</section>");
            if(existing.IsMatch(html)) html=existing.Replace(html,_=>section.ToString(),1);
            else
            {
                int end=html.LastIndexOf("</body>",StringComparison.OrdinalIgnoreCase);if(end<0) end=html.LastIndexOf("</html>",StringComparison.OrdinalIgnoreCase);
                html=end<0?html+section:html.Insert(end,section.ToString());
            }
            File.WriteAllText(path,html);
        }
    }
}
