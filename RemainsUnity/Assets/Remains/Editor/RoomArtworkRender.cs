using System;
using System.IO;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomArtworkRender
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            foreach(string key in new[]{"hub","factory"})
            {
                string level=key=="hub"?"hub":"factory:0";
                string source=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/migration/"+key+"-before-authoring.json"));
                var original=JsonUtility.FromJson<RoomLayout>(File.ReadAllText(source));var current=RoomLayout.Load(level);
                var camera=new GameObject("Сравнение оформления").AddComponent<Camera>();camera.orthographic=true;
                camera.orthographicSize=Mathf.Max((original.top-original.bottom)/2,(original.right-original.left)/2/1.8f)+.3f;
                camera.transform.position=new Vector3((original.left+original.right)/2,(original.bottom+original.top)/2,-10);
                camera.backgroundColor=new Color(.08f,.09f,.1f);
                var before=Capture(camera,RoomVisual.Build(original),key+"-before");
                var after=Capture(camera,AuthoredRoomVisual.Create(level,current),key+"-after");int changed=0;
                for(int i=0;i<before.Length;i++)
                    if(Mathf.Abs(before[i].r-after[i].r)>3 || Mathf.Abs(before[i].g-after[i].g)>3 || Mathf.Abs(before[i].b-after[i].b)>3) changed++;
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                Debug.Log($"Оформление {key}: отличающихся пикселей {changed}/{before.Length}.");
                if(changed>before.Length/1000) throw new InvalidOperationException("Перенос оформления изменил изображение комнаты: "+key);
            }
        }
        private static Color32[] Capture(Camera camera,GameObject room,string name)
        {
            var target=new RenderTexture(1800,1000,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/room-artwork-"+name+".png"));
                File.WriteAllBytes(path,image.EncodeToPNG());return image.GetPixels32();
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(room);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
