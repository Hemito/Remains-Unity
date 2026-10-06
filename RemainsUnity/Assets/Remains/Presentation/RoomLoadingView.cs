using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // Created once in the existing HUD Canvas. Visibility changes only on presentation events.
    public sealed class RoomLoadingView : MonoBehaviour
    {
        private Image background;
        private Text caption;
        private Transform menuLayer;
        public RectTransform Rect=>(RectTransform)transform;
        public bool IsVisible=>gameObject.activeInHierarchy;
        public Text Caption=>caption;

        public static RoomLoadingView Create(Transform parent,Font font,Transform menuLayer)
        {
            var canvas=parent?.GetComponentInParent<Canvas>();
            if(parent is not RectTransform || canvas==null || canvas.rootCanvas.renderMode!=RenderMode.ScreenSpaceOverlay || font==null ||
                menuLayer==null || menuLayer.parent!=parent)
                throw new InvalidOperationException("Загрузка локации: нужен существующий Overlay Canvas, шрифт HUD и слой Escape в том же Canvas.");
            var root=new GameObject("Загрузка локации",typeof(RectTransform),typeof(Image),typeof(RoomLoadingView));
            root.transform.SetParent(parent,false);
            var view=root.GetComponent<RoomLoadingView>();
            view.menuLayer=menuLayer;
            view.Rect.anchorMin=Vector2.zero;view.Rect.anchorMax=Vector2.one;
            view.Rect.offsetMin=view.Rect.offsetMax=Vector2.zero;
            view.background=root.GetComponent<Image>();
            view.background.color=new Color(.04f,.08f,.07f,1);
            view.background.raycastTarget=true;
            var label=new GameObject("Сообщение загрузки",typeof(RectTransform),typeof(Text));
            label.transform.SetParent(root.transform,false);
            var rect=(RectTransform)label.transform;
            rect.anchorMin=new Vector2(0,.5f);rect.anchorMax=new Vector2(1,.5f);
            rect.offsetMin=new Vector2(32,-24);rect.offsetMax=new Vector2(-32,24);
            view.caption=label.GetComponent<Text>();
            view.caption.font=font;view.caption.fontSize=20;
            view.caption.text="Загрузка локации…";view.caption.color=new Color(.65f,1,.75f,1);
            view.caption.alignment=TextAnchor.MiddleCenter;view.caption.raycastTarget=false;
            view.caption.resizeTextForBestFit=false;
            view.Validate();root.SetActive(false);return view;
        }
        public void SetVisible(bool visible)
        {
            if(gameObject.activeSelf==visible) return;
            gameObject.SetActive(visible);
            if(visible)
            {
                transform.SetAsLastSibling();
                // Escape stays usable when loading starts with the menu already open.
                menuLayer.SetAsLastSibling();
            }
        }
        public void Validate()
        {
            var canvas=GetComponentInParent<Canvas>();
            if(canvas==null || canvas.rootCanvas.renderMode!=RenderMode.ScreenSpaceOverlay ||
                background==null || background.gameObject!=gameObject || background.color.a!=1 || !background.raycastTarget ||
                menuLayer==null || menuLayer.parent!=transform.parent || menuLayer==transform ||
                caption==null || caption.font==null || !caption.transform.IsChildOf(transform) || caption.raycastTarget ||
                caption.text!="Загрузка локации…" || Rect.anchorMin!=Vector2.zero || Rect.anchorMax!=Vector2.one ||
                Rect.offsetMin!=Vector2.zero || Rect.offsetMax!=Vector2.zero)
                throw new InvalidOperationException("Загрузка локации: неверные ссылки, полноэкранные границы, непрозрачность или текст HUD.");
        }
    }
}
