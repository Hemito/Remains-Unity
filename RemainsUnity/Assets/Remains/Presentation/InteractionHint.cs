using System;
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
        private readonly RectTransform[] edges=new RectTransform[4];
        private Rect targetBounds;
        private bool highlightVisible;
        private Vector2 targetTop;
        private bool hasTarget;
        private bool allowed;
        private string sizedText;
        private ActorState actor;
        private readonly Func<Rect?> drawnActorBounds;
        private bool missingActorWarning;
        private enum Content {None,Pickup,PickupPending,Body,FixedContainer,MassContainer,Revival,Travel,DoorOpen,DoorClosed,MechanismPending,MechanismReady,MechanismRepair,MechanismProgress,MechanismUnavailable}
        private Content content;
        private float? displayedMass;
        private string displayedTitle;
        private int displayedPercent;
        public int TextRevisionCount {get;private set;}
        public int PlacementEvaluationCount {get;private set;}
        public InteractionHint(Transform parent,HudElements ui):this(parent,ui,null) {}
        public InteractionHint(Transform parent,HudElements ui,Func<Rect?> drawnActorBounds)
        {
            this.drawnActorBounds=drawnActorBounds;
            canvas=(RectTransform)parent;camera=Camera.main;root=parent.GetComponent<Canvas>();
            Canvas.willRenderCanvases+=Place;
            label=ui.BackedLabel(parent,"",Vector2.zero,new Vector2(220,27));
            normalColor=label.color;
            label.alignment=TextAnchor.MiddleCenter;label.fontSize=16;
            panel=(RectTransform)label.transform.parent;
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);
            panel.gameObject.SetActive(false);
            for(int i=0;i<edges.Length;i++)
            {
                var edge=ui.Panel(parent,"Выделение цели взаимодействия",Vector2.zero,Vector2.zero);
                var image=edge.GetComponent<Image>();image.raycastTarget=false;image.color=new Color(1,.85f,.1f,.85f);
                edges[i]=edge.GetComponent<RectTransform>();edges[i].anchorMin=edges[i].anchorMax=edges[i].pivot=Vector2.zero;
                edge.SetActive(false);
            }
            if(camera==null) Debug.LogWarning("Подсказка взаимодействия недоступна: не найдена камера MainCamera.");
        }
        public void Refresh(ContainerState container,bool visible,ActorState localActor=null)
        {
            RefreshSource(container,visible,localActor);
        }
        public void RefreshSource(InventorySource source,bool visible,ActorState localActor,bool pickupPending=false)
        {
            hasTarget=source!=null;allowed=visible;actor=localActor;
            if(source!=null)
            {
                var mass=source.Mass;
                var next=source.IsLooseItem?(pickupPending?Content.PickupPending:Content.Pickup):source.Body!=null?Content.Body:mass.HasValue?Content.MassContainer:Content.FixedContainer;
                if(content!=next || next==Content.MassContainer && displayedMass!=mass)
                {
                    content=next;displayedMass=mass;
                    SetText(source.IsLooseItem?(pickupPending?"Подбор выбранной вещи подтверждается…":"E · Подобрать предмет"):
                        source.Body!=null?"E · Обыскать тело":mass.HasValue?$"E · Контейнер · {mass:0.#} кг":"E · Контейнер · Закреплён",normalColor);
                }
                var bounds=source.Bounds;targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
            }
        }
        public void Highlight(InteractionTarget target,bool visible)
        {
            targetBounds=target.bounds;
            highlightVisible=visible && target.kind is InteractionKind.Container or InteractionKind.Pickup or InteractionKind.Door or InteractionKind.Map or InteractionKind.Exit or InteractionKind.Revival or InteractionKind.Mechanism;
        }
        public void RefreshRevival(ActorState ally,bool visible,ActorState localActor)
        {
            hasTarget=ally!=null;allowed=visible;actor=localActor;
            if(ally!=null)
            {
                var bounds=InventorySource.BodyBounds(ally);
                if(content!=Content.Revival) {content=Content.Revival;SetText("Удерживайте E · Поднять союзника",normalColor);}
                targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
            }
        }
        public void Refresh(TravelPoint point,bool visible,ActorState localActor)
        {
            hasTarget=point!=null;allowed=visible;actor=localActor;
            if(point!=null)
            {
                if(content!=Content.Travel || displayedTitle!=point.title) {content=Content.Travel;displayedTitle=point.title;SetText("E · "+point.title,normalColor);}
                targetTop=new Vector2(point.x+point.width*.5f,point.y+point.height+.25f);
            }
        }
        public void Refresh(DoorState door,bool visible,ActorState localActor)
        {
            hasTarget=door!=null;allowed=visible;actor=localActor;
            if(door!=null)
            {
                var next=door.open?Content.DoorOpen:Content.DoorClosed;
                if(content!=next) {content=next;SetText(door.open?"E · Закрыть дверь":"E · Открыть дверь",Color.yellow);}
                targetTop=new Vector2(door.x+door.width*.5f,door.y+door.height+.25f);
            }
        }
        public void Dispose()=>Canvas.willRenderCanvases-=Place;
        public void RefreshMechanism(MechanismButtonState button,Rect bounds,bool visible,ActorState localActor,double progress,bool pending)
        {
            hasTarget=button!=null && !button.removed;allowed=visible;actor=localActor;
            if(hasTarget)
            {
                var next=pending?Content.MechanismPending:button.repairState==MechanismRepairStates.Ready?Content.MechanismReady:
                    button.repairState==MechanismRepairStates.NeedsRepair?(progress>0?Content.MechanismProgress:Content.MechanismRepair):Content.MechanismUnavailable;
                int percent=next==Content.MechanismProgress?(int)Math.Round(progress*100):0;
                if(content!=next || percent!=displayedPercent)
                {
                    content=next;displayedPercent=percent;
                    SetText(pending?"Подтверждение взаимодействия…":button.repairState==MechanismRepairStates.Ready?
                        "E · Запустить платформу":button.repairState==MechanismRepairStates.NeedsRepair?
                        progress>0?$"E (удерживать) · Ремонт: {progress:P0}":"E (удерживать) · Починить кнопку":"Ремонт кнопки пока недоступен",Color.yellow);
                }
                targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
            }
        }
        private void SetText(string value,Color color)
        {
            if(label.text!=value) {label.text=value;TextRevisionCount++;}
            if(label.color!=color) label.color=color;
        }
        private void Place()
        {
            // Canvas final placement is shared by Refresh and Highlight. Neither
            // performs an extra projection/pose lookup before the final UI pass.
            PlacementEvaluationCount++;
            PlaceHighlight();
            if(sizedText!=label.text)
            {
                sizedText=label.text;
                var size=new Vector2(Mathf.Max(220,Mathf.Ceil(label.preferredWidth)),27);
                ((RectTransform)label.transform).sizeDelta=size;
                panel.sizeDelta=size+new Vector2(12,12);
            }
            if(!allowed || !hasTarget || camera==null) {panel.gameObject.SetActive(false);return;}
            var screen=camera.WorldToScreenPoint(targetTop);
            bool onScreen=screen.z>0 && camera.pixelRect.Contains(new Vector2(screen.x,screen.y));
            if(!onScreen) {panel.gameObject.SetActive(false);return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,screen,root.renderMode==RenderMode.ScreenSpaceOverlay?null:root.worldCamera,out var point);
            if(actor!=null)
            {
                Rect bounds;
                if(drawnActorBounds!=null)
                {
                    var actual=drawnActorBounds();
                    if(!actual.HasValue)
                    {
                        if(!missingActorWarning)
                        {missingActorWarning=true;Debug.LogWarning($"Подсказка взаимодействия скрыта: нет фактического рисунка персонажа actor={actor.id}, type={actor.actorType}.");}
                        panel.gameObject.SetActive(false);return;
                    }
                    missingActorWarning=false;bounds=actual.Value;
                }
                else bounds=ActorArtworkBounds.WorldRect(PonyVisual.WorldBounds(actor)); // Explicit old constructor compatibility.
                var uiCamera=root.renderMode==RenderMode.ScreenSpaceOverlay?null:root.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,camera.WorldToScreenPoint(new Vector3(bounds.xMin,bounds.yMin,0)),uiCamera,out var low);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,camera.WorldToScreenPoint(new Vector3(bounds.xMax,bounds.yMax,0)),uiCamera,out var high);
                var actorRect=Rect.MinMaxRect(Mathf.Min(low.x,high.x)-8,Mathf.Min(low.y,high.y)-8,Mathf.Max(low.x,high.x)+8,Mathf.Max(low.y,high.y)+8);
                if(!HintPlacement.TryPlace(point,panel.rect.size,canvas.rect,actorRect,out point)) {panel.gameObject.SetActive(false);return;}
            }
            if(!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            if(panel.anchoredPosition!=point) panel.anchoredPosition=point;
        }
        private void PlaceHighlight()
        {
            bool show=allowed && hasTarget && highlightVisible && camera!=null && targetBounds.width>0 && targetBounds.height>0;
            if(show)
            {
                var lower=camera.WorldToScreenPoint(new Vector3(targetBounds.xMin,targetBounds.yMin,0));
                var upper=camera.WorldToScreenPoint(new Vector3(targetBounds.xMax,targetBounds.yMax,0));
                show=lower.z>0 && upper.z>0;
                if(show)
                {
                    var uiCamera=root.renderMode==RenderMode.ScreenSpaceOverlay?null:root.worldCamera;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,lower,uiCamera,out var low);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,upper,uiCamera,out var high);
                    var origin=canvas.rect.min;low-=origin;high-=origin;
                    float width=high.x-low.x,height=high.y-low.y;
                    SetEdge(0,low,new Vector2(width,2));SetEdge(1,new Vector2(low.x,high.y-2),new Vector2(width,2));
                    SetEdge(2,low,new Vector2(2,height));SetEdge(3,new Vector2(high.x-2,low.y),new Vector2(2,height));
                }
            }
            foreach(var edge in edges) if(edge.gameObject.activeSelf!=show) edge.gameObject.SetActive(show);
        }
        private void SetEdge(int index,Vector2 position,Vector2 size)
        {
            var edge=edges[index];
            if(edge.anchoredPosition!=position) edge.anchoredPosition=position;
            if(edge.sizeDelta!=size) edge.sizeDelta=size;
        }
    }
}
