using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Editor
{
    public static class ContentComparisonRender
    {
        public static void Render()
            =>Render(false);
        public static void RenderLongWorld()=>Render(true);
        private static void Render(bool longWorld)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var canvas=new GameObject("Таблица").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var panel=ui.Panel(canvas.transform,"Сравнение",new Vector2(90,-70),new Vector2(1100,570));
            var rows=new List<ContentDifference>();
            for(int i=0;i<5;i++) rows.Add(new ContentDifference {id="example.balance."+i,saved="#"+(i+1)+" · версия 1.0.0\n"+new string('A',64),current="#"+(5-i)+" · версия 2.1.0\n"+new string('F',64)});
            ContentComparisonView.Draw(panel.transform,rows,longWorld?new string('Ш',48)+": версия игры или набор модов изменились.":"Контент персонажа отличается. Продолжение может изменить игровой результат.",0,_=>{},_=>{});
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1280,720,TextureFormat.RGBA32,false);
            try
            {
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,longWorld?"../../.reference/tests/content-comparison-long-world.png":"../../.reference/tests/content-comparison-prefab.png")),image.EncodeToPNG());
                var title=panel.GetComponentInChildren<ContentComparisonLayout>().Title;
                if(title.preferredHeight>title.rectTransform.rect.height+.1f)
                    throw new System.InvalidOperationException($"Предупреждение обрезано: нужно {title.preferredHeight}, доступно {title.rectTransform.rect.height}.");
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
    }
}
