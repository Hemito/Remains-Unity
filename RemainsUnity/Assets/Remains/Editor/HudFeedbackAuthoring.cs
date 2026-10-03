using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class HudFeedbackAuthoring
    {
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            var camera=new GameObject("Камера").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var canvas=new GameObject("HUD").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var view=HudFeedbackView.Create(canvas.transform);
            view.Notice.text="Предмет уже забрал другой игрок. Содержимое контейнера обновлено.";
            view.Revival.text="Поднимаем союзника: 50% · удерживайте E";
            view.LayoutNotice(false);Canvas.ForceUpdateCanvases();
            if(view.Notice.preferredHeight>view.Notice.rectTransform.rect.height+.1f || view.Revival.preferredWidth>view.Revival.rectTransform.rect.width+.1f)
                throw new InvalidOperationException("Текст обратной связи HUD обрезан.");
            var pixels=new Texture2D(1280,720,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/tests/hud-feedback.png")),pixels.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
        }
        public static void Create()
        {
            const string path="Assets/Remains/Resources/Presentation/HudFeedback.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null) throw new InvalidOperationException("Префаб обратной связи HUD уже существует; редактируйте его в Inspector.");
            var root=new GameObject("Сообщения HUD",typeof(RectTransform));
            try
            {
                var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
                var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                var view=root.AddComponent<HudFeedbackView>();
                view.Notice=ui.BackedLabel(root.transform,"",new Vector2(350,-140),new Vector2(880,55));
                view.Notice.transform.parent.name="Уведомление";
                view.Revival=ui.BackedLabel(root.transform,"",new Vector2(480,-625),new Vector2(750,35));
                view.Revival.transform.parent.name="Поднятие союзника";
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
