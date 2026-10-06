using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    [Serializable] public sealed class RoomLight
    {
        public string id,fixture,mask;
        public bool shadow;
        public float backgroundIntensity=-1;
        public float maskX,maskY,maskWidth,maskHeight;
        public float x,y,radius,intensity,width,height;
        public Color color=Color.white;
        [NonSerialized] public Texture2D maskTexture;
        public RoomLight ForBackground()
        {
            if(!float.IsFinite(backgroundIntensity) || backgroundIntensity<0 && backgroundIntensity!= -1)
                throw new InvalidOperationException($"Источник {id}: яркость фона должна быть неотрицательной или -1 (общая).");
            var copy=(RoomLight)MemberwiseClone();
            if(backgroundIntensity>=0) copy.intensity=backgroundIntensity;
            return copy;
        }
    }
    [Serializable] public sealed class RoomLightProfile
    {
        public Color ambient;
        public float backgroundAmbientScale=1;
        public RoomLight[] lights;
        public RoomLightProfile ForBackground()
        {
            if(!float.IsFinite(backgroundAmbientScale) || backgroundAmbientScale<0 || lights==null)
                throw new InvalidOperationException("Некорректный профиль фонового света: проверьте множитель и источники.");
            return new RoomLightProfile {ambient=ambient*backgroundAmbientScale,lights=Array.ConvertAll(lights,light=>light.ForBackground())};
        }
    }
    public sealed class RoomLighting : MonoBehaviour
    {
        private static RoomLighting active;
        private Texture2D map,backgroundMap,stagingMap,stagingBackground;
        private Color[] foregroundPixels,backgroundPixels;
        private RoomLightMaps prepared;
        private Coroutine updates;
        private CancellationTokenSource cancellation;
        private Task<UpdateResult> worker;
        private Task<RoomLightMaps> initialWorker;
        private System.Diagnostics.Stopwatch initialTimer;
        private bool asynchronous;
        private long binding;
        private Request pending;
        private long requestedRevision;
        public event Action<RoomLighting> ReadinessChanged;
        public event Action<RoomLighting,Exception> Failed;
        public RoomLightProfile Profile {get;private set;}
        public RoomLayout Room {get;private set;}
        public Rect Bounds {get;private set;}
        public Texture2D ForegroundMap=>map;
        public Texture2D BackgroundMap=>backgroundMap;
        public bool IsReady {get;private set;}
        public Exception Failure {get;private set;}
        public bool IsUpdating=>updates!=null || initialWorker!=null || worker!=null || pending!=null;
        public long AppliedOcclusionRevision {get;private set;}
        public int UniqueDecodedTextures=>prepared?.UniqueDecodedTextures??0;
        public long DecodedPixels=>prepared?.DecodedPixels??0;
        public double InitialBuildMilliseconds {get;private set;}
        public double InitialPrepareMilliseconds {get;private set;}
        public double InitialGetPixelsMilliseconds {get;private set;}
        public double InitialDecodeMilliseconds {get;private set;}
        public double InitialEvaluateMilliseconds {get;private set;}
        public double InitialCpuMilliseconds=>InitialDecodeMilliseconds+InitialEvaluateMilliseconds;
        public double InitialUploadMilliseconds {get;private set;}
        public int LastCandidatePixels {get;private set;}
        public int LastDirtyPixels {get;private set;}
        public double LastCpuMilliseconds {get;private set;}
        public double LastUploadMilliseconds {get;private set;}
        public double LastLatencyMilliseconds {get;private set;}
        public double LastUpdateMilliseconds=>LastCpuMilliseconds+LastUploadMilliseconds;
        public void Initialize(RoomLayout room)
        {
            string path=room.lightingProfile;
            if(string.IsNullOrEmpty(path)) throw new InvalidOperationException("Не задан профиль освещения: "+room.id);
            var asset=Resources.Load<TextAsset>(path);
            if(asset==null) throw new InvalidOperationException("Нет описания света: "+path);
            Profile=JsonUtility.FromJson<RoomLightProfile>(asset.text);
            if(Profile?.lights==null) throw new InvalidOperationException("Нет источников света: "+path);
            Initialize(room,Profile);
        }
        public void Initialize(RoomLayout room,RoomLightProfile profile)
        {
            Begin(room,profile,false);
            try
            {
                prepared=new RoomLightMaps(room,Bounds,Profile);PublishInitial();
            }
            catch {Release();throw;}
        }
        // Runtime preparation captures assets once on the main thread, then evaluates frozen
        // CPU data in a worker. Editor/previews retain the explicit synchronous entry point.
        public void InitializeAsync(RoomLayout room,RoomLightProfile profile)
        {
            Begin(room,profile,true);
            try
            {
                var plan=RoomLightMaps.PrepareBuild(room,Bounds,Profile);
                InitialPrepareMilliseconds=plan.PrepareMilliseconds;InitialGetPixelsMilliseconds=plan.GetPixelsMilliseconds;
                cancellation=new CancellationTokenSource();var token=cancellation.Token;
                initialWorker=Task.Run(()=>plan.Build(token),token);
                updates=StartCoroutine(Rebuilds());
            }
            catch(Exception error) {Fail(error);}
        }
        private void Begin(RoomLayout room,RoomLightProfile profile,bool useWorker)
        {
            if(room==null) throw new ArgumentNullException(nameof(room));
            Release();asynchronous=useWorker;initialTimer=System.Diagnostics.Stopwatch.StartNew();
            Profile=profile??throw new InvalidOperationException("Нет профиля света: "+room.id);Room=room;Failure=null;
            if(room.artwork==null)
                Debug.LogWarning($"Комната {room.id}: нет artwork; для старой диагностической геометрии используется диапазон света 0–7. Проверьте описание комнаты.",this);
            float bottom=room.artwork==null?0:room.bottom,top=room.artwork==null?7:room.top;
            Bounds=new Rect(room.left,bottom,room.right-room.left,top-bottom);
            requestedRevision=AppliedOcclusionRevision=0;
            InitialBuildMilliseconds=InitialPrepareMilliseconds=InitialGetPixelsMilliseconds=InitialDecodeMilliseconds=InitialEvaluateMilliseconds=InitialUploadMilliseconds=0;
            LastCpuMilliseconds=LastUploadMilliseconds=LastLatencyMilliseconds=0;LastCandidatePixels=LastDirtyPixels=0;
            active=this;Shader.SetGlobalFloat("_WorldLightEnabled",0);
        }
        // Called from world/geometry events, never from a frame-time geometry scan.
        public void Synchronize(RoomLayout room,long revision)
        {
            if(!ReferenceEquals(Room,room))
            {if(asynchronous) InitializeAsync(room,Profile);else Initialize(room,Profile);}
            RequestOcclusionUpdate(room,revision);
        }
        public void RequestOcclusionUpdate(RoomLayout room,long revision)
        {
            if(room==null || !ReferenceEquals(Room,room) || revision<0)
                throw new InvalidOperationException($"Свет привязан к другой геометрии: bound={Room?.id}, requested={room?.id}, revision={revision}.");
            if(Failure!=null) return;
            if(revision<=requestedRevision) return;
            pending=new Request {revision=revision,occlusion=RoomLightMaps.CaptureOcclusion(room.platforms),started=Time.realtimeSinceStartupAsDouble};
            requestedRevision=revision;
            // Preserve the full first build when a door changes. Reconcile the latest snapshot
            // before the first publication rather than restarting every multi-second build.
            if(initialWorker==null) cancellation?.Cancel();
            if(updates==null) updates=StartCoroutine(Rebuilds());
        }
        private IEnumerator Rebuilds()
        {
            long run=binding;
            // Ensure StartCoroutine has returned its handle before this iterator can complete.
            yield return null;
            if(initialWorker!=null)
            {
                var task=initialWorker;var bound=Room;
                while(!task.IsCompleted) yield return null;
                initialWorker=null;cancellation.Dispose();cancellation=null;
                if(task.IsCanceled) {if(run==binding) updates=null;yield break;}
                if(task.IsFaulted) {Fail(task.Exception);yield break;}
                if(active!=this || !ReferenceEquals(Room,bound)) {if(run==binding) updates=null;yield break;}
                prepared=task.Result;
            }
            while(pending!=null && active==this)
            {
                var request=pending;pending=null;
                var context=prepared;var bound=Room;
                cancellation=new CancellationTokenSource();var token=cancellation.Token;
                var task=Task.Run(()=>
                {
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    var patch=context.CreatePatch(request.occlusion,token);
                    return new UpdateResult {patch=patch,cpuMilliseconds=timer.Elapsed.TotalMilliseconds};
                },token);
                worker=task;
                while(!task.IsCompleted) yield return null;
                worker=null;cancellation.Dispose();cancellation=null;
                if(task.IsCanceled) continue;
                if(task.IsFaulted)
                {
                    Fail(task.Exception);yield break;
                }
                if(active!=this || !ReferenceEquals(prepared,context) || !ReferenceEquals(Room,bound) || request.revision!=requestedRevision) continue;
                var result=task.Result;
                try
                {
                    if(IsReady) Publish(context,result,request);
                    else
                    {
                        if(!context.Commit(result.patch)) throw new InvalidOperationException("Устаревший результат начального света.");
                        AppliedOcclusionRevision=request.revision;LastCpuMilliseconds=result.cpuMilliseconds;
                        LastCandidatePixels=result.patch.CandidatePixels;LastDirtyPixels=result.patch.DirtyPixels;
                    }
                }
                catch(Exception error)
                {
                    Fail(error);
                }
                if(Failure!=null) yield break;
            }
            if(!IsReady && active==this && prepared!=null)
            {try {PublishInitial();} catch(Exception error) {Fail(error);}}
            // A readiness listener may synchronously rebind this component. Do not clear
            // the new coroutine handle when the old iterator returns from that callback.
            if(run==binding) updates=null;
        }
        private void PublishInitial()
        {
            var upload=System.Diagnostics.Stopwatch.StartNew();
            foregroundPixels=new Color[prepared.Width*prepared.Height];backgroundPixels=new Color[foregroundPixels.Length];
            prepared.CopyPixels(foregroundPixels,backgroundPixels);
            map=CreateTexture("Свет: "+Room.id);backgroundMap=CreateTexture("Фоновый свет: "+Room.id);
            stagingMap=CreateTexture("Обновляемый свет: "+Room.id);stagingBackground=CreateTexture("Обновляемый фоновый свет: "+Room.id);
            Upload(map,backgroundMap);
            Shader.SetGlobalTexture("_WorldLightMap",map);Shader.SetGlobalTexture("_BackgroundLightMap",backgroundMap);
            Shader.SetGlobalVector("_WorldLightBounds",new Vector4(Bounds.x,Bounds.y,1/Bounds.width,1/Bounds.height));
            Shader.SetGlobalFloat("_WorldLightEnabled",1);
            InitialUploadMilliseconds=upload.Elapsed.TotalMilliseconds;
            InitialPrepareMilliseconds=prepared.PrepareMilliseconds;InitialGetPixelsMilliseconds=prepared.GetPixelsMilliseconds;
            InitialDecodeMilliseconds=prepared.DecodeMilliseconds;InitialEvaluateMilliseconds=prepared.EvaluateMilliseconds;
            InitialBuildMilliseconds=initialTimer.Elapsed.TotalMilliseconds;
            Debug.Log($"Подготовлен свет: room={Room.id}, revision={AppliedOcclusionRevision}, sources={prepared.SourceCount}, prepareMs={InitialPrepareMilliseconds:F2}, getPixelsMs={InitialGetPixelsMilliseconds:F2}, decodeMs={InitialDecodeMilliseconds:F2}, evaluateMs={InitialEvaluateMilliseconds:F2}, uploadMs={InitialUploadMilliseconds:F2}, totalMs={InitialBuildMilliseconds:F2}, worker={asynchronous}.",this);
            SetReady(true);
        }
        private void SetReady(bool value)
        {if(IsReady==value) return;IsReady=value;ReadinessChanged?.Invoke(this);}
        private void Fail(Exception error)
        {
            cancellation?.Cancel();cancellation?.Dispose();cancellation=null;
            if(initialWorker!=null) initialWorker.ContinueWith(task=>{_ =task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            if(worker!=null) worker.ContinueWith(task=>{_ =task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            initialWorker=null;worker=null;
            Failure=error;updates=null;pending=null;SetReady(false);
            if(active==this) Shader.SetGlobalFloat("_WorldLightEnabled",0);
            Debug.LogError($"Не удалось подготовить свет: room={Room?.id}, revision={requestedRevision}, reason={error}.",this);
            Failed?.Invoke(this,error);
        }
        private void Publish(RoomLightMaps context,UpdateResult result,Request request)
        {
            var patch=result.patch;var upload=System.Diagnostics.Stopwatch.StartNew();
            if(!patch.IsEmpty)
            {
                // Copy the last published image, then apply this patch. A staging pair may be
                // several door revisions old; its previous pixels are never the new baseline.
                context.CopyPixels(foregroundPixels,backgroundPixels);
                for(int i=0;i<patch.DirtyPixels;i++)
                {int index=patch.Indices[i];foregroundPixels[index]=patch.Foreground[i];backgroundPixels[index]=patch.Background[i];}
                Upload(stagingMap,stagingBackground);
            }
            if(!context.Commit(patch)) throw new InvalidOperationException("Устаревший или чужой результат карты света.");
            if(!patch.IsEmpty)
            {
                (map,stagingMap)=(stagingMap,map);(backgroundMap,stagingBackground)=(stagingBackground,backgroundMap);
                // No yield/event between bindings: the next rendered frame uses one complete pair.
                Shader.SetGlobalTexture("_WorldLightMap",map);Shader.SetGlobalTexture("_BackgroundLightMap",backgroundMap);
            }
            Shader.SetGlobalFloat("_WorldLightEnabled",1);
            AppliedOcclusionRevision=request.revision;LastCpuMilliseconds=result.cpuMilliseconds;
            LastUploadMilliseconds=upload.Elapsed.TotalMilliseconds;
            LastLatencyMilliseconds=(Time.realtimeSinceStartupAsDouble-request.started)*1000;
            LastCandidatePixels=patch.CandidatePixels;LastDirtyPixels=patch.DirtyPixels;
            if(LastUpdateMilliseconds>20)
                Debug.Log($"Обновление света: room={Room.id}, revision={request.revision}, candidates={LastCandidatePixels}, dirty={LastDirtyPixels}, cpuMs={LastCpuMilliseconds:F2}, uploadMs={LastUploadMilliseconds:F2}, latencyMs={LastLatencyMilliseconds:F2}.",this);
        }
        private Texture2D CreateTexture(string title)=>new(prepared.Width,prepared.Height,TextureFormat.RGBA32,false,true)
            {name=title,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        private void Upload(Texture2D foreground,Texture2D background)
        {
            foreground.SetPixels(foregroundPixels);background.SetPixels(backgroundPixels);
            foreground.Apply(false,false);background.Apply(false,false);
        }
        private void OnDestroy()=>Release();
        public void Release()
        {
            binding++;
            if(updates!=null) StopCoroutine(updates);
            updates=null;pending=null;cancellation?.Cancel();cancellation?.Dispose();cancellation=null;
            if(worker!=null) worker.ContinueWith(task=>{_ =task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            if(initialWorker!=null) initialWorker.ContinueWith(task=>{_ =task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            worker=null;initialWorker=null;prepared=null;foregroundPixels=backgroundPixels=null;Room=null;SetReady(false);
            if(active==this) {active=null;Shader.SetGlobalFloat("_WorldLightEnabled",0);}
            foreach(var texture in new[]{map,backgroundMap,stagingMap,stagingBackground})
                if(texture!=null) {if(Application.isPlaying) Destroy(texture);else DestroyImmediate(texture);}
            map=backgroundMap=stagingMap=stagingBackground=null;
        }
        private sealed class Request {public long revision;public RoomLightMaps.Occlusion occlusion;public double started;}
        private sealed class UpdateResult {public RoomLightMaps.Patch patch;public double cpuMilliseconds;}
    }
}
