using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using Debug=UnityEngine.Debug;

namespace Remains.Diagnostics
{
    // Always-on slow-frame journal for player reports: each frame is split by Unity player-loop phase
    // (markers inserted around every top-level phase), plus named sections of our own code (Section scopes)
    // and managed GC runs. Frames longer than SlowMilliseconds are written as one line each, and a summary line
    // every SummarySeconds; the file is appended at most once per summary period and on quit. Cost when nothing
    // is slow: a few timestamp reads per frame, no allocation.
    // Video card: a frame waiting in TimeUpdate pays for GPU work submitted earlier, so a slow line also lists our own
    // GPU submissions (Gpu calls) of this and the previous frame, Unity's render counters of both frames and the GPU
    // times of the latest completed frames (FrameTimingManager); the summary gives the period's GPU maximum.
    public static class FrameHitchLog
    {
        public const double SlowMilliseconds=50;
        private const double SummarySeconds=10;
        private const int MaxSections=256;

        // TimeUpdate first: it holds the wait for the previous frame's presentation (GPU-bound frames) and the frame
        // rate limit, so a frame's total is its real duration.
        private static readonly string[] phaseNames={"TimeUpdate","Initialization","EarlyUpdate","FixedUpdate","PreUpdate","Update","PreLateUpdate","PostLateUpdate"};
        private static readonly double[] phaseMs=new double[phaseNames.Length];
        private static readonly long[] phaseStart=new long[phaseNames.Length];
        private static readonly string[] sectionNames=new string[MaxSections];
        private static readonly double[] sectionMs=new double[MaxSections];
        private static readonly int[] sectionCalls=new int[MaxSections];
        private static readonly double[] periodSectionMs=new double[MaxSections];   // summary: per-section totals of the period
        private static readonly Dictionary<string,int> sectionIndex=new(StringComparer.Ordinal);
        private static readonly List<double> periodFrames=new(4096);
        private static readonly StringBuilder pending=new();
        private static long frameStart;
        private static long baselineFrameAllocated;
        private static bool baselineFrameActive;
        private static int pendingBaselineFrames;
        private static ProfilerRecorder baselineAllocations;
        // Unity 6000.0 Mono returns zero from GC.GetAllocatedBytesForCurrentThread.
        // This native byte counter is global even with CollectOnlyOnCurrentThread (verified
        // by a worker allocation). Deltas describe all managed threads during the interval.
        private static long BaselineAllocatedBytes=>baselineAllocations.CurrentValue;
        public static void BeginCpuBaseline(int frames)
        {
            if(frames<=0) throw new ArgumentOutOfRangeException(nameof(frames));
            baselineAllocations=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
            if(!baselineAllocations.Valid || baselineAllocations.UnitType.ToString()!="Bytes")
                throw new InvalidOperationException("CPU-замер: недоступен байтовый счётчик GC Allocated In Frame.");
            long before=BaselineAllocatedBytes;var check=new byte[4096];check[0]=1;
            if(BaselineAllocatedBytes-before<4096) throw new InvalidOperationException("CPU-замер: счётчик Unity не видит известное выделение памяти.");
            GC.KeepAlive(check);
            CpuBaselineCapture.Enabled=false;pendingBaselineFrames=frames;
        }
        public static void EndCpuBaseline()
        {
            CpuBaselineCapture.Enabled=false;pendingBaselineFrames=0;
            if(baselineAllocations.Valid) baselineAllocations.Dispose();
            baselineAllocations=default;
        }
        private static int gcAtFrameStart,gcAtPeriodStart;
        private static double periodStart;
        private static string path;
        private static bool installed;
        private static double toMs;

