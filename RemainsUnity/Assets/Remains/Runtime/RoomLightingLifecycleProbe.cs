using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Explicit synthetic coroutine diagnostic. No actor, canonical world or save fixture is created.
    [DefaultExecutionOrder(10000)]
    public sealed class RoomLightingLifecycleProbe : MonoBehaviour
    {
        [Serializable] private sealed class PendingWitness
        {
            public string stage,room;
            public int requestedFrame,observedFrame,width,height,sources;
            public double requestedAt,observedAt;
            public bool isReady,isUpdating,shaderDisabled;
        }
        [Serializable] private sealed class Report
        {
            public bool success,explicitSyntheticFixture=true,normalHubInitialReady,noWorldOrActor,
                rebindGpuVerified,lateCompletionGuardVerified,releaseGuardVerified,destroyGuardVerified,
                reentrantCallbackObserved,reentrantPendingObserved,reentrantReleaseGuardVerified,finalGpuVerified;
            public string detail,stage;
            public double elapsedSeconds;
            public PendingWitness[] pending;
            public RoomLightingGpuResult[] gpu;
            public string[] errors;
            public string limitation="Синтетические комнаты проверяют coroutine/Task отмену, повторную привязку и actual GPU-пару. Исходный runtime и обычный игровой маршрут здесь не проверяются.";
        }
        private PrototypeRuntime runtime;
        private RoomLighting normal,test,destroyed;
        private GameObject testObject,destroyObject;
        private Action<RoomLighting> reentrant;
        private readonly List<PendingWitness> pending=new();
        private readonly List<RoomLightingGpuResult> gpu=new();
        private readonly List<string> errors=new();
        private readonly Report report=new();
        private double began,deadline;
        private bool finished;
        private string stage="initialize";

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;began=Time.realtimeSinceStartupAsDouble;deadline=began+30;
            Application.logMessageReceived+=OnLog;
            try
            {
                Require(value!=null && value.Options.Smoke?.StartsWith("lighting-lifecycle",StringComparison.Ordinal)==true &&
                    value.Options.Role==null && !string.IsNullOrEmpty(value.Options.SaveDirectory) && !string.IsNullOrEmpty(value.Options.Report),
                    "Нужны lighting-lifecycle, отдельный --save-dir, --report и отсутствие --role.");
                RequireNoWorld();StartCoroutine(Drive());
            }
            catch(Exception error) {Finish(false,error.ToString());}
        }
        // Pump the diagnostic iterator explicitly so nested GPU evidence exceptions reach the report.
        private IEnumerator Drive()
        {
            var execution=Run();
            try
            {
                while(!finished)
                {
                    bool moved=false;Exception failure=null;
                    try {Check();moved=execution.MoveNext();}
                    catch(Exception error) {failure=error;}
                    if(failure!=null) {Finish(false,failure.ToString());yield break;}
                    if(!moved) {Finish(true,"Actual asynchronous binding/release/destroy/reentrant guards и GPU-пары проверены.");yield break;}
                    yield return execution.Current;
                }
            }
            finally {(execution as IDisposable)?.Dispose();}
        }
        private IEnumerator Run()
        {
            stage="normal-hub-ready";
            while(runtime.ActiveRoomLighting==null || !runtime.ActiveRoomLighting.IsReady || runtime.ActiveRoomLighting.IsUpdating)
            {
                if(runtime.ActiveRoomLighting?.Failure!=null) throw runtime.ActiveRoomLighting.Failure;
                yield return null;Check();
            }
            normal=runtime.ActiveRoomLighting;
            Require(normal.Room!=null && normal.ForegroundMap!=null && normal.BackgroundMap!=null,"Initial hub не опубликовал пару карт.");
            report.normalHubInitialReady=true;normal.Release();
            Require(Shader.GetGlobalFloat("_WorldLightEnabled")==0,"Release initial hub не выключил shader.");
            testObject=new GameObject("Диагностика асинхронной привязки света");test=testObject.AddComponent<RoomLighting>();

            stage="pending-rebind";
            var first=LargeRoom("synthetic-pending-before-rebind");
            var witness=StartPending(test,first);
            yield return null;ObservePending(test,first,witness);
            var second=SmallRoom("synthetic-small-2",10,20);var secondProfile=SmallProfile(.23f);
            test.InitializeAsync(second,secondProfile);
            while(!Settled(test,second)) {yield return null;Check();}
            stage="rebind-gpu";
            var compare=RoomLightingGpuEvidence.Compare(test,()=>Current(test,second),result=>gpu.Add(result),(float)(Time.unscaledTime+10));
            try {while(compare.MoveNext()) {yield return compare.Current;Check();}}
            finally {(compare as IDisposable)?.Dispose();}
            Require(gpu.Count==1,"Нет actual GPU результата smallRoom2.");report.rebindGpuVerified=true;
            stage="old-completion-grace";
            var firstGpu=gpu[0];double grace=Time.realtimeSinceStartupAsDouble+2;
            while(Time.realtimeSinceStartupAsDouble<grace)
            {RoomLightingGpuEvidence.EnsureCurrent(firstGpu,()=>Current(test,second));yield return null;Check();}
            report.lateCompletionGuardVerified=true;

            stage="pending-release";
            var releaseRoom=LargeRoom("synthetic-pending-release");witness=StartPending(test,releaseRoom);
            yield return null;ObservePending(test,releaseRoom,witness);test.Release();
            grace=Time.realtimeSinceStartupAsDouble+1.5;
            while(Time.realtimeSinceStartupAsDouble<grace)
            {RequireReleased(test);yield return null;Check();}
            report.releaseGuardVerified=true;

            stage="pending-destroy";
            destroyObject=new GameObject("Диагностика уничтожения незавершённого света");destroyed=destroyObject.AddComponent<RoomLighting>();
            var destroyRoom=LargeRoom("synthetic-pending-destroy");witness=StartPending(destroyed,destroyRoom);
            yield return null;ObservePending(destroyed,destroyRoom,witness);
            UnityEngine.Object.Destroy(destroyObject);yield return null;Check();
            Require(destroyed==null,"Pending компонент не был уничтожен в следующем кадре.");
            grace=Time.realtimeSinceStartupAsDouble+1.5;
            while(Time.realtimeSinceStartupAsDouble<grace)
            {Require(Shader.GetGlobalFloat("_WorldLightEnabled")==0,"Поздний worker воскресил shader после Destroy.");yield return null;Check();}
            report.destroyGuardVerified=true;

            stage="reentrant-ready-rebind";
            var trigger=SmallRoom("synthetic-reentrant-trigger",-4,-3);
            var reentered=LargeRoom("synthetic-reentrant-pending");PendingWitness reentryWitness=null;
            reentrant=source=>
            {
                if(!source.IsReady || !ReferenceEquals(source.Room,trigger) || report.reentrantCallbackObserved) return;
                report.reentrantCallbackObserved=true;
                // Same public component, synchronously inside its real readiness notification.
                reentryWitness=StartPending(source,reentered);
            };
            test.ReadinessChanged+=reentrant;test.InitializeAsync(trigger,SmallProfile(.61f));
            while(!report.reentrantCallbackObserved) {yield return null;Check();}
            yield return null;ObservePending(test,reentered,reentryWitness);
            report.reentrantPendingObserved=true;
            test.ReadinessChanged-=reentrant;reentrant=null;
            stage="reentrant-pending-release";test.Release();
            grace=Time.realtimeSinceStartupAsDouble+1.5;
            while(Time.realtimeSinceStartupAsDouble<grace)
            {RequireReleased(test);yield return null;Check();}
            report.reentrantReleaseGuardVerified=true;

            stage="final-small-gpu";
            var final=SmallRoom("synthetic-final-binding",31,2);
            test.InitializeAsync(final,SmallProfile(.47f));
            while(!Settled(test,final)) {yield return null;Check();}
            compare=RoomLightingGpuEvidence.Compare(test,()=>Current(test,final),result=>gpu.Add(result),(float)(Time.unscaledTime+10));
            try {while(compare.MoveNext()) {yield return compare.Current;Check();}}
            finally {(compare as IDisposable)?.Dispose();}
            Require(gpu.Count==2,"Нет actual GPU результата final binding.");
            grace=Time.realtimeSinceStartupAsDouble+1;
            while(Time.realtimeSinceStartupAsDouble<grace)
            {RoomLightingGpuEvidence.EnsureCurrent(gpu[1],()=>Current(test,final));yield return null;Check();}
            report.finalGpuVerified=true;
        }
        private PendingWitness StartPending(RoomLighting source,RoomLayout room)
        {
            var result=new PendingWitness {stage=stage,room=room.id,requestedFrame=Time.frameCount,
                requestedAt=Time.realtimeSinceStartupAsDouble-began,width=480,height=480,sources=16};
            source.InitializeAsync(room,LargeProfile());
            Require(!source.IsReady && source.IsUpdating && ReferenceEquals(source.Room,room),"InitializeAsync не дал pending binding: "+room.id);
            pending.Add(result);return result;
        }
        private void ObservePending(RoomLighting source,RoomLayout room,PendingWitness result)
        {
            Check();Require(result!=null && Time.frameCount>result.requestedFrame && source!=null &&
                ReferenceEquals(source.Room,room) && !source.IsReady && source.IsUpdating,
                "Настоящий pending frame не наблюдался; fixture нельзя считать доказательством отмены: "+room.id);
            result.observedFrame=Time.frameCount;result.observedAt=Time.realtimeSinceStartupAsDouble-began;
            result.isReady=source.IsReady;result.isUpdating=source.IsUpdating;
            result.shaderDisabled=Shader.GetGlobalFloat("_WorldLightEnabled")==0;
            Require(result.shaderDisabled,"Pending binding оставил предыдущий shader включённым: "+room.id);
        }
        private bool Settled(RoomLighting source,RoomLayout room)
        {
            Check();if(source?.Failure!=null) throw source.Failure;
            Require(source!=null && ReferenceEquals(source.Room,room),"Потеряна ожидаемая привязка: "+room.id);
            return source.IsReady && !source.IsUpdating;
        }
        private static bool Current(RoomLighting source,RoomLayout room)=>source!=null && source.IsReady && !source.IsUpdating &&
            source.Failure==null && ReferenceEquals(source.Room,room);
        private static void RequireReleased(RoomLighting source)
        {
            Require(source!=null && source.Room==null && !source.IsReady && !source.IsUpdating &&
                source.ForegroundMap==null && source.BackgroundMap==null && Shader.GetGlobalFloat("_WorldLightEnabled")==0,
                "Release pending binding оставил работу/карты либо поздно воскресил shader.");
        }
        private void Check()
        {
            Require(Time.realtimeSinceStartupAsDouble<=deadline,"Таймаут 30 с: "+stage);
            RequireNoWorld();
            if(errors.Count>0) throw new InvalidOperationException("Ошибка runtime: "+errors[0]);
            if(test!=null && test.Failure!=null) throw test.Failure;
            if(destroyed!=null && destroyed.Failure!=null) throw destroyed.Failure;
        }
        private void RequireNoWorld()
        {
            Require(runtime.Session.World==null && runtime.Session.LocalActor==null,"Синтетическая проверка не допускает мир или игрока.");
            report.noWorldOrActor=true;
        }
        private void OnLog(string message,string trace,LogType type)
        {if(!finished && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert) && errors.Count<8) errors.Add(message+"\n"+trace);}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            report.success=success;report.detail=detail;report.stage=stage;
            report.elapsedSeconds=Time.realtimeSinceStartupAsDouble-began;report.pending=pending.ToArray();report.gpu=gpu.ToArray();report.errors=errors.ToArray();
            Cleanup();Application.logMessageReceived-=OnLog;
            try
            {
                string path=Path.GetFullPath(runtime.Options.Report);Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,StateJson.ToJson(report,true));
            }
            catch(Exception error) {success=false;Debug.LogError("Не записан lifecycle отчёт: "+error);}
            if(success) Debug.Log("Асинхронный lifecycle света: PASS, синтетическая coroutine/GPU проверка.");
            else Debug.LogError("Асинхронный lifecycle света: FAIL, stage="+stage+", reason="+detail);
            Application.Quit(success?0:1);
        }
        private void Cleanup()
        {
            if(test!=null) {if(reentrant!=null) test.ReadinessChanged-=reentrant;test.Release();}
            if(destroyed!=null) destroyed.Release();
            if(testObject!=null) UnityEngine.Object.Destroy(testObject);
            if(destroyObject!=null) UnityEngine.Object.Destroy(destroyObject);
        }
        private void OnDestroy() {Application.logMessageReceived-=OnLog;Cleanup();}
        private static RoomLayout LargeRoom(string id)
        {
            var solids=new RoomSurface[8];
            for(int index=0;index<solids.Length;index++) solids[index]=new RoomSurface {x=100+index*3,y=100,width=1,height=1};
            return new RoomLayout {id=id,left=0,right=60,bottom=0,top=60,platforms=solids,
                artwork=Array.Empty<RoomArtwork>(),ladders=Array.Empty<RoomLadder>()};
        }
        private static RoomLightProfile LargeProfile()
        {
            var lights=new RoomLight[16];
            for(int index=0;index<lights.Length;index++) lights[index]=new RoomLight {id="synthetic-radial-"+index,
                x=10+index*2,y=30,radius=120,intensity=.08f,backgroundIntensity=.06f};
            return new RoomLightProfile {ambient=new Color(.04f,.03f,.02f),backgroundAmbientScale=.5f,lights=lights};
        }
        private static RoomLayout SmallRoom(string id,float x,float y)=>new() {id=id,left=x,right=x+3.125f,bottom=y,top=y+2.375f,
            artwork=Array.Empty<RoomArtwork>(),ladders=Array.Empty<RoomLadder>(),
            platforms=new[]{new RoomSurface {x=x+1.5f,y=y+.5f,width=.125f,height=1.5f}}};
        private static RoomLightProfile SmallProfile(float amount)=>new() {ambient=new Color(.13f,.19f,.23f),backgroundAmbientScale=.41f,
            lights=new[]{new RoomLight {id="synthetic-small-light",x=0,y=0,radius=120,intensity=amount,backgroundIntensity=amount*.37f,
                color=new Color(.9f,.6f,.3f)}}};
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Lifecycle света: "+reason);}
    }
}
