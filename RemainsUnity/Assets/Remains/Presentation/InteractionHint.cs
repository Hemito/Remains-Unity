using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // Displays the same target that the common E interaction selected.
    public sealed class InteractionHint
    {
        private readonly RectTransform canvas, panel;
        private readonly Text label;
        private readonly Color normalColor;
        private readonly Camera camera;
        private readonly Canvas root;
        private Vector2 targetTop;
        private bool hasTarget;
        private bool allowed;
        private ActorState actor;
        public InteractionHint(Transform parent,HudElements ui)
        {
            canvas=(RectTransform)parent;camera=Camera.main;root=parent.GetComponent<Canvas>();
            Canvas.willRenderCanvases+=Place;
            label=ui.BackedLabel(parent,"",Vector2.zero,new Vector2(220,27));
            normalColor=label.color;
            label.alignment=TextAnchor.MiddleCenter;label.fontSize=16;
            panel=(RectTransform)label.transform.parent;
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);
            panel.gameObject.SetActive(false);
            if(camera==null) Debug.LogWarning("Подсказка взаимодействия недоступна: не найдена камера MainCamera.");
        }
        public void Refresh(ContainerState container,bool visible,ActorState localActor=null)
        {
            hasTarget=container!=null;allowed=visible;actor=localActor;
            if(container!=null) {label.color=normalColor;label.text=container.presentationItem==null?$"E · Контейнер · {container.mass:0.#} кг":"E · Подобрать предмет";targetTop=container.Position+Vector2.up*(container.height-container.anchorHeight+.25f);}
            Place();
        }
        public void Refresh(TravelPoint point,bool visible,ActorState localActor)
        {
            hasTarget=point!=null;allowed=visible;actor=localActor;
            if(point!=null) {label.color=normalColor;label.text="E · "+point.title;targetTop=new Vector2(point.x+point.width*.5f,point.y+point.height+.25f);}
            Place();
        }
        public void Refresh(DoorState door,bool visible,ActorState localActor)
        {
            hasTarget=door!=null;allowed=visible;actor=localActor;
            if(door!=null)
            {
                label.color=Color.yellow;label.text=door.open?"E · Закрыть дверь":"E · Открыть дверь";
                targetTop=new Vector2(door.x+door.width*.5f,door.y+door.height+.25f);
            }
            Place();
        }
        public void Dispose()=>Canvas.willRenderCanvases-=Place;
        private void Place()
        {
            if(!allowed || !hasTarget || camera==null) {panel.gameObject.SetActive(false);return;}
            var screen=camera.WorldToScreenPoint(targetTop);
            bool onScreen=screen.z>0 && camera.pixelRect.Contains(new Vector2(screen.x,screen.y));
            panel.gameObject.SetActive(onScreen);
            if(!onScreen) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,screen,root.renderMode==RenderMode.ScreenSpaceOverlay?null:root.worldCamera,out var point);
            if(actor!=null)
            {
                var bounds=PonyVisual.WorldBounds(actor);
                var uiCamera=root.renderMode==RenderMode.ScreenSpaceOverlay?null:root.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,camera.WorldToScreenPoint(bounds.min),uiCamera,out var low);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,camera.WorldToScreenPoint(bounds.max),uiCamera,out var high);
                var actorRect=Rect.MinMaxRect(Mathf.Min(low.x,high.x)-8,Mathf.Min(low.y,high.y)-8,Mathf.Max(low.x,high.x)+8,Mathf.Max(low.y,high.y)+8);
                if(!HintPlacement.TryPlace(point,panel.rect.size,canvas.rect,actorRect,out point)) {panel.gameObject.SetActive(false);return;}
            }
            panel.anchoredPosition=point;
        }
    }
}
