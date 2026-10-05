using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class RaiderWeaponSocketChecks
    {
        public static void Run()
        {
            var data=AtlasAnimationData.RequirePrepared(ActorTypes.Get("raider").artwork);
            Require(ActorTypes.Get("raider").atlasWeaponSocket,"Рейдер не использует точку кадра");
            var actor=new ActorState {id="socket-check",actorType="raider",anatomy="raider",weapon="pistol",x=10,y=2,weaponReach=1.4f};
            var visual=(AtlasActorVisual)ActorVisualFactory.Create(actor);var gun=UnityEngine.Object.Instantiate(WeaponRigs.Prefab(actor.weapon));
            int checkedFrames=0;
            try
            {
                visual.Bind(actor);
                foreach(var clip in data.clips)
                for(int frame=0;frame<clip.count;frame++)
                {
                    actor.pose=clip.pose;actor.poseTime=(frame+.25f)/clip.framesPerSecond;
                    // Down is time sampled; the airborne clip alone reads velocity.
                    if(clip.UsesMotion) actor.vy=frame<=7?7.2f*(1-frame/7f):-7.2f*(frame-7)/8f;
                    actor.climbing=clip.pose=="climb";if(actor.climbing) actor.vy=1;
                    foreach(int facing in new[]{-1,1})
                    {
                        actor.facing=facing;visual.Draw();
                        int drawnFrame=(int)(visual.GetComponent<SpriteRenderer>().sprite.rect.x/data.cellWidth);
                        var point=clip.weaponSockets[drawnFrame];
                        var expected=actor.Position+new Vector2((point.x-60)/50*facing,(110-point.y)/50);
                        Require(Vector2.Distance(WeaponPose.RangedAnchor(actor),expected)<.00001f,$"Точка расходится с кадром рисунка: {clip.pose}:{frame}:{facing}");
                        var target=expected+new Vector2(facing*5,1);
                        NpcWeaponAim.Apply(actor,target);
                        Require(actor.weaponReach==0 && Vector2.Distance(WeaponPose.Mount(actor),expected)<.00001f,"Применён радиус игрока к NPC");
                        ApplyView(gun,actor);
                        Require(Vector2.Distance(gun.Muzzle.position,WeaponPose.Muzzle(actor))<.00001f,"Дуло изображения расходится с выстрелом");
                        var delta=target-WeaponPose.Muzzle(actor);
                        Require(delta.magnitude>0 && Vector2.Dot(delta.normalized,WeaponPose.Direction(actor))>.99999f,"Ствол не направлен в цель");
                        checkedFrames++;
                    }
                }
                actor.pose="idle";actor.climbing=false;actor.poseTime=0;actor.facing=-1;
                NpcWeaponAim.Apply(actor,null);Require(actor.weaponFlipY && WeaponPose.Direction(actor).x<-.999f,"Покой смотрит против направления тела");
                NpcWeaponAim.Apply(actor,actor.Position+new Vector2(5,.55f));
                Require(actor.facing==1 && !actor.weaponFlipY,"Поиск/видимая цель не повернули тело и пистолет вместе");
                NpcWeaponAim.Apply(actor,actor.Position+new Vector2(-5,.55f));
                Require(actor.facing==-1 && actor.weaponFlipY,"Смена стороны цели не отразила пистолет");
                var player=new ActorState {id="player-socket-regression",actorType="pony",player=true,weapon="pistol"};
                WeaponPose.Aim(player,new Vector2(10,2));
                Require(WeaponPose.RangedReachLimit(player)>0 && player.weaponReach>0 && !AtlasWeaponSockets.Enabled(player),"У игрока исчез радиус удержания");
                ExpectRejected(new AtlasActorClip {pose="missing",count=2},"пустые обязательные точки");
                ExpectRejected(new AtlasActorClip {pose="short",count=2,weaponSockets=new[]{new AtlasWeaponSocketPoint()}},"неполные точки");
                ExpectRejected(new AtlasActorClip {pose="nan",count=1,weaponSockets=new[]{new AtlasWeaponSocketPoint {x=float.NaN}}},"нечисловая точка");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);UnityEngine.Object.DestroyImmediate(gun.gameObject);}
            Debug.Log($"RaiderWeaponSocketChecks PASS: {checkedFrames} проверок кадра/отражения, общие mount/muzzle, покой/смена цели и прежнее удержание игрока.");
        }
        private static void ExpectRejected(AtlasActorClip clip,string reason)
        {
            bool rejected=false;try {AtlasWeaponSockets.ValidateClip(clip,"контракт",required:true);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,reason+" приняты без диагностики");
        }
        private static void ApplyView(WeaponView gun,ActorState actor)
        {
            gun.transform.position=WeaponPose.Mount(actor);gun.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);gun.SetMirrored(WeaponPose.Flip(actor));
        }
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Проверка крепления оружия рейдера");int column=0;
            foreach(string pose in new[]{"idle","walk","run","jump","climb","down"})
            foreach(int facing in new[]{1,-1})
            {
                var actor=new ActorState {id=pose+facing,actorType="raider",anatomy="raider",weapon="pistol",facing=facing,
                    pose=pose,poseTime=.1f,x=column/2*2.8f,y=facing==1?3.6f:0,climbing=pose=="climb",vy=pose=="climb"?1:0};
                var view=(AtlasActorVisual)ActorVisualFactory.Create(actor);view.transform.SetParent(root.transform);view.Bind(actor);view.Draw();
                var gun=UnityEngine.Object.Instantiate(WeaponRigs.Prefab(actor.weapon),root.transform);
                NpcWeaponAim.Apply(actor,null);ApplyView(gun,actor);column++;
            }
            var camera=new GameObject("Камера точек оружия").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=5;
            camera.transform.position=new Vector3(7,2.8f,-10);camera.backgroundColor=new Color(.16f,.17f,.19f);
            var target=new RenderTexture(2560,1440,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/raider-weapon-sockets.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Кадр крепления оружия сохранён: "+path);
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("RaiderWeaponSocketChecks: "+reason);}
    }
}
