using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public static class StartupFailure
    {
        public static void Show(Exception failure)
        {
            Debug.LogError("Ошибка загрузки проекта/модов:\n"+failure);
            if(Application.isBatchMode) { Application.Quit(3);return; }
            var root=new GameObject("Главное меню — журнал ошибок");
            var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            root.AddComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var panel=new GameObject("Ошибка загрузки",typeof(RectTransform));panel.transform.SetParent(root.transform,false);
            var rect=panel.GetComponent<RectTransform>();rect.anchorMin=new Vector2(0.05f,0.05f);rect.anchorMax=new Vector2(0.95f,0.95f);rect.offsetMin=rect.offsetMax=Vector2.zero;
            panel.AddComponent<Image>().color=new Color(0.06f,0.02f,0.02f,1);
            var textObject=new GameObject("Журнал",typeof(RectTransform));textObject.transform.SetParent(panel.transform,false);
            var textRect=textObject.GetComponent<RectTransform>();textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=new Vector2(20,20);textRect.offsetMax=new Vector2(-20,-20);
            var text=textObject.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=18;
            text.color=new Color(1,0.6f,0.5f);text.text="Главное меню\n\nНе удалось загрузить проект или моды. Исправьте указанную ошибку и перезапустите игру. Сохранения не изменены.\n\n"+failure;
        }
    }
}
