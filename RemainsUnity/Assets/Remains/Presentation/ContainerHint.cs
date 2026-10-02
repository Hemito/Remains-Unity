using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // Shows exactly the container selected by the inventory, without another selection rule.
    public sealed class ContainerHint
    {
        private readonly RectTransform canvas, panel;
        private readonly Text label;
        private readonly Camera camera;
        private readonly Canvas root;
        private ContainerState selected;
        private bool allowed;
        public ContainerHint(Transform parent,HudElements ui)
        {
            canvas=(RectTransform)parent;camera=Camera.main;root=parent.GetComponent<Canvas>();
            Canvas.willRenderCanvases+=Place;
            label=ui.BackedLabel(parent,"",Vector2.zero,new Vector2(220,27));
            label.alignment=TextAnchor.MiddleCenter;label.fontSize=16;
            panel=(RectTransform)label.transform.parent;
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);
            panel.gameObject.SetActive(false);
            if(camera==null) Debug.LogWarning("Подсказка контейнера недоступна: не найдена камера MainCamera.");
        }
        public void Refresh(ContainerState container,bool visible)
        {
            selected=container;allowed=visible;
            if(container!=null) label.text=$"I · Контейнер · {container.mass:0.#} кг";
            Place();
        }
        public void Dispose()=>Canvas.willRenderCanvases-=Place;
        private void Place()
        {
            var container=selected;
            if(!allowed || container==null || camera==null) {panel.gameObject.SetActive(false);return;}
            var top=container.Position+Vector2.up*(container.height-container.anchorHeight+.25f);
            var screen=camera.WorldToScreenPoint(top);
            bool onScreen=screen.z>0 && camera.pixelRect.Contains(new Vector2(screen.x,screen.y));
            panel.gameObject.SetActive(onScreen);
            if(!onScreen) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,screen,root.renderMode==RenderMode.ScreenSpaceOverlay?null:root.worldCamera,out var point);
            panel.anchoredPosition=point;
        }
    }
}
