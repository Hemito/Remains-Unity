using System;
using System.IO;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class WorldLobbyRender
    {
        public static void Render()
        {
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(1920,1080)}) RenderAt(size.x,size.y);
        }
        private static void RenderAt(int width,int height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера лобби").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Лобби").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var view=WorldLobbyView.Create(canvas.transform);
            for(int i=0;i<view.Choices.Length;i++) view.ShowChoice(i,i==0?new string('Ш',48):"Совместный завод",new string('Ш',48),long.MaxValue,
                i==1?"Выбранная копия":"Другая ветвь — прогресс не объединяется",i==1,true);
            view.Status.text="Получение и проверка выбранного сохранения…";
            Canvas.ForceUpdateCanvases();
            foreach(var button in view.Choices)
            {
                var text=button.GetComponentInChildren<Text>();
                if(text.preferredHeight>text.rectTransform.rect.height+.1f) throw new InvalidOperationException($"Строка лобби обрезана: нужно {text.preferredHeight}, доступно {text.rectTransform.rect.height}.");
            }
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../..",$".reference/tests/world-lobby-{width}x{height}.png")),image.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
