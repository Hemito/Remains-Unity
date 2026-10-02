using System;
using System.IO;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomAuthoringScenes
    {
        public static void ValidateSavedOverrides()
        {
            foreach(string name in new[]{"HubAuthoring","FactoryAuthoring"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/Remains/Scenes/"+name+".unity",OpenSceneMode.Single);
                var rooms=scene.GetRootGameObjects().Where(root=>root.GetComponent<RoomAuthoring>()!=null).ToArray();
                if(rooms.Length!=1 || !PrefabUtility.IsPartOfPrefabInstance(rooms[0])) throw new InvalidOperationException("Сцена авторинга должна ссылаться на один префаб уровня: "+name);
                if(PrefabUtility.HasPrefabInstanceAnyOverrides(rooms[0],false))
                    throw new InvalidOperationException($"В {name} есть правки экземпляра уровня. Примените нужные Overrides к исходному префабу или отмените их перед сборкой; игра загружает исходный префаб.");
            }
        }
        public static void Create()
        {
            foreach(string level in new[]{"hub","factory:0"})
            {
                string name=level=="hub"?"HubAuthoring":"FactoryAuthoring";
                string path="Assets/Remains/Scenes/"+name+".unity";
                if(File.Exists(path)) throw new InvalidOperationException("Сцена уже существует, замена отменена: "+path);
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(RoomAuthoring.Load(level).gameObject);
                var room=instance.GetComponent<RoomAuthoring>().CreateLayout();
                var camera=new GameObject("Камера предпросмотра").AddComponent<Camera>();camera.orthographic=true;
                camera.orthographicSize=Mathf.Max((room.top-room.bottom)/2,(room.right-room.left)/2/1.8f)+.3f;
                camera.transform.position=new Vector3((room.left+room.right)/2,(room.bottom+room.top)/2,-10);camera.backgroundColor=new Color(.08f,.09f,.1f);
                EditorSceneManager.SaveScene(scene,path);
            }
            Verify();
        }
        public static void Verify()
        {
            foreach(string name in new[]{"HubAuthoring","FactoryAuthoring"})
            {
                string path="Assets/Remains/Scenes/"+name+".unity";
                string before=File.ReadAllText(path);var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                var room=scene.GetRootGameObjects().Select(root=>root.GetComponent<RoomAuthoring>()).Single(value=>value!=null);
                if(!PrefabUtility.IsPartOfPrefabInstance(room)) throw new InvalidOperationException("Сцена потеряла связь с префабом: "+name);
                RoomLightingPreview.Refresh(room);var map=Shader.GetGlobalTexture("_WorldLightMap");
                if(map==null || Shader.GetGlobalFloat("_WorldLightEnabled")!=1) throw new InvalidOperationException("Нет карты предпросмотра света.");
                RoomLightingPreview.Refresh(room);
                if(map!=null) throw new InvalidOperationException("Повторный предпросмотр оставил прежнюю карту света.");
                if(scene.isDirty) throw new InvalidOperationException("Предпросмотр изменил сцену: "+name);
                var camera=scene.GetRootGameObjects().Select(root=>root.GetComponent<Camera>()).Single(value=>value!=null);
                var target=new RenderTexture(1800,1000,24);target.Create();camera.targetTexture=target;
                var image=new Texture2D(1800,1000,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
                try
                {
                    camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1800,1000),0,0);image.Apply();
                    File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/"+name+"-preview.png")),image.EncodeToPNG());
                }
                finally {RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);RoomLightingPreview.Clear();}
                if(Shader.GetGlobalFloat("_WorldLightEnabled")!=0 || File.ReadAllText(path)!=before) throw new InvalidOperationException("Предпросмотр не очистился или изменил файл сцены.");
                Debug.Log("Сцена авторинга и временный свет: PASS — "+name);
            }
        }
    }
}
