using System;
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
            var actor=new ActorState {id="render",owner="local",player=true,connected=true,level="hub",weapon="pistol"};world.actors.Add(actor);
            typeof(CoopSession).GetProperty("World").SetValue(session,world);
            typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
            var animation=root.AddComponent<WeaponShotAnimation>();animation.Initialize(session);var artwork=WeaponArtwork.Load();
            int row=0;
            foreach(string weapon in new[]{"pistol","energy"})
            {
                actor.weapon=weapon;var rest=artwork[weapon];
                animation.Observe(new AttackEvent {id=weapon,attacker=actor.id,weapon=weapon,level="hub"});
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
            foreach(string weapon in new[]{"pistol","energy"})
            {
                var root=new GameObject("Проверка кадров выстрела");
                try
                {
                    var session=root.AddComponent<CoopSession>();var world=new WorldState();
                    var actor=new ActorState {id="shot-view",owner="local",player=true,connected=true,level="hub",weapon=weapon};world.actors.Add(actor);
                    typeof(CoopSession).GetProperty("World").SetValue(session,world);
                    typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                    var animation=root.AddComponent<WeaponShotAnimation>();animation.Initialize(session);
                    var rest=WeaponArtwork.Load()[weapon];
                    var attack=new AttackEvent {id="shot",attacker=actor.id,weapon=weapon,level="hub"};
                    Require(animation.Select(actor,rest)==rest,"Покой заменён до выстрела");
                    animation.Observe(attack);var frame=animation.Select(actor,rest);
                    Require(frame!=rest,"Нет исходного кадра выстрела");
                    var rig=Remains.Content.WeaponRigs.Get(weapon);
                    Require(Mathf.Abs(frame.pivot.x-(rig.x+rest.pivot.x))<.001f && Mathf.Abs(frame.pivot.y-(rig.y+rest.pivot.y))<.001f,
                        "При замене кадра смещается графическая точка оружия");
                    animation.Step(1f/60);animation.Observe(attack);
                    animation.Step((weapon=="pistol"?1f:4f)/30-1f/60+.001f);
                    Require(animation.Select(actor,rest)==rest,"Дубликат продлил вспышку или анимация не завершилась");
                    attack.id="other-level";attack.level="factory:0";animation.Observe(attack);
                    Require(animation.Select(actor,rest)==rest,"Выстрел другого уровня появился локально");
                    attack.id="new-shot";attack.level="hub";animation.Observe(attack);actor.generation++;
                    Require(animation.Select(actor,rest)==rest,"Смена поколения оставила старую вспышку");
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
            Debug.Log("Кадры выстрела: PASS — привязка, длительность, дубли, уровень и поколение.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Кадры выстрела: "+reason);}
    }
}
