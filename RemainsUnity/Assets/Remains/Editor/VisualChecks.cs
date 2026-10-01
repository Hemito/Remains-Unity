using System;
using System.IO;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Remains.Networking;
using Remains.Persistence;
using kcp2k;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class VisualChecks
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RoomVisual.Build(RoomLayout.Load());
            PonyVisual.Prepare();
            var idle=new GameObject("Пони: стояние").AddComponent<PonyVisual>();
            idle.Bind(new ActorState { id="visual-idle",x=-1,pose="idle",hair=0 });idle.DrawPose(0);idle.DrawPose(0.5f);
            var running=new GameObject("Пони: бег").AddComponent<PonyVisual>();
            running.Bind(new ActorState { id="visual-run",x=2,pose="run",hair=1 });running.DrawPose(0);running.DrawPose(0.2f);
            var down=new GameObject("Пони: нокдаун").AddComponent<PonyVisual>();
            down.Bind(new ActorState {id="visual-down",x=5,down=true});down.DrawPose(0);
            var camera=new GameObject("Камера проверки").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=4;
            camera.transform.position=new Vector3(2,2.5f,-10);camera.backgroundColor=new Color(0.04f,0.06f,0.05f);
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            camera.Render();RenderTexture.active=target;
            var image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/visual"));
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"scene.png");File.WriteAllBytes(path,image.EncodeToPNG());
            var network=new GameObject("Проверка интерфейса");network.AddComponent<KcpTransport>();
            var session=network.AddComponent<CoopSession>();session.Saves=new SaveStore(Path.Combine(directory,"hud-save"));
            var world=new WorldState();world.actors.Add(new ActorState{id="ui-player",character="Проверка",health=65,mana=70});
            var container=new ContainerState();world.containers.Add(container);
            container.items.Add(SharedInventory.Create("caps",20));container.items.Add(SharedInventory.Create("heal",3));
            world.Find("ui-player").inventory.Add(SharedInventory.Create("heal",5));
            typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,world);
            typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(session,"ui-player");
            var hud=new GameObject("Интерфейс проверки").AddComponent<PrototypeHud>();hud.Initialize(session);hud.ToggleInventory();
            var canvas=hud.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"hud.png"),image.EncodeToPNG());
            RenderTexture.active=null;camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("Снимок рендера Unity: "+path);
        }
    }
}
