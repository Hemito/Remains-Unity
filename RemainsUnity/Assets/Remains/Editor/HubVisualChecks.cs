using System.IO;
using Remains.Domain;
using Remains.Content;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class HubVisualChecks
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Anatomy.Load();PonyAnimationData.Load();
            var hub=RoomLayout.Load("hub");RoomVisual.Build(hub);
            var camera=new GameObject("Камера проверки хаба").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=hub.top/2;
            camera.transform.position=new Vector3(19.2f,10,-10);camera.backgroundColor=Color.black;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/hub"));
            Directory.CreateDirectory(directory);
            Save(camera,1920,1000,Path.Combine(directory,"background.png"));
            PonyVisual.Prepare();
            var actor=new ActorState {id="visual-hub",pose="idle"};CharacterPlacement.SpawnInHub(actor);
            var pony=new GameObject("Пони для проверки сборки").AddComponent<PonyVisual>();pony.Bind(actor);pony.DrawPose(0);
            camera.orthographicSize=1.6f;camera.transform.position=new Vector3(actor.x,actor.y+0.8f,-10);
            Save(camera,640,640,Path.Combine(directory,"pony.png"));
            actor.pose="walk";pony.DrawPose(.3f);
            Save(camera,640,640,Path.Combine(directory,"walk.png"));
            Debug.Log("Проверка фона хаба и сборки пони: "+directory);
        }

        private static void Save(Camera camera,int width,int height,string path)
        {
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var previous=RenderTexture.active;
            camera.Render();RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;target.Release();
            Object.DestroyImmediate(target);Object.DestroyImmediate(image);
        }
    }
}