        // Our own GPU submissions by name, this frame and the previous one.
        private const int MaxGpu=64;
        private static readonly string[] gpuNames=new string[MaxGpu];
        private static int[] gpuCalls=new int[MaxGpu],previousGpuCalls=new int[MaxGpu];
        private static readonly Dictionary<string,int> gpuIndex=new(StringComparer.Ordinal);
        public static void Gpu(string name)
        {
            if(!installed) return;
            if(!gpuIndex.TryGetValue(name,out int index))
            {
                if(gpuIndex.Count>=MaxGpu) return;
                index=gpuIndex.Count;gpuIndex.Add(name,index);gpuNames[index]=name;
            }
            gpuCalls[index]++;
        }
        // Unity render counters (values of the frame just rendered) and GPU frame timings.
        private static readonly (string Label,string Counter,ProfilerCategory Category,bool Bytes)[] counterSpecs=
        {
            ("отрисовок","Draw Calls Count",ProfilerCategory.Render,false),("пакетов","Batches Count",ProfilerCategory.Render,false),
            ("смен шейдера","SetPass Calls Count",ProfilerCategory.Render,false),("треугольников","Triangles Count",ProfilerCategory.Render,false),
            ("текстур рендера","Render Textures Count",ProfilerCategory.Render,false),("память текстур рендера","Render Textures Bytes",ProfilerCategory.Render,true),
            ("память текстур","Used Textures Bytes",ProfilerCategory.Render,true),("память видеокарты","Gfx Used Memory",ProfilerCategory.Memory,true)
        };
        private static ProfilerRecorder[] counters;
        private static long[] counterValues,previousCounterValues;
        private static readonly FrameTiming[] timings=new FrameTiming[4];
        private static bool frameTimings;
        private static ulong lastTimingStamp;
        private static double periodGpuMax;
        private static int periodGpuSlow;

        private static string note;
        // Extra detail for the current frame's line (e.g. what changed in a snapshot); kept only if the frame is slow.
        public static void Note(string text) {if(installed) note=note==null?text:note+"; "+text;}

        // Short description of where the player is (level, counts); set by the runtime on changes.
        public static string Context="";
        public static string Path=>path;

