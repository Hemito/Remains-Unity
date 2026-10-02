using System.IO;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class MainMenuRender
    {
        public static void Render()
        {
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) RenderAt(size.x,size.y);
        }
        private static void RenderAt(int width,int height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера меню").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Меню").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var menu=new MainMenuPanel(canvas.transform,new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")),()=>{},()=>{},()=>{},()=>{});
            menu.Character.text="Литлпип";
            menu.World.text="Мир 7f65d82c · ревизия 930\nВетвь 1122aabb · снимок 48";
            Canvas.ForceUpdateCanvases();
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", $".reference/tests/main-menu-{width}x{height}.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Главное меню: кадр сохранён "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
