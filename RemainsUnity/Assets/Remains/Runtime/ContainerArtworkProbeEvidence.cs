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
    // Capture-time evidence only. This helper never changes a container or its view.
    internal static class ContainerArtworkProbeEvidence
    {
        [Serializable] private sealed class Evidence
        {
            public int schema=1,version,preparedFrames,width,height,itemCount;
            public long appearanceRevision,worldRevision;
            public string phase,world,source,definition,observer,mode,sprite,texture;
            public bool opened,containerWindowOpen,fullyVisible;
            public float pixelsPerUnit,physicalWidth,physicalHeight;
            public float[] spriteRect;
            public Vector2 spritePivot,physicalPosition;
            public Vector3 rendererMinimum,rendererMaximum,viewScale;
            public Vector3[] viewportCorners;
        }
        public static void RequireState(ContainerState source,bool opened)
        {
            var state=source?.appearance;
            if(state==null || state.version!=1 || state.opened!=opened || state.revision!=(opened?1:0))
                throw new InvalidOperationException($"Состояние рисунка мебели не подтверждено: source={source?.id}, expectedOpened={opened}, version={state?.version}, opened={state?.opened}, revision={state?.revision}.");
        }
        public static void Save(PrototypeRuntime runtime,ContainerState source,string phase,string path)
        {
            var views=UnityEngine.Object.FindObjectsByType<PropView>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(view=>view.name=="Контейнер "+source.id).ToArray();
            if(views.Length!=1) throw new InvalidOperationException($"Нет единственного рисунка мебели: source={source.id}, views={views.Length}.");
            var view=views[0];var profile=view.ContainerArtwork;var renderer=view.Artwork;
            if(profile==null) throw new InvalidOperationException("Нет канонического профиля рисунка мебели: "+source.id);
            profile.Validate(source.definition);
            var expected=profile.Select(source.appearance.opened);
            if(!view.gameObject.activeInHierarchy || renderer==null || !renderer.enabled || renderer.sprite!=expected)
                throw new InvalidOperationException($"Рисунок мебели не читает подтверждённое состояние: source={source.id}, opened={source.appearance.opened}, actual={renderer?.sprite?.name}, expected={expected?.name}.");
            var glow=view.GetComponentInChildren<HeldPropGlow>(true);
            int frames=profile.Mode==ContainerArtworkMode.Static?1:2;
            if(glow==null || glow.PreparedFrameCount!=frames || glow.CurrentFrame!=expected)
                throw new InvalidOperationException($"Кэш подсветки мебели не соответствует текущему кадру: source={source.id}, expectedFrames={frames}, actualFrames={glow?.PreparedFrameCount}.");
            var camera=Camera.main;
            if(camera==null) throw new InvalidOperationException("Нет штатной камеры для проверки рисунка мебели.");
            var bounds=renderer.bounds;var minimum=bounds.min;var maximum=bounds.max;
            var corners=new[]{new Vector3(minimum.x,minimum.y,bounds.center.z),new Vector3(maximum.x,minimum.y,bounds.center.z),
                new Vector3(maximum.x,maximum.y,bounds.center.z),new Vector3(minimum.x,maximum.y,bounds.center.z)}
                .Select(camera.WorldToViewportPoint).ToArray();
            bool visible=corners.All(point=>point.x>=0 && point.x<=1 && point.y>=0 && point.y<=1 && point.z>=camera.nearClipPlane && point.z<=camera.farClipPlane);
            if(!visible) throw new InvalidOperationException($"Мебель обрезана штатной камерой: source={source.id}, bounds={bounds}, camera={camera.transform.position}.");
            var sprite=renderer.sprite;
            File.WriteAllText(path,StateJson.ToJson(new Evidence {phase=phase,world=runtime.Session.World.worldId,worldRevision=runtime.Session.World.revision,
                observer=runtime.Session.LocalActorId,source=source.id,definition=source.definition,version=source.appearance.version,
                opened=source.appearance.opened,appearanceRevision=source.appearance.revision,mode=profile.Mode.ToString(),sprite=sprite.name,texture=sprite.texture.name,
                preparedFrames=glow.PreparedFrameCount,width=Screen.width,height=Screen.height,itemCount=source.items.Count,
                containerWindowOpen=runtime.Hud.ContainerOpen,fullyVisible=visible,pixelsPerUnit=sprite.pixelsPerUnit,spriteRect=new[]{sprite.rect.x,sprite.rect.y,sprite.rect.width,sprite.rect.height},
                spritePivot=new Vector2(sprite.pivot.x/sprite.rect.width,sprite.pivot.y/sprite.rect.height),physicalPosition=source.Position,
                physicalWidth=source.width,physicalHeight=source.height,rendererMinimum=minimum,rendererMaximum=maximum,viewScale=view.transform.localScale,viewportCorners=corners},true));
        }
    }
}
