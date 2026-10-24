using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class LaserImpactChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void Render()
        {
            WeaponCatalog.Load();Anatomy.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Проверка лазера");var session=root.AddComponent<CoopSession>();
            var actor=new ActorState {id="observer",level="hub",x=-2,player=true,connected=true,owner="local"};
            var world=new WorldState();world.actors.Add(actor);
            world.actors.Add(new ActorState {id="target",actorType="training-dummy",level="hub",x=2});
            typeof(CoopSession).GetProperty("World").SetValue(session,world);
            typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
            var geometry=new SimulationGeometry(_=>
                new RoomLayout {platforms=new[]{new RoomSurface {x=2,y=0,width=.2f,height=2,texture="Hub/concrete"}}});
            geometry.Reconcile(world);
            var controller=new ProjectileController(session,geometry,(_,_,_)=>{});
            ProjectileContact? lastContact=null;int contacts=0;
            controller.ContactObserved+=contact=>{lastContact=contact;contacts++;};
            var orders=new WorldVisualOrderTable();orders.Reconcile(world,actor.level);
            var effects=root.AddComponent<LaserImpactEffects>();effects.Initialize(session,controller,orders);
            world.projectiles.Add(new ProjectileState {launch=new AttackEvent {id="laser-check",weapon="energy",level="hub",
                attacker="source",sourceOwner="remote",sourcePlayer=true,originY=1,endX=6,endY=1},owner="remote",generation=1,flightSpeed=1200});
            typeof(ProjectileController).GetMethod("Reconcile",Private).Invoke(controller,null);
            controller.Step(.01f);controller.Step(.01f);
            var sprites=root.GetComponentsInChildren<SpriteRenderer>();
            var prefab=GameAssets.Load<LaserImpactView>("Presentation/Effects/laser-impact");
            Require(prefab!=null && sprites.Length==1 && sprites[0].sprite==prefab.Frames[0] &&
                sprites[0].sharedMaterial==prefab.Artwork.sharedMaterial && !string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(prefab.Frames[0])),
                "Вспышка не использует сохранённые ресурсы префаба");
            Require(sprites.Length==1 && sprites[0].transform.position.x>1 && sprites[0].transform.position.x<3,"Контакт должен создать одну вспышку на мишени");
            Require(contacts==1 && lastContact.Value.target=="target","Вместо мишени зарегистрирована стена за ней");
            var paused=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",Private).GetValue(session.Pause);
            paused.Add("hub");effects.Step(1);Require(root.GetComponentsInChildren<SpriteRenderer>().Length==1,"Пауза погасила вспышку");paused.Clear();
            var camera=new GameObject("Камера").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1;
            camera.transform.position=new Vector3(2,1,-10);camera.backgroundColor=new Color(.15f,.2f,.25f);
            var target=new RenderTexture(600,400,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(600,400,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,600,400),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/laser-impact.png")),image.EncodeToPNG());
                effects.Step(.04f);Require(sprites[0].sprite.texture.name=="laser-impact-2","Неверный второй кадр");
                effects.Step(.07f);Require(root.GetComponentsInChildren<SpriteRenderer>().Length==0,"Завершённая вспышка осталась в сцене");
                world.actors.RemoveAll(value=>value.id=="target");
                var wallShot=world.projectiles[0].Copy();wallShot.launch.id="wall-check";
                world.projectiles.Add(wallShot);
                typeof(ProjectileController).GetMethod("Reconcile",Private).Invoke(controller,null);
                controller.Step(.01f);controller.Step(.01f);
                sprites=root.GetComponentsInChildren<SpriteRenderer>();
                Require(contacts==2 && lastContact.Value.target==null && lastContact.Value.surfaceTexture=="Hub/concrete" &&
                    Mathf.Abs(lastContact.Value.position.x-2)<.001f && sprites.Length==1,"Лазер не создаёт единственный контакт и вспышку на стене");
                foreach(var flight in controller.Flights) if(flight.state.launch.id=="wall-check")
                    Require(flight.path.Finished && Mathf.Abs(flight.path.Position.x-2)<.001f,"Лазер продолжил полёт за стеной");
                actor.level="factory:0";effects.Step(0);
                Require(root.GetComponentsInChildren<SpriteRenderer>().Length==0,"Вспышка осталась после ухода в другую локацию");
                Debug.Log("Попадание лазера: PASS — мишень и стена, остановка, отсутствие повторов, пауза, окончание и смена уровня.");
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}

