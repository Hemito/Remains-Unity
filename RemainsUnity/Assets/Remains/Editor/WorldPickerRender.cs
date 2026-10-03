using System;
using System.IO;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class WorldPickerRender
    {
        public static void RenderBefore()=>RenderAt(1280,720,true);
        public static void Render()
        {
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(1920,1080)})
                RenderAt(size.x,size.y,false);
        }
        private static void RenderAt(int width,int height,bool before)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера списка миров").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Список миров").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var menu=new MainMenuPanel(canvas.transform,()=>{},()=>{},()=>{},()=>{},()=>{});
            var view=WorldPickerView.Create(canvas.transform);view.Empty.gameObject.SetActive(false);
            string[] names={new string('Ш',48),"Наш завод", "Наш завод", "Другая экспедиция", "Завод с друзьями"};
            string[] relations={"Другая ветвь — прогресс не объединяется","Выбранная копия","Более позднее продолжение","Другой мир","Более ранняя копия"};
            for(int i=0;i<view.Choices.Length;i++)
            {
                if(before) view.Choices[i].GetComponentInChildren<Text>().text=$"{names[i]} · ветвь 1122aabb · ревизия {long.MaxValue}\n{relations[i]}";
                else view.ShowChoice(i,names[i],"1122aabb",long.MaxValue,relations[i],i==1);
            }
            Canvas.ForceUpdateCanvases();
            foreach(var button in view.Choices)
            {
                var label=button.GetComponentInChildren<Text>();
                Debug.Log($"Строка мира: до={before}, требуется={label.preferredHeight}, доступно={label.rectTransform.rect.height}.");
                if(!before && label.preferredHeight>label.rectTransform.rect.height+.1f)
                    throw new InvalidOperationException("Текст копии мира не помещается в строку.");
            }
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..",$".reference/tests/world-picker-{(before?"before-":"")}{width}x{height}.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());
                Debug.Log("Список миров: сохранён кадр "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