        public static void Install()
        {
            if(installed) return;
            installed=true;
            toMs=1000.0/Stopwatch.Frequency;
            var loop=PlayerLoop.GetCurrentPlayerLoop();
            var systems=loop.subSystemList;
            for(int i=0;i<systems.Length;i++)
            {
                int phase=Array.IndexOf(phaseNames,systems[i].type?.Name);
                if(phase<0) continue;
                var original=systems[i].subSystemList??Array.Empty<PlayerLoopSystem>();
                var inner=new List<PlayerLoopSystem>(original.Length*2+2);
                int captured=phase;
                inner.Add(new PlayerLoopSystem {type=typeof(FrameHitchLog),updateDelegate=()=>BeginPhase(captured)});
                // Each Unity subsystem of the phase as its own section ("<phase>.<subsystem>"), timed between markers.
                foreach(var sub in original)
                {
                    int section=SectionIndex(phaseNames[phase]+"."+(sub.type?.Name??"?"));
                    inner.Add(new PlayerLoopSystem {type=typeof(FrameHitchLog),updateDelegate=()=>subStart=Stopwatch.GetTimestamp()});
                    inner.Add(sub);
                    inner.Add(new PlayerLoopSystem {type=typeof(FrameHitchLog),updateDelegate=()=>EndSub(section)});
                }
                inner.Add(new PlayerLoopSystem {type=typeof(FrameHitchLog),updateDelegate=()=>EndPhase(captured)});
                systems[i].subSystemList=inner.ToArray();
            }
            loop.subSystemList=systems;
            PlayerLoop.SetPlayerLoop(loop);
            string directory=System.IO.Path.Combine(Remains.Runtime.ProjectStorage.RootDirectory,"Diagnostics");
            Directory.CreateDirectory(directory);
            path=System.IO.Path.Combine(directory,"frames-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
            pending.Append("# Журнал медленных кадров (>").Append(SlowMilliseconds).Append(" мс). Фазы Unity и участки кода в мс; gc — сборки мусора за кадр. Кадр — от TimeUpdate (ожидание видеокарты и предела кадров) до конца PostLateUpdate.\n");
            pending.Append("# ").Append(Application.version).Append(", ").Append(SystemInfo.processorType).Append(", ").Append(SystemInfo.graphicsDeviceName).Append('\n');
            InstallGpu();
            Application.quitting+=Flush;
            Debug.Log("Журнал медленных кадров: "+path);
        }

        private static void InstallGpu()
        {
            var missing=new List<string>();
            // Unity fills its render counters only in development players; a release build would log zeros.
            if(Debug.isDebugBuild || Application.isEditor)
            {
            counters=new ProfilerRecorder[counterSpecs.Length];counterValues=new long[counterSpecs.Length];previousCounterValues=new long[counterSpecs.Length];
            for(int i=0;i<counterSpecs.Length;i++)
            {
                counters[i]=ProfilerRecorder.StartNew(counterSpecs[i].Category,counterSpecs[i].Counter);
                if(!counters[i].Valid) missing.Add(counterSpecs[i].Counter);
            }
            }
            frameTimings=FrameTimingManager.IsFeatureEnabled();
            pending.Append("# Видеокарта: ").Append(SystemInfo.graphicsDeviceType).Append(", время кадров видеокарты ")
                .Append(frameTimings?"измеряется":"недоступно (FrameTimingManager выключен или не поддержан)")
                .Append(counters==null?", счётчики отрисовки Unity только в отладочной сборке":missing.Count==0?"":", недоступные счётчики: "+string.Join(", ",missing)).Append('\n');
            if(!frameTimings || missing.Count>0)
                Debug.LogWarning($"Журнал медленных кадров: часть данных видеокарты недоступна: время кадров={(frameTimings?"есть":"нет")}, счётчики без данных: {(missing.Count==0?"нет":string.Join(", ",missing))}.");
        }

        // Editor benches: sections are timed without the player loop, file or summaries; TakeSections reads and
        // clears the accumulated totals.
        public static void MeasureOnly() {if(installed) return;installed=true;toMs=1000.0/Stopwatch.Frequency;}
        public static List<(string Name,double Milliseconds,int Calls)> TakeSections()
        {
            var result=new List<(string,double,int)>();
            for(int i=0;i<sectionIndex.Count;i++) if(sectionCalls[i]>0) result.Add((sectionNames[i],sectionMs[i],sectionCalls[i]));
            Array.Clear(sectionMs,0,sectionIndex.Count);Array.Clear(sectionCalls,0,sectionIndex.Count);
            return result;
        }

        public static Scope Section(string name)
        {
            if(!installed) return default;
            int index=SectionIndex(name);
            return index<0?default:new Scope(index,Stopwatch.GetTimestamp());
        }

        private static int SectionIndex(string name)
        {
            if(sectionIndex.TryGetValue(name,out int index)) return index;
            if(sectionIndex.Count>=MaxSections) return -1;
            index=sectionIndex.Count;sectionIndex.Add(name,index);sectionNames[index]=name;
            return index;
        }

        private static long subStart;
        private static void EndSub(int section)
        {
            if(section<0) return;
            sectionMs[section]+=(Stopwatch.GetTimestamp()-subStart)*toMs;sectionCalls[section]++;
        }

        public readonly struct Scope : IDisposable
        {
            private readonly int index;private readonly long started,allocated;
            private readonly bool baseline;
            internal Scope(int index,long started)
            {this.index=index;this.started=started;baseline=CpuBaselineCapture.Enabled;allocated=baseline?BaselineAllocatedBytes:0;}
            public void Dispose()
            {
                if(started==0) return;
                double elapsed=(Stopwatch.GetTimestamp()-started)*toMs;
                sectionMs[index]+=elapsed;sectionCalls[index]++;
                if(baseline && CpuBaselineCapture.Enabled)
                    CpuBaselineCapture.Section(sectionNames[index],elapsed,BaselineAllocatedBytes-allocated);
            }
        }

        private static void BeginPhase(int phase)
        {
            long now=Stopwatch.GetTimestamp();
            if(phase==0)
            {
                if(pendingBaselineFrames>0)
                {CpuBaselineCapture.Reset();CpuBaselineCapture.FrameLimit=pendingBaselineFrames;pendingBaselineFrames=0;CpuBaselineCapture.Enabled=true;}
                now=Stopwatch.GetTimestamp();
                frameStart=now;gcAtFrameStart=GC.CollectionCount(0);baselineFrameActive=CpuBaselineCapture.Enabled;
                baselineFrameAllocated=baselineFrameActive?BaselineAllocatedBytes:0;
            }
            phaseStart[phase]=now;
        }

        private static void EndPhase(int phase)
        {
            long now=Stopwatch.GetTimestamp();
            phaseMs[phase]+=(now-phaseStart[phase])*toMs;
            if(phase==phaseNames.Length-1) EndFrame(now);
        }

        private static void EndFrame(long now)
        {
            double total=frameStart==0?0:(now-frameStart)*toMs;
            double baselineWait=phaseMs[0];
            double time=Time.realtimeSinceStartupAsDouble;
            if(periodStart==0) periodStart=time;
            periodFrames.Add(total);
            ReadGpu();
            if(total>SlowMilliseconds) WriteSlow(total);
            (gpuCalls,previousGpuCalls)=(previousGpuCalls,gpuCalls);Array.Clear(gpuCalls,0,gpuIndex.Count);
            if(counters!=null) (counterValues,previousCounterValues)=(previousCounterValues,counterValues);
            for(int i=0;i<sectionIndex.Count;i++) periodSectionMs[i]+=sectionMs[i];
            note=null;
            Array.Clear(phaseMs,0,phaseMs.Length);
            Array.Clear(sectionMs,0,sectionIndex.Count);Array.Clear(sectionCalls,0,sectionIndex.Count);
            if(time-periodStart>=SummarySeconds) {WriteSummary(time);Flush();}
            if(CpuBaselineCapture.Enabled && baselineFrameActive)
            {
                double measured=(Stopwatch.GetTimestamp()-frameStart)*toMs;
                CpuBaselineCapture.Frame(measured,Math.Min(measured,baselineWait),BaselineAllocatedBytes-baselineFrameAllocated);
            }
        }

        private static void ReadGpu()
        {
            if(counters!=null) for(int i=0;i<counters.Length;i++) counterValues[i]=counters[i].Valid?counters[i].LastValue:-1;
            if(!frameTimings) return;
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timings)==0 || timings[0].frameStartTimestamp==lastTimingStamp) return;
            lastTimingStamp=timings[0].frameStartTimestamp;
            double gpu=timings[0].gpuFrameTime;
            if(gpu>periodGpuMax) periodGpuMax=gpu;
            if(gpu>SlowMilliseconds) periodGpuSlow++;
        }

