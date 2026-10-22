using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    // Opt-in diagnostic only. Requests presentation windows; never writes actor/world state.
    internal sealed class Seed104LightWindowRoute : IDisposable
    {
        [Serializable] public sealed class Sample
        {
            public string fromWindow,targetWindow,committedWindow;
            public int index,prefetchFrame,commitRequestFrame,committedFrame,oldFieldId,newFieldId,preparingObservedFrames;
            public double prefetchSeconds,commitRequestSeconds,committedSeconds,leadSeconds,commitDelaySeconds;
            public long initialRenderTextureBytes,peakObservedRenderTextureBytes,committedRenderTextureBytes,peakObservedSpareBytes;
        }
        [Serializable] public struct FrameSample
        {
            public int frame,step,activeFieldId;public bool preparing,newGpuTiming;
            public double seconds,gpuLastCompletedMs;public long renderTextureBytes,spareBytes;
        }
        private readonly List<FrameSample> frames=new();
        private readonly FrameTiming[] gpuTiming=new FrameTiming[1];
        private ulong lastGpuTimestamp;
        public FrameSample[] Frames=>frames.ToArray();
        private readonly PrototypeRuntime runtime;
        private readonly FactoryLevelView view;
        private readonly FactoryLightFocus focus;
        private readonly bool focusWasEnabled;
        private readonly List<Sample> samples=new();
        private readonly List<Vector2> targets=new();
        private readonly List<RectInt> windows=new();
        private Sample current;
        private int step;
        private bool disposed;
        private double finishedAt;
        public Sample[] Samples=>samples.ToArray();

        public Seed104LightWindowRoute(PrototypeRuntime runtime)
        {
            this.runtime=runtime;
            view=runtime.ActiveFactoryLighting;
            if(view==null || !view.IsReady || view.IsPreparingWindow) throw new InvalidOperationException("Свет-маршрут: нужен готовый завод без кандидата.");
            focus=view.GetComponent<FactoryLightFocus>();
            if(focus==null) throw new InvalidOperationException("Свет-маршрут: отсутствует авторский FactoryLightFocus.");
            Discover(runtime.Geometry.Get(runtime.Session.LocalActor.level));
            focusWasEnabled=focus.enabled;
            Debug.LogWarning("Свет-маршрут: FactoryLightFocus временно отключён только для диагностики; явные запросы окон не проверяют движение камеры или персонажа.",view);
            focus.enabled=false;
        }
        private static string Key(RectInt r)=>$"{r.x},{r.y},{r.width},{r.height}";
        private void Discover(RoomLayout layout)
        {
            if(layout==null || !(layout.right>layout.left) || !(layout.top>layout.bottom))
                throw new InvalidOperationException("Свет-маршрут: некорректные границы геометрии.");
            var points=new Dictionary<RectInt,Vector2>();
            // Explicit one-time diagnostic discovery, outside the measured capture.
            float spacing=Mathf.Min(2,Mathf.Min((layout.right-layout.left)/16,(layout.top-layout.bottom)/64));
            if(spacing<.1f) throw new InvalidOperationException("Свет-маршрут: слишком малые границы полного завода.");
            for(float y=layout.bottom+.01f;y<layout.top;y+=spacing)
                for(float x=layout.left+.01f;x<layout.right;x+=spacing)
                {
                    Vector2 world=view.transform.TransformPoint(new Vector3(x,y,0));
                    RectInt window=view.WindowFor(world);
                    if(!points.ContainsKey(window)) points.Add(window,world);
                }
            RectInt initial=view.LightWindowRooms;initialWindow=initial;
            if(!points.ContainsKey(initial)) throw new InvalidOperationException("Свет-маршрут: начальное окно не найдено в геометрии: "+Key(initial));
            var ordered=new List<RectInt>(points.Keys);
            ordered.Sort((a,b)=>a.y!=b.y?a.y.CompareTo(b.y):a.x.CompareTo(b.x));
            var path=new List<RectInt>{initial};
            if(!FindPath(ordered,path,6)) throw new InvalidOperationException("Свет-маршрут: нет цепочки шести различных соседних окон.");
            for(int i=1;i<path.Count;i++) {windows.Add(path[i]);targets.Add(points[path[i]]);}
            windows.Add(initial);targets.Add(points[initial]); // Deliberately direct return, not a seventh adjacent boundary.
            Debug.Log($"Свет-маршрут: найдено {points.Count} окон, шесть соседних переходов и прямой возврат; начало={Key(initial)}.");
        }
        private static bool FindPath(List<RectInt> ordered,List<RectInt> path,int remaining)
        {
            if(remaining==0) return true;
            RectInt last=path[path.Count-1];
            foreach(RectInt next in ordered)
            {
                if(next.size!=last.size || Math.Abs(next.x-last.x)+Math.Abs(next.y-last.y)!=1 || path.Contains(next)) continue;
                path.Add(next);
                if(FindPath(ordered,path,remaining-1)) return true;
                path.RemoveAt(path.Count-1);
            }
            return false;
        }
        public bool Tick()
        {
            if(disposed) throw new ObjectDisposedException(nameof(Seed104LightWindowRoute));
            if(view==null || !ReferenceEquals(runtime.ActiveFactoryLighting,view) || !view.IsReady || view.Failure!=null || runtime.Session.Travel.Pending)
                throw new InvalidOperationException("Свет-маршрут: вид уровня заменён, не готов или сообщил ошибку: "+view?.Failure);
            double now=Time.realtimeSinceStartupAsDouble;
            bool newGpu=false;double gpuMs=0;
            if(FrameTimingManager.GetLatestTimings(1,gpuTiming)>0 && gpuTiming[0].frameStartTimestamp!=lastGpuTimestamp)
            {lastGpuTimestamp=gpuTiming[0].frameStartTimestamp;gpuMs=gpuTiming[0].gpuFrameTime;newGpu=gpuMs>0;}
            frames.Add(new FrameSample {frame=Time.frameCount,step=step,activeFieldId=view.LightFieldInstanceId,
                preparing=view.IsPreparingWindow,seconds=now,renderTextureBytes=LightTextureBudget.Bytes,
                spareBytes=RoomRadianceField.SpareTextureBytes,newGpuTiming=newGpu,gpuLastCompletedMs=gpuMs});
            if(step==windows.Count) return now-finishedAt>=2; // Include the last commit's following work frames.
            if(current==null)
            {
                current=new Sample {index=step,fromWindow=Key(view.LightWindowRooms),targetWindow=Key(windows[step]),
                    prefetchFrame=Time.frameCount,prefetchSeconds=now,oldFieldId=view.LightFieldInstanceId,
                    initialRenderTextureBytes=LightTextureBudget.Bytes};
                samples.Add(current);
                view.RequestLightWindow(targets[step],false);
                Debug.Log($"Свет-маршрут prefetch: шаг={step}, frame={Time.frameCount}, окно={current.targetWindow}, поле={current.oldFieldId}, RT={LightTextureBudget.Bytes}.");
            }
            current.peakObservedRenderTextureBytes=Math.Max(current.peakObservedRenderTextureBytes,LightTextureBudget.Bytes);
            current.peakObservedSpareBytes=Math.Max(current.peakObservedSpareBytes,RoomRadianceField.SpareTextureBytes);
            if(view.IsPreparingWindow) current.preparingObservedFrames++;
            if(LightTextureBudget.Bytes>LightTextureBudget.MaximumBytes) throw new InvalidOperationException("Свет-маршрут: превышен агрегатный RT-бюджет.");
            if(current.commitRequestFrame==0)
            {
                if(view.LightWindowRooms!=ExpectedCurrentWindow() || view.LightFieldInstanceId!=current.oldFieldId) throw new InvalidOperationException("Свет-маршрут: окно опубликовано раньше commit-запроса.");
                if(now-current.prefetchSeconds<4) return false;
                current.commitRequestFrame=Time.frameCount;current.commitRequestSeconds=now;current.leadSeconds=now-current.prefetchSeconds;
                view.RequestLightWindow(targets[step],true);
                Debug.Log($"Свет-маршрут запрос commit: шаг={step}, frame={Time.frameCount}, lead={current.leadSeconds:F3} с, окно={current.targetWindow}.");
            }
            now=Time.realtimeSinceStartupAsDouble;
            if(view.LightWindowRooms==windows[step])
            {
                if(view.LightFieldInstanceId==current.oldFieldId) throw new InvalidOperationException("Свет-маршрут: окно сменилось без смены поля.");
                current.committedFrame=Time.frameCount;current.committedSeconds=now;current.commitDelaySeconds=now-current.commitRequestSeconds;
                current.newFieldId=view.LightFieldInstanceId;current.committedWindow=Key(view.LightWindowRooms);current.committedRenderTextureBytes=LightTextureBudget.Bytes;
                Debug.Log($"Свет-маршрут commit: шаг={step}, frame={Time.frameCount}, задержка={current.commitDelaySeconds:F3} с, окно={current.committedWindow}, поле={current.newFieldId}, RT={LightTextureBudget.Bytes}, RTpeakObserved={current.peakObservedRenderTextureBytes}, preparingFrames={current.preparingObservedFrames}.");
                current=null;step++;if(step==windows.Count) finishedAt=now;
            }
            else if(now-current.commitRequestSeconds>30) throw new TimeoutException($"Свет-маршрут: commit не завершён за 30 с, шаг={step}, цель={current.targetWindow}, фактически={Key(view.LightWindowRooms)}.");
            return false;
        }
        private RectInt ExpectedCurrentWindow()=>step==0?initialWindow:windows[step-1];
        private RectInt initialWindow;
        public void Dispose()
        {
            if(disposed) return;disposed=true;
            if(view!=null) view.CancelWindowPreparation();
            if(focus!=null) focus.enabled=focusWasEnabled;
        }
    }
}
