using System;
using System.IO;
using System.Collections.Generic;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class PauseMenuRender
    {
        public static void Render()
        {
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
                RenderAt(size.x,size.y);
        }
        private static void RenderAt(int width,int height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера меню паузы").AddComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-10);camera.backgroundColor=new Color(.05f,.06f,.05f);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Меню паузы").AddComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var view=PauseMenuView.Create(canvas.transform);view.Panel.SetActive(true);
            // Explicit display fixtures; no gameplay or pause state is modified by this render.
            view.Status.text="Уровень остановлен · Escape — меню";
            view.MenuSetting.GetComponentInChildren<Text>().text="Хост: пауза при меню — выключена";
            view.InventorySetting.GetComponentInChildren<Text>().text="Хост: пауза при инвентаре — включена";
            Canvas.ForceUpdateCanvases();
            foreach(var label in view.Panel.GetComponentsInChildren<Text>(false))
                if(label.preferredHeight>label.rectTransform.rect.height+.1f)
                    throw new InvalidOperationException($"Меню паузы {width}×{height}: обрезана подпись {label.name}, нужно {label.preferredHeight}, доступно {label.rectTransform.rect.height}.");
            var fields=new List<(string name,Rect bounds)>();
            foreach(RectTransform child in view.Panel.transform)
            {
                if(!child.gameObject.activeInHierarchy) continue;
                var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(view.Panel.transform,child);
                var rect=Rect.MinMaxRect(bounds.min.x,bounds.min.y,bounds.max.x,bounds.max.y);
                foreach(var field in fields)
                    if(rect.Overlaps(field.bounds))
                        throw new InvalidOperationException($"Меню паузы {width}×{height}: {child.name} перекрывает {field.name}, области={rect}/{field.bounds}.");
                fields.Add((child.name,rect));
            }
            var corners=new Vector3[4];
            foreach(var rect in new[]{view.Panel.GetComponent<RectTransform>(),view.Controls.transform.parent.GetComponent<RectTransform>()})
            {
                rect.GetWorldCorners(corners);
                foreach(var corner in corners)
                {
                    var point=RectTransformUtility.WorldToScreenPoint(camera,corner);
                    if(point.x<-.1f || point.x>width+.1f || point.y<-.1f || point.y>height+.1f)
                        throw new InvalidOperationException($"Меню паузы {width}×{height}: панель {rect.name} выходит за экран, координата={point}.");
                }
            }
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../..",$".reference/tests/pause-menu-{width}x{height}.png")),image.EncodeToPNG());
                Debug.Log($"Меню паузы: справка помещается при {width}×{height}, шрифт={view.Controls.fontSize}, высота={view.Controls.preferredHeight}/{view.Controls.rectTransform.rect.height}.");
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