        private static void AppendGpu(StringBuilder line)
        {
            line.Append(" | видеокарта:");
            if(frameTimings)
            {
                uint count=FrameTimingManager.GetLatestTimings((uint)timings.Length,timings);
                line.Append(" время последних кадров");
                for(int i=0;i<count;i++) line.Append(' ').Append(timings[i].gpuFrameTime.ToString("F1"));
                line.Append(" мс;");
            }
            AppendCalls(line," наше в этом кадре",gpuCalls);AppendCalls(line," наше в прошлом",previousGpuCalls);
            if(counters==null) return;
            AppendCounters(line," этот кадр",counterValues);AppendCounters(line," прошлый",previousCounterValues);
        }
        private static void AppendCalls(StringBuilder line,string label,int[] calls)
        {
            line.Append(label).Append(':');bool any=false;
            for(int i=0;i<gpuIndex.Count;i++) if(calls[i]>0) {line.Append(' ').Append(gpuNames[i]).Append('×').Append(calls[i]).Append(',');any=true;}
            line.Append(any?"":" нет").Append(';');
        }
        private static void AppendCounters(StringBuilder line,string label,long[] values)
        {
            line.Append(label).Append(':');
            for(int i=0;i<values.Length;i++)
            {
                if(values[i]<0) continue;
                line.Append(' ').Append(counterSpecs[i].Label).Append(' ')
                    .Append(counterSpecs[i].Bytes?(values[i]/1048576.0).ToString("F0")+" МБ":values[i].ToString());
            }
            line.Append(';');
        }

