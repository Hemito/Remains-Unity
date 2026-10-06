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
        private Request pending;
        private long requestedRevision;
        public RoomLightProfile Profile {get;private set;}
        public RoomLayout Room {get;private set;}
        public Rect Bounds {get;private set;}
        public Texture2D ForegroundMap=>map;
        public Texture2D BackgroundMap=>backgroundMap;
        public bool IsUpdating=>updates!=null || worker!=null || pending!=null;
        public long AppliedOcclusionRevision {get;private set;}
        public int UniqueDecodedTextures=>prepared?.UniqueDecodedTextures??0;
        public long DecodedPixels=>prepared?.DecodedPixels??0;
        public double InitialBuildMilliseconds {get;private set;}
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
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            Release();
            Profile=profile??throw new InvalidOperationException("Нет профиля света: "+room.id);
            if(room.artwork==null)
                Debug.LogWarning($"Комната {room.id}: нет artwork; для старой диагностической геометрии используется диапазон света 0–7. Проверьте описание комнаты.",this);
            float bottom=room.artwork==null?0:room.bottom,top=room.artwork==null?7:room.top;
            Bounds=new Rect(room.left,bottom,room.right-room.left,top-bottom);
            try
            {
                prepared=new RoomLightMaps(room,Bounds,Profile);Room=room;
                foregroundPixels=new Color[prepared.Width*prepared.Height];backgroundPixels=new Color[foregroundPixels.Length];
                prepared.CopyPixels(foregroundPixels,backgroundPixels);
                map=CreateTexture("Свет: "+room.id);backgroundMap=CreateTexture("Фоновый свет: "+room.id);
                stagingMap=CreateTexture("Обновляемый свет: "+room.id);stagingBackground=CreateTexture("Обновляемый фоновый свет: "+room.id);
                Upload(map,backgroundMap);
                requestedRevision=AppliedOcclusionRevision=0;
                LastCpuMilliseconds=LastUploadMilliseconds=LastLatencyMilliseconds=0;LastCandidatePixels=LastDirtyPixels=0;
            }
            catch {Release();throw;}
            active=this;Shader.SetGlobalTexture("_WorldLightMap",map);
            Shader.SetGlobalTexture("_BackgroundLightMap",backgroundMap);
            Shader.SetGlobalVector("_WorldLightBounds",new Vector4(Bounds.x,Bounds.y,1/Bounds.width,1/Bounds.height));
            Shader.SetGlobalFloat("_WorldLightEnabled",1);
            InitialBuildMilliseconds=elapsed.Elapsed.TotalMilliseconds;
            if(elapsed.ElapsedMilliseconds>100) Debug.Log($"Построение света: room={room.id}, sources={Profile.lights.Length}, milliseconds={elapsed.ElapsedMilliseconds}.");
        }
        // Called from world/geometry events, never from a frame-time geometry scan.
        public void Synchronize(RoomLayout room,long revision)
        {
            if(!ReferenceEquals(Room,room)) Initialize(room,Profile);
            RequestOcclusionUpdate(room,revision);
        }
        public void RequestOcclusionUpdate(RoomLayout room,long revision)
        {
            if(room==null || !ReferenceEquals(Room,room) || prepared==null || revision<0)
                throw new InvalidOperationException($"Свет привязан к другой геометрии: bound={Room?.id}, requested={room?.id}, revision={revision}.");
            if(revision<=requestedRevision) return;
            pending=new Request {revision=revision,occlusion=RoomLightMaps.CaptureOcclusion(room.platforms),started=Time.realtimeSinceStartupAsDouble};
            requestedRevision=revision;cancellation?.Cancel();
            if(updates==null) updates=StartCoroutine(Rebuilds());
        }
        private IEnumerator Rebuilds()
        {
            // Ensure StartCoroutine has returned its handle before this iterator can complete.
            yield return null;
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
                    Debug.LogError($"Не удалось пересчитать свет: room={bound?.id}, revision={request.revision}, reason={task.Exception}.",this);
                    if(active==this) Shader.SetGlobalFloat("_WorldLightEnabled",0);
                    continue;
                }
                if(active!=this || !ReferenceEquals(prepared,context) || !ReferenceEquals(Room,bound) || request.revision!=requestedRevision) continue;
                var result=task.Result;
                try {Publish(context,result,request);}
                catch(Exception error)
                {
                    Debug.LogError($"Не удалось опубликовать две карты света: room={bound?.id}, revision={request.revision}, reason={error}.",this);
                    if(active==this) Shader.SetGlobalFloat("_WorldLightEnabled",0);
                }
            }
            updates=null;
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
            if(updates!=null) StopCoroutine(updates);
            updates=null;pending=null;cancellation?.Cancel();cancellation?.Dispose();cancellation=null;
            if(worker!=null) worker.ContinueWith(task=>{_ =task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            worker=null;prepared=null;foregroundPixels=backgroundPixels=null;Room=null;
            if(active==this) {active=null;Shader.SetGlobalFloat("_WorldLightEnabled",0);}
            foreach(var texture in new[]{map,backgroundMap,stagingMap,stagingBackground})
                if(texture!=null) {if(Application.isPlaying) Destroy(texture);else DestroyImmediate(texture);}
            map=backgroundMap=stagingMap=stagingBackground=null;
        }
        private sealed class Request {public long revision;public RoomLightMaps.Occlusion occlusion;public double started;}
        private sealed class UpdateResult {public RoomLightMaps.Patch patch;public double cpuMilliseconds;}
    }
}
