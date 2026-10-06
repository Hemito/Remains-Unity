using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Remains.Diagnostics
{
    [Serializable] public struct TransitionTimingSample
    {
        public long sequence,parentSequence;
        public int frame;
        public string name,context;
        public double milliseconds;
    }

    // Opt-in event diagnostics on the main thread. No logging, I/O or allocations when disabled.
    public static class TransitionTiming
    {
        public const int Capacity=8192;
        private static TransitionTimingSample[] samples;
        private static int thread,count,activeIndex=-1;
        private static long epoch;
        private static bool enabled;

        public static bool Enabled
        {
            get=>enabled;
            set
            {
                if(value==enabled) return;
                RequireThread();
                if(value && samples==null) samples=new TransitionTimingSample[Capacity];
                enabled=value;
            }
        }
        public static void Reset()
        {
            RequireThread();
            if(activeIndex!=-1) throw new InvalidOperationException("Диагностика перехода: Reset внутри незавершённого scope.");
            if(count>0) Array.Clear(samples,0,count);
            count=0;epoch++;
        }
        public static Scope Measure(string name,string context=null)
        {
            if(!enabled) return default;
            RequireThread();
            if(string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Диагностика перехода: пустое имя замера.",nameof(name));
            if(count>=Capacity) throw new InvalidOperationException($"Диагностика перехода переполнена: limit={Capacity}, name={name}, context={context}.");
            int index=count++;
            samples[index]=new TransitionTimingSample {sequence=index+1,parentSequence=activeIndex+1,frame=Time.frameCount,
                name=name,context=context,milliseconds=double.NaN};
            activeIndex=index;
            return new Scope(index,epoch,Stopwatch.GetTimestamp());
        }
        public static TransitionTimingSample[] ToArray()
        {
            RequireThread();
            if(activeIndex!=-1) throw new InvalidOperationException("Диагностика перехода: snapshot внутри незавершённого scope: "+samples[activeIndex].name);
            if(count==0) return Array.Empty<TransitionTimingSample>();
            var result=new TransitionTimingSample[count];Array.Copy(samples,result,count);return result;
        }
        private static void RequireThread()
        {
            int current=Thread.CurrentThread.ManagedThreadId;
            if(thread==0) thread=current;
            else if(thread!=current) throw new InvalidOperationException($"Диагностика перехода разрешена только на основном потоке: expected={thread}, actual={current}.");
        }
        private static void Complete(int index,long capturedEpoch,long startTicks)
        {
            RequireThread();
            if(capturedEpoch!=epoch || index!=activeIndex || index<0 || index>=count || !double.IsNaN(samples[index].milliseconds))
                throw new InvalidOperationException($"Диагностика перехода: нарушен LIFO или scope уже завершён/сброшен: sequence={index+1}, activeSequence={activeIndex+1}.");
            samples[index].milliseconds=(Stopwatch.GetTimestamp()-startTicks)*1000.0/Stopwatch.Frequency;
            activeIndex=(int)samples[index].parentSequence-1;
        }
        public struct Scope : IDisposable
        {
            private readonly int index;
            private readonly long capturedEpoch,startTicks;
            private bool active;
            internal Scope(int index,long epoch,long ticks)
            {this.index=index;capturedEpoch=epoch;startTicks=ticks;active=true;}
            public void Dispose()
            {
                if(!active) return;
                Complete(index,capturedEpoch,startTicks);active=false;
            }
        }
    }
}
