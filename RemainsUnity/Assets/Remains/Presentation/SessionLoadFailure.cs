using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public static class SessionLoadFailure
    {
        // The caller first ends the partial session. Keep the main menu usable after closing
        // this log; do not claim an active session's saves were never written.
        public static void Show(Exception error)
            =>Show(error,"Не удалось загрузить локацию. Сессия остановлена.");
        public static void Show(Exception error,string explanation)
        {
            if(Application.isBatchMode) {Application.Quit(3);return;}
            var root=new GameObject("Журнал ошибки локации",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if(font==null) throw new InvalidOperationException("Не найден шрифт журнала ошибки локации.");
            var panel=new GameObject("Ошибка локации",typeof(RectTransform),typeof(Image));panel.transform.SetParent(root.transform,false);
            var rect=(RectTransform)panel.transform;rect.anchorMin=new Vector2(.04f,.04f);rect.anchorMax=new Vector2(.96f,.96f);rect.offsetMin=rect.offsetMax=Vector2.zero;
            panel.GetComponent<Image>().color=new Color(.06f,.02f,.02f,1);
            var label=new GameObject("Ошибка",typeof(RectTransform),typeof(Text));label.transform.SetParent(panel.transform,false);
            var labelRect=(RectTransform)label.transform;labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;
            labelRect.offsetMin=new Vector2(24,84);labelRect.offsetMax=new Vector2(-24,-24);
            var text=label.GetComponent<Text>();text.font=font;text.fontSize=18;text.color=new Color(1,.65f,.55f);text.raycastTarget=false;
            text.text=explanation+" Подробности ошибки:\n\n"+error;
            var close=new GameObject("Вернуться в главное меню",typeof(RectTransform),typeof(Image),typeof(Button));close.transform.SetParent(panel.transform,false);
            var closeRect=(RectTransform)close.transform;closeRect.anchorMin=new Vector2(.25f,0);closeRect.anchorMax=new Vector2(.75f,0);closeRect.offsetMin=new Vector2(0,20);closeRect.offsetMax=new Vector2(0,64);
            close.GetComponent<Image>().color=new Color(.08f,.2f,.13f,1);
            var caption=new GameObject("Текст",typeof(RectTransform),typeof(Text));caption.transform.SetParent(close.transform,false);
            var captionRect=(RectTransform)caption.transform;captionRect.anchorMin=Vector2.zero;captionRect.anchorMax=Vector2.one;captionRect.offsetMin=captionRect.offsetMax=Vector2.zero;
            var captionText=caption.GetComponent<Text>();captionText.font=font;captionText.fontSize=20;captionText.text="Вернуться в главное меню";captionText.alignment=TextAnchor.MiddleCenter;captionText.color=new Color(.65f,1,.75f);captionText.raycastTarget=false;
            close.GetComponent<Button>().onClick.AddListener(()=>UnityEngine.Object.Destroy(root));
        }
    }
}
