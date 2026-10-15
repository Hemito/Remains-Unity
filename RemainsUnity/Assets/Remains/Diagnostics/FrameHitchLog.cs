using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
    public static class FrameHitchLog
    {
        public const double SlowMilliseconds=50;
        private const double SummarySeconds=10;
        private const int MaxSections=256;

        private static readonly string[] phaseNames={"Initialization","EarlyUpdate","FixedUpdate","PreUpdate","Update","PreLateUpdate","PostLateUpdate"};
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
        private static int gcAtFrameStart;
        private static double periodStart;
        private static string path;
        private static bool installed;
        private static double toMs;

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
            pending.Append("# Журнал медленных кадров (>").Append(SlowMilliseconds).Append(" мс). Фазы Unity и участки кода в мс; gc — сборки мусора за кадр.\n");
            pending.Append("# ").Append(Application.version).Append(", ").Append(SystemInfo.processorType).Append(", ").Append(SystemInfo.graphicsDeviceName).Append('\n');
            Application.quitting+=Flush;
            Debug.Log("Журнал медленных кадров: "+path);
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
            private readonly int index;private readonly long started;
            internal Scope(int index,long started) {this.index=index;this.started=started;}
            public void Dispose()
            {
                if(started==0) return;
                sectionMs[index]+=(Stopwatch.GetTimestamp()-started)*toMs;sectionCalls[index]++;
            }
        }

        private static void BeginPhase(int phase)
        {
            long now=Stopwatch.GetTimestamp();
            if(phase==0) {frameStart=now;gcAtFrameStart=GC.CollectionCount(0);}
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
            double time=Time.realtimeSinceStartupAsDouble;
            if(periodStart==0) periodStart=time;
            periodFrames.Add(total);
            if(total>SlowMilliseconds) WriteSlow(total);
            for(int i=0;i<sectionIndex.Count;i++) periodSectionMs[i]+=sectionMs[i];
            note=null;
            Array.Clear(phaseMs,0,phaseMs.Length);
            Array.Clear(sectionMs,0,sectionIndex.Count);Array.Clear(sectionCalls,0,sectionIndex.Count);
            if(time-periodStart>=SummarySeconds) {WriteSummary(time);Flush();}
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
            periodFrames.Clear();periodStart=time;
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
