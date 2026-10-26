using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class WindowActorCompositeChecks
    {
        public static void RunBatch()
        {
            try {RunGroupedWindows();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()=>RunWindows(false);
        public static void RunGroupedWindows()=>RunWindows(true);
        private static void RunWindows(bool grouped)
        {
            GameObject room=null,cameraRoot=null,weaponRoot=null,actorGroupRoot=null;PonyVisual visual=null;
            RenderTexture target=null;Texture2D alone=null,full=null;var previous=RenderTexture.active;
            try
            {
                room=AuthoredRoomVisual.Create("hub",RoomLayout.Load("hub"));
                var windows=room.GetComponentsInChildren<RoomSpriteAuthoring>(true).Where(item=>item.Erase!=null).ToArray();
                var window=windows.FirstOrDefault();
                if(window==null) throw new InvalidOperationException("Проверка окна: в настоящем хабе не найдено окно со стиранием.");
                var area=window.GetComponent<SpriteRenderer>().bounds;
                // Straddle the window frame, so an incorrect room/actor order cannot hide
                // behind transparent glass pixels in the middle of the artwork.
                var overlap=new Vector2(area.min.x+.1f,area.center.y);
                var level=room.GetComponent<FactoryLevelView>();level.Focus(overlap);
                ActorTypes.Load();PropCatalog.Load();Anatomy.Load();WeaponCatalog.Load();PonyVisual.Prepare();
                var actor=new ActorState{id="window-composite-check",actorType="pony",anatomy="pony",player=true,connected=true,
                    level="hub",pose="idle",x=area.center.x,y=area.center.y,facing=1,weapon="pistol",weaponAngle=0,weaponReach=.4f,
                    appearance=new CharacterAppearance{version=CharacterAppearance.CurrentVersion}};
                visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.ConfigureMagicPreview(0);visual.Bind(actor);visual.DrawPose(0);
                if(!visual.TryGetDrawnBounds(out var body)) throw new InvalidOperationException("Проверка окна: нет границ пони.");
                actor.x+=overlap.x-body.center.x;actor.y+=overlap.y-body.center.y;visual.DrawPose(0);
                visual.GetComponent<SortingGroup>().sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player);
                weaponRoot=new GameObject("Оружие перед окном");var weapon=weaponRoot.AddComponent<SpriteRenderer>();
                SpriteArtworkMaterial.Apply(weapon,WeaponArtwork.Load()["pistol"]);var pose=WeaponPose.SampleRanged(actor);
                weaponRoot.transform.position=pose.Root;weaponRoot.transform.rotation=Quaternion.Euler(0,0,pose.Angle);
                weaponRoot.transform.localScale=new Vector3(1,pose.Mirrored?-1:1,1);
                weapon.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player)+1;
                if(grouped)
                {
                    var world=new WorldState();world.actors.Add(actor);
                    var orders=new WorldVisualOrderTable();orders.Reconcile(world,"hub");
                    var layers=new ActorVisualLayers(orders:orders);
                    actorGroupRoot=layers.Attach(actor.id,visual.transform).gameObject;layers.Attach(actor.id,weaponRoot.transform);
                    visual.DrawOrder=WorldVisualOrderPolicy.ActorPartOrder;weapon.sortingOrder=WorldVisualOrderPolicy.WeaponPartOrder;
                }
                const int layer=31;
                foreach(var root in new[]{room,visual.gameObject,weaponRoot})
                    foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
                cameraRoot=new GameObject("Камера проверки окна");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.5f;camera.aspect=1;
                camera.transform.position=new Vector3(overlap.x,overlap.y,-10);camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.allowHDR=false;camera.allowMSAA=false;
                target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
                int cases=0,totalOpaque=0;
                foreach(var currentWindow in grouped?windows:new[]{window})
                {
                    var currentArea=currentWindow.GetComponent<SpriteRenderer>().bounds;
                    var currentOverlap=new Vector2(currentArea.min.x+.1f,currentArea.center.y);
                    level.Focus(currentOverlap);
                    foreach(var phase in grouped?new[]{0f,.12f,.3f}:new[]{0f}) foreach(int facing in grouped?new[]{1,-1}:new[]{1})
                    {
                        actor.pose=phase==0?"idle":"run";actor.facing=facing;actor.poseTime=phase;actor.vx=phase==0?0:3*facing;
                        visual.DrawPose(phase,true);
                        if(!visual.TryGetDrawnBounds(out body)) throw new InvalidOperationException("Нет границ позы перед окном.");
                        actor.x+=currentOverlap.x-body.center.x;actor.y+=currentOverlap.y-body.center.y;visual.DrawPose(phase,true);
                        pose=WeaponPose.SampleRanged(actor);weaponRoot.transform.position=pose.Root;
                        weaponRoot.transform.rotation=Quaternion.Euler(0,0,pose.Angle);weaponRoot.transform.localScale=new Vector3(1,pose.Mirrored?-1:1,1);
                        camera.transform.position=new Vector3(currentOverlap.x,currentOverlap.y,-10);
                        var renderers=room.GetComponentsInChildren<Renderer>(true);var enabled=renderers.Select(item=>item.enabled).ToArray();
                        foreach(var renderer in renderers) renderer.enabled=false;
                        if(grouped && cases<6)
                        {
                            weapon.enabled=false;
                            var bodyOnly=Capture(camera,target);
                            try {CheckBodyAlpha(bodyOnly,actor,cases);}
                            finally {UnityEngine.Object.DestroyImmediate(bodyOnly);weapon.enabled=true;}
                        }
                        alone=Capture(camera,target);
                        for(int i=0;i<renderers.Length;i++) renderers[i].enabled=enabled[i];
                        full=Capture(camera,target);
                        var expected=alone.GetPixels32();var actual=full.GetPixels32();int opaque=0,changed=0;
                        for(int i=0;i<expected.Length;i++)
                        {
                            if(expected[i].a<254) continue;
                            opaque++;
                            if(Math.Abs(expected[i].r-actual[i].r)>2 || Math.Abs(expected[i].g-actual[i].g)>2 || Math.Abs(expected[i].b-actual[i].b)>2) changed++;
                        }
                        if(opaque<500 || changed!=0)
                        {
                            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference"));Directory.CreateDirectory(directory);
                            File.WriteAllBytes(Path.Combine(directory,"window-actor-failure.png"),full.EncodeToPNG());
                            File.WriteAllBytes(Path.Combine(directory,"window-actor-alone.png"),alone.EncodeToPNG());
                            throw new InvalidOperationException($"Окно изменило непрозрачные пиксели пони/оружия: window={currentWindow.Definition}, grouped={grouped}, pose={actor.pose}, phase={phase}, facing={facing}, opaque={opaque}, changed={changed}.");
                        }
                        cases++;totalOpaque+=opaque;
                        UnityEngine.Object.DestroyImmediate(alone);UnityEngine.Object.DestroyImmediate(full);alone=full=null;
                    }
                }
                Debug.Log($"Композиция настоящих окон: PASS — grouped={grouped}, вариантов={cases}, непрозрачных пикселей={totalOpaque}; оформление не изменило их RGB.");
            }
            finally
            {
                RenderTexture.active=previous;
                if(target!=null) target.Release();
                foreach(var item in new UnityEngine.Object[]{room,actorGroupRoot,visual?.gameObject,weaponRoot,cameraRoot,target,alone,full})
                    if(item!=null) UnityEngine.Object.DestroyImmediate(item);
            }
        }
        private static void CheckBodyAlpha(Texture2D image,ActorState actor,int sample)
        {
            var pixels=image.GetPixels32();int interior=0,semi=0,minimum=255;
            for(int y=2;y<image.height-2;y++) for(int x=2;x<image.width-2;x++)
            {
                bool inside=true;
                for(int dy=-2;dy<=2 && inside;dy++) for(int dx=-2;dx<=2;dx++)
                    if(pixels[(y+dy)*image.width+x+dx].a<16) {inside=false;break;}
                if(!inside) continue;
                int alpha=pixels[y*image.width+x].a;interior++;minimum=Math.Min(minimum,alpha);if(alpha<254) semi++;
            }
            // Diagnostic only: report semiopaque interiors without altering intended art.
            Debug.Log($"Альфа тела без оружия/магии: pose={actor.pose}, phase={actor.poseTime}, facing={actor.facing}, interior={interior}, semi={semi}, minimum={minimum}/255.");
            if(sample==0)
            {
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference"));Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory,"window-actor-body-alone.png"),image.EncodeToPNG());
                var heat=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false,true);
                try
                {
                    for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(pixels[i].a,pixels[i].a,pixels[i].a,255);
                    heat.SetPixels32(pixels);heat.Apply();File.WriteAllBytes(Path.Combine(directory,"window-actor-body-alpha.png"),heat.EncodeToPNG());
                }
                finally {UnityEngine.Object.DestroyImmediate(heat);}
                foreach(var part in PonyRenderSelection.Frames(PonyAppearance.Snapshot(actor),"idle")[0])
                    if(part.slot=="fur" && part.visible)
                        Debug.Log($"Альфа исходной части шерсти: path={part.sourcePath}, shape={part.sourceShape}, vertexAlpha={part.baseColor.a}, texture={part.material.mainTexture.name}.");
            }
        }
        public static void RunLampProjection()
        {
            GameObject room=null,cameraRoot=null,weaponRoot=null;PonyVisual visual=null;ActorCastShadowView shadow=null;
            RenderTexture target=null;Texture2D both=null,leftOnly=null,matchedLeft=null,sheet=null;var previous=RenderTexture.active;
            try
            {
                room=AuthoredRoomVisual.Create("hub",RoomLayout.Load("hub"));var level=room.GetComponent<FactoryLevelView>();
                level.Focus(new Vector2(33,1.6f));
                string left=level.LampIds.Single(id=>id.EndsWith(":vJGnXZBoX5BOIT4p",StringComparison.Ordinal));
                string middle=level.LampIds.Single(id=>id.EndsWith(":otIYrGj5RimnIyMV",StringComparison.Ordinal));
                string right=level.LampIds.Single(id=>id.EndsWith(":T7bpyfTNhj0wX4EY",StringComparison.Ordinal));
                level.SyncLamps(new[]{middle},1,new[]{middle});
                ActorTypes.Load();PropCatalog.Load();Anatomy.Load();WeaponCatalog.Load();PonyVisual.Prepare();
                var actor=new ActorState{id="lamp-projection-check",actorType="pony",anatomy="pony",player=true,connected=true,
                    level="hub",pose="idle",x=33,y=.86395f,facing=1,weapon="pistol",weaponAngle=40,weaponReach=.4f,
                    appearance=new CharacterAppearance{version=CharacterAppearance.CurrentVersion}};
                visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.ConfigureMagicPreview(0);visual.Bind(actor);visual.DrawPose(0);
                if(!visual.TryGetDrawnBounds(out var body)) throw new InvalidOperationException("Проверка ламп: нет границ пони.");
                visual.GetComponent<SortingGroup>().sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player);
                weaponRoot=new GameObject("Пистолет между лампами");var weapon=weaponRoot.AddComponent<SpriteRenderer>();
                SpriteArtworkMaterial.Apply(weapon,WeaponArtwork.Load()["pistol"]);var pose=WeaponPose.SampleRanged(actor);
                weaponRoot.transform.position=pose.Root;weaponRoot.transform.rotation=Quaternion.Euler(0,0,pose.Angle);
                weaponRoot.transform.localScale=new Vector3(1,pose.Mirrored?-1:1,1);
                weapon.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player)+1;
                var twoSources=new List<ActorShadowSource>();level.SelectActorShadowSources(body.center,twoSources);
                if(twoSources.Count!=2 || !twoSources.Exists(item=>item.Id==left) || !twoSources.Exists(item=>item.Id==right))
                    throw new InvalidOperationException("Проверка проекции ванной: не выбраны обе лампы, позиция="+body.center+", источники="+string.Join(",",twoSources.Select(item=>item.Id)));
                level.SyncLamps(new[]{middle,right},2,new[]{middle,right});
                var oneSource=new List<ActorShadowSource>();level.SelectActorShadowSources(body.center,oneSource);
                if(oneSource.Count!=1 || oneSource[0].Id!=left)
                    throw new InvalidOperationException("Проверка проекции ванной: после разрушения правой лампы осталось неверное число источников: "+oneSource.Count);
                // Restore the identical lighting for both images: this comparison measures
                // the rendered shadow masks, rather than the brighter/darker room itself.
                level.SyncLamps(new[]{middle},3,new[]{middle});
                var parts=new List<LightOccluder>();visual.AppendLightOccluders(parts);
                parts.Add(new LightOccluder(weapon.sprite,weaponRoot.transform.localToWorldMatrix));
                shadow=UnityEngine.Object.Instantiate(GameAssets.Load<ActorCastShadowView>(ActorCastShadowView.ResourcePath)
                    ??throw new InvalidOperationException("Нет авторского префаба тени для проверки ванной."));shadow.Prepare(parts,2);
                level.PrepareActorShadowFields(twoSources.Select(item=>item.Id).ToArray());
                var masks=new List<ProjectedLightShadow>();
                const int layer=31;
                foreach(var root in new[]{room,visual.gameObject,weaponRoot,shadow.gameObject})
                    foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
                cameraRoot=new GameObject("Камера двух теней ванной");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=2.4f;camera.aspect=2;
                camera.transform.position=new Vector3(33,2.6f,-10);camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
                target=new RenderTexture(1024,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
                shadow.Draw(body.center,parts,twoSources,level.ShadowSettings);
                shadow.AppendProjectedLightShadows(masks);level.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),masks,Array.Empty<string>());
                both=Capture(camera,target);
                if(shadow.ActiveSourceCount!=2) throw new InvalidOperationException("Вид тени не сохранил обе выбранные лампы.");
                shadow.Draw(body.center,parts,oneSource,level.ShadowSettings);
                masks.Clear();shadow.AppendProjectedLightShadows(masks);level.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),masks,Array.Empty<string>());
                leftOnly=Capture(camera,target);
                if(shadow.ActiveSourceCount!=1) throw new InvalidOperationException("Вид тени не отключил проекцию правой лампы.");
                var a=both.GetPixels32();var b=leftOnly.GetPixels32();int changed=0;
                for(int i=0;i<a.Length;i++) if(Math.Abs(a[i].r-b[i].r)>2 || Math.Abs(a[i].g-b[i].g)>2 || Math.Abs(a[i].b-b[i].b)>2) changed++;
                // Also remove only the right slot without reweighting the left slot.
                // Otherwise a strength change of the surviving shadow alone could pass.
                shadow.Draw(body.center,parts,new[]{twoSources.Single(item=>item.Id==left)},level.ShadowSettings);
                masks.Clear();shadow.AppendProjectedLightShadows(masks);level.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),masks,Array.Empty<string>());
                matchedLeft=Capture(camera,target);var matched=matchedLeft.GetPixels32();int rightPixels=0;
                for(int i=0;i<a.Length;i++) if(Math.Abs(a[i].r-matched[i].r)>2 || Math.Abs(a[i].g-matched[i].g)>2 || Math.Abs(a[i].b-matched[i].b)>2) rightPixels++;
                sheet=new Texture2D(2048,512,TextureFormat.RGBA32,false,true);sheet.SetPixels(0,0,1024,512,both.GetPixels());sheet.SetPixels(1024,0,1024,512,leftOnly.GetPixels());sheet.Apply();
                string export=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/lamp-shadow-comparison.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(export));File.WriteAllBytes(export,sheet.EncodeToPNG());
                File.WriteAllText(Path.ChangeExtension(export,".txt"),$"Слева: две проекции ламп; справа: только левая. Одинаковое поле освещения и поза в обоих кадрах.\nActor={body.center}; left={left}; right={right}; changedPixels={changed}; rightShadowPixels={rightPixels} (сохраняет вклад левой).\n");
                if(changed<=20 || rightPixels<=20) throw new InvalidOperationException($"Проекция второй лампы не видна в настоящей ванной: изменено {changed} пикселей, только правая={rightPixels}; {export}.");
                Debug.Log($"Проекции ламп ванной: PASS — две/одна тень при одинаковом свете; изменено пикселей={changed}, только правая={rightPixels}; {export}.");
            }
            finally
            {
                RenderTexture.active=previous;if(target!=null) target.Release();
                foreach(var item in new UnityEngine.Object[]{room,visual?.gameObject,weaponRoot,cameraRoot,shadow?.gameObject,target,both,leftOnly,matchedLeft,sheet})
                    if(item!=null) UnityEngine.Object.DestroyImmediate(item);
            }
        }
        private static Texture2D Capture(Camera camera,RenderTexture target)
        {
            CombatHudRender.RenderReserveCamera(camera,target);RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image;
        }
    }
}
