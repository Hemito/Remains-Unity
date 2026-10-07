using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    // Companion observation of the ordinary office/warehouse route. Never supplies input or changes world data.
    [DefaultExecutionOrder(11000)]
    public sealed class FactoryBackdropFrameProbe : MonoBehaviour
    {
        [Serializable] public sealed class Placement
        {
            public string record,texture,shader;
            public float x,y,width,height;
            public int order;
            public bool enabled;
        }
        [Serializable] public sealed class Frame
        {
            public string module,actor,owner,level,world,livePath,overviewPath,visibility;
            public int generation,lifecycle,unityFrame,artworks,regions,bindings,atlasWidth,atlasHeight;
            public float actorX,actorY,cameraX,cameraY,cameraSize,overviewX,overviewY,overviewSize;
            public Placement[] placements;
            public bool lightingReady,ordinaryRoute=true;
        }
        [Serializable] private sealed class Report
        {
            public int schema=1;
            public bool success,originalRuntimeVerified;
            public string detail;
            public Frame[] frames;
            public string limitation="Live — фактическая камера обычного маршрута; overview — отдельный диагностический ракурс того же runtime без перемещения игрока. Original runtime не проверен.";
        }
        private PrototypeRuntime runtime;
        private readonly List<Frame> frames=new();
        private string pending;
        private bool failed;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;
            if(!value.Options.Smoke.Contains("-backdrop-",StringComparison.Ordinal) ||
                !value.Options.Smoke.StartsWith("travel-points-",StringComparison.Ordinal) ||
                !value.Options.Smoke.Contains("-office-warehouse-",StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(value.Options.Report) || string.IsNullOrWhiteSpace(value.Options.SaveDirectory))
                throw new InvalidOperationException("Кадры оформления требуют отдельного ordinary office/warehouse маршрута.");
        }
        private void Update()
        {
            if(failed || pending!=null || frames.Count==2) return;
            var actor=runtime.Session.LocalActor;
            if(actor==null || actor.level!="factory:0" || runtime.Session.Travel.Pending || actor.down) return;
            // These coordinates are reached by the existing ordinary route. They are observation gates, not teleports.
            if(frames.Count==0 && actor.x>=96 && actor.x<115.2f) {pending="FactoryOffice53";StartCoroutine(Observe());}
            else if(frames.Count==1 && actor.x>=117 && actor.x<130) {pending="FactoryWarehouse40";StartCoroutine(Observe());}
        }
        private IEnumerator Observe()
        {
            RoomAuthoring module=null;RoomLighting lighting=null;
            try
            {
                module=FindObjectsByType<RoomAuthoring>(FindObjectsSortMode.None).Single(r=>r.name==pending);
                lighting=module.GetComponentInParent<RoomLighting>()??throw new InvalidOperationException("Нет текущего освещения: "+pending);
            }
            catch(Exception error) {Fail(error);}
            if(failed) yield break;
            while(!lighting.IsReady) yield return null;
            yield return null;
            try
            {
                Capture(module,lighting);pending=null;
                Write(frames.Count==2,frames.Count==2?"Оба текущих модуля и шесть фрагментов подтверждены на обычном маршруте.":"Снят офис; ожидается склад.");
            }
            catch(Exception error) {Fail(error);}
        }
        private void Fail(Exception error) {failed=true;Write(false,error.ToString());Debug.LogError("Проверка оформления существующего завода: "+error);}
        private void Capture(RoomAuthoring module,RoomLighting lighting)
        {
            bool office=pending=="FactoryOffice53";
            var actor=runtime.Session.LocalActor;
            var mask=module.GetComponent<RoomWindowMaskAuthoring>()??throw new InvalidOperationException("Нет сохранённой маски оформления: "+pending);
            mask.Validate();
            var art=module.GetComponentsInChildren<RoomArtworkAuthoring>(true);
            Require(art.Length==(office?208:98) && mask.Regions.Length==(office?8:1) && mask.Layers.Length==(office?39:27),"Разошлись количество рисунков/масок/bindings: "+pending);
            Require(mask.Visibility.width==1920 && mask.Visibility.height==1000,"Изменены размеры сохранённой маски: "+pending);
            var placements=new List<Placement>();
            if(office)
            {
                Read(135,"Factory53/decor-blood1-1",25.2f,.8f,3.98f,2.4f,-20);
                Read(136,"Factory53/decor-blood1-1",26,.8f,3.98f,2.4f,-20);
                Read(138,"Factory53/decor-blood1-1",24.4f,0,3.98f,2.4f,-20);
                Read(139,"Factory53/decor-blood1-1",25.2f,0,3.98f,2.4f,-20);
                Read(137,"Factory53/decor-chole6-6",22.4f,-1.105f,8.945f,6.705f,-30);
            }
            else Read(82,"Factory40/decor-chole1-1",21.6f,11.695f,8.945f,6.705f,-30);
            void Read(int record,string key,float x,float y,float width,float height,int order)
            {
                var matches=art.Select(a=>new {component=a,data=a.Read(module)})
                    .Where(a=>a.data.texture==key && Mathf.Abs(a.data.x-x)<.0001f && Mathf.Abs(a.data.y-y)<.0001f).ToArray();
                Require(matches.Length==1,"Нет единственного установленного source рисунка: "+pending+"/"+record+"/"+key);
                var item=matches[0];var renderer=item.component.GetComponent<MeshRenderer>();
                Require(Mathf.Abs(item.data.width-width)<.0001f && Mathf.Abs(item.data.height-height)<.0001f && item.data.order==order &&
                    renderer.enabled && item.component.gameObject.activeInHierarchy,"Изменены bounds/order/visibility source записи: "+record);
                placements.Add(new Placement {record=$"original:plant:{(office?53:40)}:record:{record}",texture=key,shader=renderer.sharedMaterial.shader.name,
                    x=item.data.x,y=item.data.y,width=item.data.width,height=item.data.height,order=item.data.order,enabled=renderer.enabled});
            }
            var camera=Camera.main??throw new InvalidOperationException("Нет фактической камеры игры.");
            var savedPosition=camera.transform.position;float savedSize=camera.orthographicSize;
            string suffix=office?"office":"warehouse";
            var frame=new Frame {module=pending,actor=actor.id,owner=actor.owner,level=actor.level,world=runtime.Session.World.worldId,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,unityFrame=Time.frameCount,actorX=actor.x,actorY=actor.y,
                cameraX=savedPosition.x,cameraY=savedPosition.y,cameraSize=savedSize,overviewX=office?104.1f:141.27f,overviewY=office?3:15.05f,overviewSize=6,
                artworks=art.Length,regions=mask.Regions.Length,bindings=mask.Layers.Length,atlasWidth=mask.Visibility.width,atlasHeight=mask.Visibility.height,
                visibility=mask.Visibility.name,lightingReady=lighting.IsReady,placements=placements.ToArray(),
                livePath=runtime.Options.Report+".backdrop-"+suffix+".live.png",overviewPath=runtime.Options.Report+".backdrop-"+suffix+".overview.png"};
            NetworkFrameCapture.Save(camera,frame.livePath,runtime.Hud.GetComponent<Canvas>(),1920,1080);
            try
            {
                camera.transform.position=new Vector3(frame.overviewX,frame.overviewY,savedPosition.z);camera.orthographicSize=frame.overviewSize;
                NetworkFrameCapture.Save(camera,frame.overviewPath,null,1920,1080);
            }
            finally {camera.transform.position=savedPosition;camera.orthographicSize=savedSize;}
            Require(Vector3.Distance(camera.transform.position,savedPosition)==0 && camera.orthographicSize==savedSize,"Диагностический ракурс не восстановил камеру.");
            frames.Add(frame);
            Debug.Log($"Оформление на обычном маршруте: module={pending}, actor={actor.id}, position={actor.Position}, art={art.Length}, bindings={mask.Layers.Length}, captures={frame.livePath}/{frame.overviewPath}.");
        }
        private void Write(bool success,string detail)=>File.WriteAllText(runtime.Options.Report+".backdrop.json",JsonUtility.ToJson(new Report {success=success,detail=detail,frames=frames.ToArray()},true));
        private static void Require(bool condition,string detail) {if(!condition) throw new InvalidOperationException(detail);}
    }
}