        private static void WriteSlow(double total)
        {
            var line=pending;
            line.Append(Time.realtimeSinceStartup.ToString("F1")).Append("с кадр ").Append(Time.frameCount)
                .Append(" всего ").Append(total.ToString("F1"));
            for(int i=0;i<phaseNames.Length;i++)
                if(phaseMs[i]>=1) line.Append(" | ").Append(phaseNames[i]).Append(' ').Append(phaseMs[i].ToString("F1"));
            for(int i=0;i<sectionIndex.Count;i++)
                if(sectionMs[i]>=1) line.Append(" | ").Append(sectionNames[i]).Append(' ').Append(sectionMs[i].ToString("F1"))
                    .Append(sectionCalls[i]>1?"×"+sectionCalls[i]:"");
            int gc=GC.CollectionCount(0)-gcAtFrameStart;
            if(gc>0) line.Append(" | gc ").Append(gc);
            if(note!=null) line.Append(" | ").Append(note);
            AppendGpu(line);
            if(!string.IsNullOrEmpty(Context)) line.Append(" | ").Append(Context);
            line.Append('\n');
        }

        private static void WriteSummary(double time)
        {
            periodFrames.Sort();
            int n=periodFrames.Count;double sum=0;foreach(double f in periodFrames) sum+=f;
            pending.Append("== ").Append(time.ToString("F0")).Append("с: кадров ").Append(n)
                .Append(", среднее ").Append((sum/Math.Max(1,n)).ToString("F1"))
                .Append(", 99% ").Append(periodFrames[Math.Min(n-1,(int)(n*.99))].ToString("F1"))
                .Append(", макс ").Append(periodFrames[n-1].ToString("F1"))
                .Append(", медленных ").Append(periodFrames.FindAll(f=>f>SlowMilliseconds).Count)
                .Append(", сборок мусора ").Append(GC.CollectionCount(0)-gcAtPeriodStart)
                .Append(", память ").Append((GC.GetTotalMemory(false)/1048576.0).ToString("F0")).Append(" МБ")
                .Append(frameTimings?", видеокарта макс "+periodGpuMax.ToString("F1")+" мс, кадров видеокарты >"+SlowMilliseconds+" мс "+periodGpuSlow:"")
                .Append(string.IsNullOrEmpty(Context)?"":" | "+Context).Append('\n');
            // Steady load: the costliest own sections per frame over the period (Unity phases excluded).
            var order=new List<int>();
            for(int i=0;i<sectionIndex.Count;i++) if(periodSectionMs[i]>0 && sectionNames[i].IndexOf('.')<0) order.Add(i);
            order.Sort((a,b)=>periodSectionMs[b].CompareTo(periodSectionMs[a]));
            pending.Append("   на кадр:");
            for(int k=0;k<Math.Min(8,order.Count);k++)
                pending.Append(' ').Append(sectionNames[order[k]]).Append(' ').Append((periodSectionMs[order[k]]/Math.Max(1,n)).ToString("F2")).Append(';');
            pending.Append('\n');
            // Unity's own subsystems per frame (rendering, scripts, physics...): where the rest of the frame goes.
            var engine=new List<int>();
            for(int i=0;i<sectionIndex.Count;i++) if(periodSectionMs[i]>0 && sectionNames[i].IndexOf('.')>=0) engine.Add(i);
            engine.Sort((a,b)=>periodSectionMs[b].CompareTo(periodSectionMs[a]));
            pending.Append("   движок на кадр:");
            for(int k=0;k<Math.Min(6,engine.Count);k++)
                pending.Append(' ').Append(sectionNames[engine[k]]).Append(' ').Append((periodSectionMs[engine[k]]/Math.Max(1,n)).ToString("F2")).Append(';');
            pending.Append('\n');
            Array.Clear(periodSectionMs,0,periodSectionMs.Length);
            periodFrames.Clear();periodStart=time;gcAtPeriodStart=GC.CollectionCount(0);periodGpuMax=0;periodGpuSlow=0;
        }

        private static void Flush()
        {
            if(pending.Length==0 || path==null) return;
            try {File.AppendAllText(path,pending.ToString(),Encoding.UTF8);}
            catch(Exception error) {Debug.LogWarning($"Журнал медленных кадров: запись в {path} не удалась: {error.Message}");}
            pending.Clear();
        }
    }
}
