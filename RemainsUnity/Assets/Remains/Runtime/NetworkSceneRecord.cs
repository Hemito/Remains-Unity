using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // StateJson persists fields; Unity Rect exposes its geometry as properties.
    [Serializable] public struct NetworkFootSurfaceRecord
    {
        public float x,y,width,height;
        public int slopeDirection;
        public static NetworkFootSurfaceRecord Capture(ActorBodySupportSample sample)
            =>new() {x=sample.SurfaceBounds.x,y=sample.SurfaceBounds.y,width=sample.SurfaceBounds.width,
                height=sample.SurfaceBounds.height,slopeDirection=sample.SlopeDirection};
    }
    [Serializable] public sealed class NetworkPonyRecord
    {
        public ActorState actor;public Vector3 position,scale;public string clip,groupId;public int frame,groupOrder;public float time;public bool active;
        public Vector2 bodyPosition,footContact;public NetworkFootSurfaceRecord footSurface;public bool footProfile,footGrounded,hasFootContact;
        public string footOutcome;public int footPart,footPixelX,footPixelY;
        public bool hasMagicActivity;public float magicOpacity,magicMassKg;public string magicSource;public PonyMagicAction magicAction;
    }
    [Serializable] public sealed class NetworkSpriteRecord
    {public string name,resource,spriteName,textureName,groupId;public Vector3 position,scale,euler;public float rectX,rectY,rectWidth,rectHeight;public Vector2 pivot;public float pixelsPerUnit;public int order,groupOrder;public Color color;public bool flipX,flipY,enabled;}
    [Serializable] public sealed class NetworkSceneRecord
    {
        public int schema=2,phase,width,height;
        public string observer,level;
        public Vector3 cameraPosition;
        public Vector3 localVisualMinimum,localVisualMaximum;
        public float cameraSize;
        public Color background;
        public WorldState world;
        public NetworkPonyRecord[] ponies;
        public NetworkSpriteRecord[] sprites;
        public string[] hudText;
        public static void Save(PrototypeRuntime runtime,int phase)
        {
            var camera=Camera.main??UnityEngine.Object.FindFirstObjectByType<Camera>();
            if(camera==null) throw new InvalidOperationException("Нет камеры для записи сетевого кадра.");
            var world=runtime.Session.World;
            var record=new NetworkSceneRecord {phase=phase,width=Screen.width,height=Screen.height,observer=runtime.Session.LocalActorId,
                level=runtime.Session.LocalActor.level,cameraPosition=camera.transform.position,cameraSize=camera.orthographicSize,background=camera.backgroundColor,world=world.Copy()};
            if(runtime.FindActorVisual(runtime.Session.LocalActor.id) is not {} localVisual || !localVisual.TryGetDrawnBounds(out var localBounds))
                throw new InvalidOperationException("Нет фактически отрисованных границ игрока для записи: "+runtime.Session.LocalActor.id);
            record.localVisualMinimum=localBounds.min;record.localVisualMaximum=localBounds.max;
            record.ponies=UnityEngine.Object.FindObjectsByType<PonyVisual>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(view=>
            {
                var actor=world.Find(view.ActorId);var support=ActorBodySupport.Sample(actor);
                return new NetworkPonyRecord {actor=actor.Copy(),position=view.transform.position,scale=view.transform.localScale,
                    clip=view.DrawnClip,frame=view.DrawnFrame,time=view.DrawnTime,active=view.gameObject.activeInHierarchy,
                    groupId=view.GetComponentInParent<ActorVisualGroup>()?.ActorId,groupOrder=view.GetComponentInParent<ActorVisualGroup>()?.Order??0,
                    bodyPosition=support.BodyPosition,footContact=support.ContactPoint,footSurface=NetworkFootSurfaceRecord.Capture(support),
                    footProfile=support.HasFootProfile,footGrounded=support.Grounded,hasFootContact=support.HasContact,
                    footOutcome=support.Outcome.ToString(),footPart=support.PartIndex,footPixelX=support.PixelX,footPixelY=support.PixelY,
                    hasMagicActivity=!view.UsesMagicPreview,magicOpacity=view.DrawnMagicActivity.Opacity,magicMassKg=view.DrawnMagicActivity.MassKg,
                    magicSource=view.DrawnMagicActivity.SourceId,magicAction=view.DrawnMagicActivity.Action};
            }).ToArray();
            record.sprites=UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(view=>view.GetComponentInParent<CasingEffects>()?.IsUnusedRenderer(view)!=true).Select(view=>
            {
                if(view.sprite==null) throw new InvalidOperationException($"Нет спрайта при записи: {view.name}.");
                var sprite=view.sprite;
                return new NetworkSpriteRecord {name=view.name,resource=Resource(view,world),spriteName=sprite.name,textureName=sprite.texture.name,
                    position=view.transform.position,scale=view.transform.localScale,euler=view.transform.eulerAngles,
                    rectX=sprite.rect.x,rectY=sprite.rect.y,rectWidth=sprite.rect.width,rectHeight=sprite.rect.height,
                    pivot=new Vector2(sprite.pivot.x/sprite.rect.width,sprite.pivot.y/sprite.rect.height),pixelsPerUnit=sprite.pixelsPerUnit,order=view.sortingOrder,
                    groupId=view.GetComponentInParent<ActorVisualGroup>()?.ActorId,groupOrder=view.GetComponentInParent<ActorVisualGroup>()?.Order??0,
                    color=view.color,flipX=view.flipX,flipY=view.flipY,enabled=view.enabled && view.gameObject.activeInHierarchy};
            }).ToArray();
            File.WriteAllText(runtime.Options.Report+$".phase-{phase}.scene.json",StateJson.ToJson(record,true));
            if(runtime.Options.Smoke.Contains("-frames-"))
            {
                NetworkFrameCapture.Save(camera,runtime.Options.Report+$".phase-{phase}.live.png");
                if(runtime.Hud==null) throw new InvalidOperationException("Нет HUD для записи сетевого интерфейса.");
                record.hudText=runtime.Hud.GetComponentsInChildren<UnityEngine.UI.Text>().Select(text=>text.text).ToArray();
                NetworkFrameCapture.Save(camera,runtime.Options.Report+$".phase-{phase}.hud.png",runtime.Hud.GetComponent<Canvas>());
                File.WriteAllText(runtime.Options.Report+$".phase-{phase}.scene.json",StateJson.ToJson(record,true));
            }
        }
        private static string Resource(SpriteRenderer view,WorldState world)
        {
            // A container's canonical resource may live outside Original/Props. Its
            // texture name alone is not a resource path (for example OfficeLoot).
            const string prefix="Контейнер ";
            if(view.GetComponent<PropView>()!=null && view.name.StartsWith(prefix,StringComparison.Ordinal))
            {
                string id=view.name.Substring(prefix.Length);var source=world.containers.Find(c=>c.id==id);
                if(source==null)
                {
                    Debug.LogWarning($"Нет состояния контейнера для записи графики: id={id}, view={view.name}.",view);
                    return null;
                }
                return PropCatalog.Get(source.definition).resource;
            }
            return view.GetComponent<StaticActorVisual>()?.Resource ?? view.GetComponent<AtlasActorVisual>()?.Resource ??
                "Original/"+(view.name.StartsWith("Оружие ",StringComparison.Ordinal)?"Weapons/":"Props/")+view.sprite.texture.name;
        }
    }
}
