using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyArtworkBoundsChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();ActorTypes.Load();Anatomy.Load();PonyVisual.Prepare();
            var actor=new ActorState {id="artwork-bounds-check",actorType="pony",anatomy="pony",pose="idle",x=3,y=2,
                appearance=new CharacterAppearance {version=1,hairStyle=1,eyeStyle=1}};
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);int samples=0;
            try
            {
                visual.Bind(actor);
                Require(!visual.TryGetDrawnBounds(out _),"Bind выдал старые/неотрисованные границы");
                foreach(int hair in new[]{1,PonyAnimationData.Library.hairStyles}) foreach(bool secondary in new[]{false,true})
                {
                    actor.appearance.hairStyle=hair;actor.appearance.secondaryHairEnabled=secondary;actor.appearance.eyeStyle=secondary?PonyAnimationData.Library.eyeStyles:1;
                    foreach(var clip in PonyAnimationData.Library.clips) foreach(int facing in new[]{-1,1})
                    {
                        actor.pose=clip.id;actor.facing=facing;actor.climbing=clip.id=="climb";actor.vy=actor.climbing?1:0;
                        for(int frame=0;frame<clip.frames.Length;frame++)
                        {
                            float time=(frame+.25f)/clip.fps;visual.DrawPose(time,true);
                            VerifyVertices(visual);samples++;
                            var selected=PonyRenderSelection.Frames(PonyAppearance.Snapshot(actor),visual.DrawnClip)[visual.DrawnFrame];
                            Require(visual.DrawnPartCount==selected.Length,"Фактическое включение частей расходится с выбранным кадром: "+clip.id);
                        }
                    }
                }
                actor.appearance.hairStyle=1;actor.appearance.secondaryHairEnabled=false;actor.climbing=false;actor.vy=0;actor.facing=1;actor.pose="idle";
                visual.DrawPose(0,true);Require(visual.TryGetDrawnBounds(out var before),"Нет idle рисунка");
                var hull=InventorySource.BodyBounds(actor);
                Require(before.max.y>hull.yMax,"Fixture не воспроизводит выступающий над hull рисунок головы/рога");
                actor.x+=2;actor.y+=.5f;visual.DrawPose(0,true);
                Require(visual.TryGetDrawnBounds(out var moved) && Near(moved.min,before.min+new Vector3(2,.5f,0)) && Near(moved.max,before.max+new Vector3(2,.5f,0)),
                    "Повторный кадр не обновил Transform bounds");
                actor.facing=-1;visual.DrawPose(0,true);Require(visual.TryGetDrawnBounds(out var flipped),"Нет flipped рисунка");
                Require(Mathf.Abs(flipped.min.x-(actor.x-(moved.max.x-actor.x)))<.00002f && Mathf.Abs(flipped.max.x-(actor.x-(moved.min.x-actor.x)))<.00002f,
                    "Flip сохранил прежний X footprint");VerifyVertices(visual);
                var drawn=flipped;actor.pose="climb";actor.poseTime=9;
                Require(visual.TryGetDrawnBounds(out var unsampled) && unsampled==drawn,"Getter пересэмплирует состояние вместо actual drawing");
                actor.climbing=true;actor.vy=0;visual.DrawPose(0);
                Require(visual.DrawnClip=="climb" && visual.DrawnFrame==0,"Не проверен фактический неподвижный кадр лестницы");VerifyVertices(visual);
                visual.transform.rotation=Quaternion.Euler(0,0,17);visual.DrawPose(0);VerifyVertices(visual);
                visual.transform.rotation=Quaternion.identity;visual.DrawPose(0);
                var renderers=visual.GetComponentsInChildren<MeshRenderer>(true);var active=renderers.Where(r=>r.enabled).ToArray();
                foreach(var renderer in active) renderer.enabled=false;
                visual.DrawPose(0);Require(!visual.TryGetDrawnBounds(out _) && visual.DrawnPartCount==0,"Отключённые части защищают несуществующий рисунок");
                active[0].enabled=true;visual.DrawPose(0);VerifyVertices(visual);
                Require(visual.DrawnPartCount==1,"Не проверено одиночное фактическое включение части");
                active[0].forceRenderingOff=true;visual.DrawPose(0);Require(!visual.TryGetDrawnBounds(out _),"forceRenderingOff не исключён");
                active[0].forceRenderingOff=false;active[0].gameObject.SetActive(false);visual.DrawPose(0);
                Require(!visual.TryGetDrawnBounds(out _),"Скрытый child остался в footprint");
                active[0].gameObject.SetActive(true);visual.DrawPose(0);VerifyVertices(visual);
                visual.SetVisible(false);Require(!visual.TryGetDrawnBounds(out _),"Скрытый актёр выдал actual рисунок");
                visual.SetVisible(true);visual.DrawPose(0);VerifyVertices(visual);
                visual.enabled=false;Require(visual.TryGetDrawnBounds(out _),"Отключение animation component потеряло всё ещё видимые Renderer");visual.enabled=true;
                visual.Bind(new ActorState {id="reused-other-actor",pose="idle"});Require(!visual.TryGetDrawnBounds(out _),"Новый Bind сохранил footprint старого actor");
                CheckHeldSprite();
                Debug.Log($"Границы рисунка пони: PASS — {samples} фактических sample разных поз/внешности/направлений, вершины готовых meshes, same-frame Transform/flip, лестница, disabled/hidden/forceRenderingOff, смена actor и отдельный удерживаемый Sprite. Проверка игрового окна/кадра выполняется отдельно.");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }
        private static void VerifyVertices(PonyVisual visual)
        {
            Require(visual.TryGetDrawnBounds(out var drawn),"Нет actual footprint: "+visual.ActorId+", "+visual.DrawnClip+":"+visual.DrawnFrame);
            bool first=true;Bounds rendererUnion=default;int count=0;
            foreach(var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;if(mesh==null) continue;
                if(first) {rendererUnion=renderer.bounds;first=false;} else rendererUnion.Encapsulate(renderer.bounds);count++;
                // Independent actual vertices, no PonyMotionFrames or copied pose matrices.
                foreach(var vertex in mesh.vertices)
                {
                    var point=renderer.transform.TransformPoint(vertex);var minimum=drawn.min;var maximum=drawn.max;
                    Require(point.x>=minimum.x-.00002f && point.x<=maximum.x+.00002f && point.y>=minimum.y-.00002f && point.y<=maximum.y+.00002f,
                        "Actual вершина рисунка вышла из reservation: "+visual.DrawnClip+":"+visual.DrawnFrame+", part="+renderer.name+", point="+point+", bounds="+drawn);
                }
            }
            Require(!first && count==visual.DrawnPartCount && Near(drawn.min,rendererUnion.min) && Near(drawn.max,rendererUnion.max),"Drawn cache расходится с actual enabled Renderer bounds");
        }
        private static void CheckHeldSprite()
        {
            var root=new GameObject("Удерживаемый Sprite проверки",typeof(SpriteRenderer));var texture=new Texture2D(20,10);
            texture.SetPixels(Enumerable.Repeat(Color.white,200).ToArray());texture.Apply();
            var sprite=Sprite.Create(texture,new Rect(0,0,20,10),new Vector2(.5f,.5f),10);
            try
            {
                var renderer=root.GetComponent<SpriteRenderer>();renderer.sprite=sprite;root.transform.position=new Vector3(6,3,0);root.transform.rotation=Quaternion.Euler(0,0,35);
                var actual=ActorArtworkBounds.VisibleSprite(renderer);
                Require(actual.HasValue && actual.Value==ActorArtworkBounds.WorldRect(renderer.bounds),"Удерживаемый Sprite использует range/иной pose вместо actual bounds");
                renderer.enabled=false;Require(!ActorArtworkBounds.VisibleSprite(renderer).HasValue,"Отключённое оружие защищает пустую область");
                renderer.enabled=true;renderer.color=new Color(1,1,1,0);Require(!ActorArtworkBounds.VisibleSprite(renderer).HasValue,"Невидимый Sprite остался в footprint");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }
        public static int RunPure()
        {
            var bounds=new Bounds(new Vector3(-3,4,0),new Vector3(2,6,0));
            Require(ActorArtworkBounds.WorldRect(bounds)==new Rect(-4,1,2,6),"Изменено XY преобразование фактического bounds");
            bool rejected=false;try {ActorArtworkBounds.WorldRect(new Bounds(Vector3.zero,Vector3.zero));} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Пустые границы рисунка приняты");return 2;
        }
        private static bool Near(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)<.00002f;
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Границы рисунка персонажа: "+reason);}
    }
}
