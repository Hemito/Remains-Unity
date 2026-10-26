using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class ActorFlashShadowExperimentChecks
    {
        public static void RunBatch()
        {
            try
            {
                ActorShadowSourceChecks.Run();
                WindowActorCompositeChecks.RunGroupedWindows();
                WindowActorCompositeChecks.RunLampProjection();
                // Includes ActorCastShadowAuthoring.Check and the queued-first-frame timing proof.
                ActorProjectedShadowChecks.RunTimingChecks();
                RenderHubExperiment();
                Debug.Log("ActorFlashShadowExperimentChecks: PASS — отдельная маска вкладa вспышки, исходный свет актёра, первый кадр и настоящий хаб.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RenderBatch()
        {
            try {RenderHubExperiment();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void RenderHubExperiment()
        {
            GameObject room=null,weaponRoot=null,flashRoot=null,cameraRoot=null,labelRoot=null;
            PonyVisual visual=null;ActorCastShadowView shadow=null;
            var frames=new Texture2D[3];RenderTexture target=null;Texture2D normal=null,maskedNormal=null,contactSheet=null;
            var oldTarget=RenderTexture.active;
            try
            {
                var layout=RoomLayout.Load("hub");room=AuthoredRoomVisual.Create("hub",layout);
                var level=room.GetComponent<FactoryLevelView>();level.Focus(new Vector2(10,5));
                Require(level.IsReady,"Настоящий хаб не подготовил поле света.");
                ActorTypes.Load();PropCatalog.Load();Anatomy.Load();WeaponCatalog.Load();PonyVisual.Prepare();
                var actor=new ActorState{id="flash-shadow-experiment",actorType="pony",anatomy="pony",player=true,connected=true,
                    level="hub",pose="idle",x=10,y=2,facing=1,weapon="pistol",weaponAngle=0,weaponReach=.4f,
                    appearance=new CharacterAppearance{version=CharacterAppearance.CurrentVersion}};
                visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.ConfigureMagicPreview(0);visual.Bind(actor);visual.DrawPose(0);
                Require(visual.TryGetDrawnBounds(out var bodyBounds),"Пони не передал реальные границы изображения.");
                if(!FreeBody(layout,bodyBounds))
                {
                    bool placed=false;
                    foreach(var candidate in new[]{new Vector2(8,2),new Vector2(18,2),new Vector2(10,5),new Vector2(22,2),new Vector2(layout.spawnX,layout.spawnY)})
                    {
                        actor.x=candidate.x;actor.y=candidate.y;visual.DrawPose(0);
                        if(visual.TryGetDrawnBounds(out bodyBounds) && FreeBody(layout,bodyBounds)) {placed=true;break;}
                    }
                    Require(placed,"Кандидаты позиции пони пересекают физические стены хаба.");
                    Debug.Log($"Эксперимент тени: позиция (10,2) занята; выбрана проверенная свободная позиция ({actor.x},{actor.y}).");
                }
                float floor=float.NegativeInfinity;
                foreach(var surface in layout.platforms)
                    if(surface.slopeDirection==0 && bodyBounds.center.x>surface.x && bodyBounds.center.x<surface.x+surface.width &&
                        surface.y+surface.height<=bodyBounds.min.y+.03f)
                        floor=Mathf.Max(floor,surface.y+surface.height);
                Require(float.IsFinite(floor),"Под диагностической пони не найдена опорная поверхность.");
                actor.y+=floor+.01f-bodyBounds.min.y;visual.DrawPose(0);
                Require(visual.TryGetDrawnBounds(out bodyBounds) && FreeBody(layout,bodyBounds),"Пони пересекает стену после установки на опорную поверхность.");
                visual.GetComponent<SortingGroup>().sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player);
                var artwork=WeaponArtwork.Load();weaponRoot=new GameObject("Пистолет эксперимента");var weapon=weaponRoot.AddComponent<SpriteRenderer>();
                SpriteArtworkMaterial.Apply(weapon,artwork["pistol"]);var pose=WeaponPose.SampleRanged(actor);
                weaponRoot.transform.position=pose.Root;weaponRoot.transform.rotation=Quaternion.Euler(0,0,pose.Angle);
                weaponRoot.transform.localScale=new Vector3(1,pose.Mirrored?-1:1,1);weapon.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player)+1;
                var flashSprite=AssetDatabase.LoadAssetAtPath<Sprite>(MuzzleFlashAuthoring.Root+"pistol-flash-1.png")
                    ??throw new InvalidOperationException("Нет исходного рисунка вспышки пистолета.");
                flashRoot=new GameObject("Исходная вспышка эксперимента");var flash=flashRoot.AddComponent<SpriteRenderer>();
                SpriteArtworkMaterial.Prepare(flashSprite);SpriteArtworkMaterial.Apply(flash,flashSprite);SpriteArtworkMaterial.SetSelfIlluminated(flash);
                flash.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Effect);
                flashRoot.transform.position=weaponRoot.transform.TransformPoint(new Vector3(.602f,.122f,0));
                flashRoot.transform.rotation=weaponRoot.transform.rotation;flashRoot.transform.localScale=weaponRoot.transform.localScale;
                var sourcePosition=(Vector2)flashRoot.transform.TransformPoint(flashSprite.bounds.center);
                var emission=new[]{new SpriteEmission(flashSprite,flashRoot.transform.localToWorldMatrix,8)};
                var parts=new List<LightOccluder>();visual.AppendLightOccluders(parts);
                parts.Add(new LightOccluder(weapon.sprite,weaponRoot.transform.localToWorldMatrix));
                shadow=UnityEngine.Object.Instantiate(GameAssets.Load<ActorCastShadowView>(ActorCastShadowView.ResourcePath)
                    ??throw new InvalidOperationException("Нет авторского префаба проекции тени."));
                const string sourceId="experiment:actual-pistol-flash";
                var sources=new[]{new ActorShadowSource(sourceId,sourcePosition,flashSprite.bounds.size.magnitude*.25f,8f/9f,true,
                    ActorShadowPresentation.FlashProjectionScale(level.ShadowSettings))};
                shadow.Prepare(parts,1);shadow.Draw(bodyBounds.center,parts,sources,level.ShadowSettings);
                var masks=new List<ProjectedLightShadow>();shadow.AppendProjectedLightShadows(masks);
                Require(masks.Count==1,"Тень вспышки не экспортировала ровно одну собственную маску.");
                foreach(var renderer in shadow.GetComponentsInChildren<Renderer>()) Require(!renderer.enabled,"В эксперименте присутствует чёрный overlay вспышки.");

                const int layer=31;
                foreach(var root in new[]{room,visual.gameObject,weaponRoot,flashRoot,shadow.gameObject})
                    foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
                cameraRoot=new GameObject("Камера настоящего хаба");var camera=cameraRoot.AddComponent<Camera>();
                var frameBounds=bodyBounds;frameBounds.Encapsulate(weapon.bounds);frameBounds.Encapsulate(flash.bounds);
                foreach(var projected in masks)
                {frameBounds.Encapsulate(new Vector3(projected.WorldBounds.xMin,projected.WorldBounds.yMin,0));frameBounds.Encapsulate(new Vector3(projected.WorldBounds.xMax,projected.WorldBounds.yMax,0));}
                // The floor is not a wall receiver: keep the visible wall, rather than framing a
                // mathematically projected silhouette that is correctly clipped below the floor.
                frameBounds.SetMinMax(new Vector3(frameBounds.min.x,floor-.15f,0),frameBounds.max);
                camera.enabled=false;camera.orthographic=true;camera.aspect=2;
                camera.orthographicSize=Mathf.Max(frameBounds.size.y*.62f,frameBounds.size.x*.31f);
                camera.transform.position=new Vector3(frameBounds.center.x,frameBounds.center.y,-10);camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
                target=new RenderTexture(1024,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
                labelRoot=new GameObject("Подпись кадра эксперимента");labelRoot.layer=layer;var label=labelRoot.AddComponent<TextMesh>();
                label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=48;label.characterSize=.045f;
                label.anchor=TextAnchor.UpperLeft;label.color=Color.white;
                var labelRenderer=label.GetComponent<MeshRenderer>();labelRenderer.sharedMaterial=label.font.material;labelRenderer.sortingOrder=short.MaxValue;
                labelRoot.transform.position=camera.transform.position+new Vector3(-camera.orthographicSize*1.94f,camera.orthographicSize*.92f,10);
                flash.enabled=false;label.text="До выстрела";
                level.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
                frames[0]=Capture(camera,target);
                flash.enabled=true;label.text="Вспышка без тени";
                level.RenderMuzzleFlashes(emission,Array.Empty<ProjectedLightShadow>(),new[]{sourceId});
                frames[1]=Capture(camera,target);normal=ReadTexture(Shader.GetGlobalTexture("_RemainsRadiance"));
                label.text="Вспышка с тенью";
                level.RenderMuzzleFlashes(emission,masks,new[]{sourceId});
                frames[2]=Capture(camera,target);maskedNormal=ReadTexture(Shader.GetGlobalTexture("_RemainsRadiance"));
                RequireEqual(normal.GetPixels(),maskedNormal.GetPixels(),"Маска фона изменила поле освещения актёра.");
                var baseline=frames[0].GetPixels();var lit=frames[1].GetPixels();var masked=frames[2].GetPixels();
                int flashChanged=0,shadowChanged=0;
                // Ignore caption rows: only actual hub pixels count as evidence.
                for(int y=0;y<430;y++) for(int x=0;x<1024;x++)
                {
                    int index=y*1024+x;
                    if(Difference(baseline[index],lit[index])>.02f) flashChanged++;
                    if(Difference(lit[index],masked[index])>.01f) shadowChanged++;
                }
                Require(flashChanged>50,$"Настоящая вспышка не изменила изображение хаба: пикселей={flashChanged}.");
                Require(shadowChanged>20,$"Настоящая маска не изменила освещённый фон хаба: пикселей={shadowChanged}.");
                contactSheet=new Texture2D(3072,512,TextureFormat.RGBA32,false);
                for(int frame=0;frame<3;frame++) contactSheet.SetPixels(frame*1024,0,1024,512,frames[frame].GetPixels());
                contactSheet.Apply();string export=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/wall-shadow-experiment.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(export));File.WriteAllBytes(export,contactSheet.EncodeToPNG());
                File.WriteAllText(Path.ChangeExtension(export,".txt"),$"Настоящий хаб, PonyVisual, pistol и исходная вспышка. Рендер активного pipeline, не AI и не искусственный чёрный фон.\nСлева: до выстрела; центр: вспышка без маски; справа: тот же выстрел с отдельной маской собственного вклада.\nПозиция актёра ({actor.x},{actor.y}), bounds={bodyBounds}; физические стены не пересечены.\nИсточников={sources.Length}, масок={masks.Count}, силуэтов={parts.Count}, projectionScale={sources[0].ProjectionScale}, coverage={masks[0].Opacity}.\nИзменений изображения от вспышки={flashChanged}, от тени={shadowChanged}; поле освещения актёра совпадает с исходным.\n");
                Debug.Log($"Эксперимент в настоящем хабе: PASS — flashPixels={flashChanged}, shadowPixels={shadowChanged}, actor fluence unchanged; {export}.");
            }
            finally
            {
                RenderTexture.active=oldTarget;if(target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}
                foreach(var frame in frames) if(frame!=null) UnityEngine.Object.DestroyImmediate(frame);
                foreach(var obj in new UnityEngine.Object[]{normal,maskedNormal,contactSheet,cameraRoot,labelRoot,flashRoot,weaponRoot,
                    shadow!=null?shadow.gameObject:null,visual!=null?visual.gameObject:null,room}) if(obj!=null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }
        private static bool FreeBody(RoomLayout room,Bounds bounds)
        {
            // Test the inner picture rectangle, ignoring a floor touching its outside edge.
            var body=Rect.MinMaxRect(bounds.min.x+.03f,bounds.min.y+.03f,bounds.max.x-.03f,bounds.max.y-.03f);
            foreach(var surface in room.platforms)
                if(!surface.oneWay && surface.slopeDirection==0 && body.Overlaps(new Rect(surface.x,surface.y,surface.width,surface.height))) return false;
            return true;
        }
        private static Texture2D Capture(Camera camera,RenderTexture target)
        {
            CombatHudRender.RenderReserveCamera(camera,target);var previous=RenderTexture.active;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image;}
            finally {RenderTexture.active=previous;}
        }
        private static Texture2D ReadTexture(Texture source)
        {
            Require(source is RenderTexture,"Не опубликована настоящая GPU-текстура освещения актёра.");
            var target=(RenderTexture)source;var previous=RenderTexture.active;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image;}
            finally {RenderTexture.active=previous;}
        }
        private static float Difference(Color a,Color b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);
        private static void RequireEqual(Color[] a,Color[] b,string message)
        {Require(a.Length==b.Length,message);for(int i=0;i<a.Length;i++) Require(Difference(a[i],b[i])<.0001f,$"{message} Тексель={i}, было={a[i]}, стало={b[i]}.");}
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
