using System;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponShotChecks
    {
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            var root=new GameObject("Кадры оружия");var session=root.AddComponent<CoopSession>();var world=new WorldState();
            var actor=new ActorState {id="render",owner="local",player=true,connected=true,level="hub",weapon="pistol",equippedWeaponItem="render-item"};world.actors.Add(actor);
            typeof(CoopSession).GetProperty("World").SetValue(session,world);
            typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
            var animation=root.AddComponent<WeaponShotAnimation>();animation.Initialize(session);var artwork=WeaponArtwork.Load();
            int row=0;
            foreach(string weapon in new[]{"pistol","energy"})
            {
                actor.weapon=weapon;var rest=artwork[weapon];
                animation.Observe(Shot(world,actor,weapon));
                int count=weapon=="pistol"?1:4;
                for(int i=0;i<=count;i++)
                {
                    var view=UnityEngine.Object.Instantiate(Remains.Content.WeaponRigs.Prefab(weapon));
                    view.SetFrame(i==0?rest:animation.Select(actor,rest));
                    view.transform.position=new Vector3(i*2,-row*1.3f,0);
                    if(i>0) animation.Step(1f/30);
                }
                row++;
            }
            var camera=new GameObject("Проверка импорта кадров").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.5f;
            camera.transform.position=new Vector3(4,-.6f,-10);camera.backgroundColor=new Color(.13f,.15f,.17f);
            var target=new RenderTexture(1500,450,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/tests/weapon-shot-frames.png"));
                System.IO.File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Кадры оружия сохранены: "+path);
            }
            finally {RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void Run()
        {
            CheckAssets();
            foreach(string weapon in new[]{"pistol","energy"})
            {
                var root=new GameObject("Проверка кадров выстрела");
                try
                {
                    var session=root.AddComponent<CoopSession>();var world=new WorldState();
                    var actor=new ActorState {id="shot-view",owner="local",player=true,connected=true,level="hub",weapon=weapon,equippedWeaponItem="shot-item"};world.actors.Add(actor);
                    typeof(CoopSession).GetProperty("World").SetValue(session,world);
                    typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                    var animation=root.AddComponent<WeaponShotAnimation>();animation.Initialize(session);
                    var rest=WeaponArtwork.Load()[weapon];
                    var attack=Shot(world,actor,"shot");
                    Require(animation.Select(actor,rest)==rest,"Покой заменён до выстрела");
                    animation.Observe(attack);var frame=animation.Select(actor,rest);
                    Require(frame!=rest,"Нет исходного кадра выстрела");
                    if(weapon=="pistol")
                    {
                        var matrix=Matrix4x4.TRS(new Vector3(3,4,0),Quaternion.Euler(0,0,90),new Vector3(1,-1,1));
                        Require(animation.TryEmission(actor,frame,matrix,out var emission),"Нет отдельной маски света пистолета");
                        var asset=GameAssets.Load<WeaponAsset>("Content/weapons/pistol");
                        float expected=GameAssets.Load<MuzzleFlashCatalog>("Content/MuzzleFlashCatalog").Read()[asset.CreateDefinition().ammo]*asset.MuzzleFlashGain;
                        Require(emission.Gain==expected && emission.Sprite!=frame,"Свет получил корпус оружия или неверный профиль патрона");
                        Require(Vector3.Distance(emission.LocalToWorld.MultiplyPoint3x4(Vector3.zero),matrix.MultiplyPoint3x4(new Vector3(.602f,.122f,0)))<.00001f,
                            "Маска света потеряла поворот, зеркальность или исходную привязку");
                        Require(!animation.TryEmission(actor,rest,matrix,out _),"Свет остался при показе обычного кадра");
                    }
                    var rig=Remains.Content.WeaponRigs.Get(weapon);
                    Require(Mathf.Abs(frame.pivot.x-(rig.x+rest.pivot.x))<.001f && Mathf.Abs(frame.pivot.y-(rig.y+rest.pivot.y))<.001f,
                        "При замене кадра смещается графическая точка оружия");
                    animation.Step(1f/60);animation.Observe(attack);
                    animation.Step((weapon=="pistol"?1f:4f)/30-1f/60+.001f);
                    Require(animation.Select(actor,rest)==rest,"Дубликат продлил вспышку или анимация не завершилась");
                    Require(!animation.TryEmission(actor,frame,Matrix4x4.identity,out _),"Свет остался после завершения кадра");
                    attack.id="other-level";attack.level="factory:0";animation.Observe(attack);
                    Require(animation.Select(actor,rest)==rest,"Выстрел другого уровня появился локально");
                    attack.id="new-shot";attack.level="hub";animation.Observe(attack);actor.generation++;
                    Require(animation.Select(actor,rest)==rest,"Смена поколения оставила старую вспышку");
                    Require(!animation.TryEmission(actor,frame,Matrix4x4.identity,out _),"Смена поколения оставила старый свет");
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
            Debug.Log("Кадры выстрела: PASS — привязка, длительность, дубли, уровень и поколение.");
        }
        private static AttackEvent Shot(WorldState world,ActorState actor,string id)
            =>new() {id=id,attacker=actor.id,sourceOwner=actor.owner,weapon=actor.weapon,level=actor.level,
                rangedPose=Remains.Gameplay.WeaponPose.SampleRanged(actor).Capture(actor,world.worldId)};
        private static void CheckAssets()
        {
            var pistol=GameAssets.Load<WeaponAsset>("Content/weapons/pistol");
            var npc=GameAssets.Load<WeaponAsset>("Content/weapons/npc-shot");
            Require(pistol.ShotAnimation!=null && npc.ShotAnimation==pistol.ShotAnimation,"NPC и пистолет потеряли общую анимацию");
            foreach(var weapon in GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog").AnimatedWeapons())
            {
                var clip=weapon.ShotAnimation;if(clip.Deferred) continue;   // frames of the export: FirearmChecks
                var frames=clip.CopyFrames();var first=frames[0];
                frames[0]=null;Require(clip.CopyFrames()[0]==first,"Рабочий массив изменил SO анимации");
            }
            var test=ScriptableObject.CreateInstance<WeaponShotClip>();
            var rest=pistol.Presentation.Artwork.sprite;
            var wrong=Sprite.Create(rest.texture,rest.rect,Vector2.zero,rest.pixelsPerUnit*2);
            try
            {
                test.Configure(30,new[]{wrong});bool rejected=false;
                try {test.ValidateAgainst(rest);} catch(InvalidOperationException) {rejected=true;}
                Require(rejected,"Несогласованные масштаб и привязка кадра приняты");
            }
            finally {UnityEngine.Object.DestroyImmediate(test);UnityEngine.Object.DestroyImmediate(wrong);}
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Кадры выстрела: "+reason);}
    }
}
