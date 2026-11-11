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
        private WorldInteractionOutline worldOutline;
        private InteractionTarget highlightedTarget;
        private bool highlightVisible;
        private bool missingOutlineWarning;
        private Vector2 targetTop;
        private bool hasTarget;
        private bool allowed;
        private string sizedText;
        private string interactKey="E";
        private ActorState actor;
        private readonly Func<Rect?> drawnActorBounds;
        private bool missingActorWarning;
        private enum Content {None,Pickup,PickupPending,Body,FixedContainer,MassContainer,SecuredContainer,Revival,Travel,DoorOpen,DoorClosed,MechanismPending,MechanismReady,MechanismRepair,MechanismProgress,MechanismUnavailable,Mine,MinePending,Hazard}
        private Content content;
        private float? displayedMass;
        private string displayedTitle;
        private int displayedPercent;
        public int TextRevisionCount {get;private set;}
        public int PlacementEvaluationCount {get;private set;}
        public WorldInteractionOutline WorldOutline=>worldOutline;
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
            if(camera==null) Debug.LogWarning("Подсказка взаимодействия недоступна: не найдена камера MainCamera.");
        }
        public void ConfigureWorldOutline(Camera worldCamera,Func<InteractionTarget,WorldOutlineSource> resolve)
        {
            worldOutline?.Dispose();worldOutline=new WorldInteractionOutline(worldCamera,resolve);missingOutlineWarning=false;
            UpdateOutline();
        }
        public void SetInteractKey(string key)
        {
            if(interactKey==key) return;
            interactKey=key;content=Content.None;
        }
        public void Refresh(ContainerState container,bool visible,ActorState localActor=null)
        {
            RefreshSource(container,visible,localActor);
        }
        public void RefreshSource(InventorySource source,bool visible,ActorState localActor,bool pickupPending=false,ILootRule lootRule=null)
        {
            hasTarget=source!=null;allowed=visible;actor=localActor;
            if(!hasTarget || !allowed) UpdateOutline();
            if(source!=null)
            {
                var mass=source.Mass;
                string security=localActor==null?NearbyLootRule.SecurityRejection(source.LockLevel,source.MineLevel):
                    ContainerAccess.Rejection(localActor,source,lootRule);
                var next=security!=null?Content.SecuredContainer:source.IsLooseItem?(pickupPending?Content.PickupPending:Content.Pickup):source.Body!=null?Content.Body:mass.HasValue?Content.MassContainer:Content.FixedContainer;
                if(content!=next || next==Content.MassContainer && displayedMass!=mass || next==Content.SecuredContainer && displayedTitle!=security)
                {
                    content=next;displayedMass=mass;displayedTitle=security;
                    SetText(security??(source.IsLooseItem?(pickupPending?Remains.Content.GameLocalization.UiText("source.InteractionHint.da9b272370b0c345", "Подбор выбранной вещи подтверждается…", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):Remains.Content.GameLocalization.Format("source.InteractionHint.36fbec6653bd2c71", $"{interactKey} · Подобрать предмет", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs")):
                        source.Body!=null?Remains.Content.GameLocalization.Format("source.InteractionHint.945e2c328e173d46", $"{interactKey} · Обыскать тело", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):mass.HasValue?Remains.Content.GameLocalization.Format("source.InteractionHint.d47a57acf5b27684", $"{interactKey} · Контейнер · {mass:0.#} кг", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):Remains.Content.GameLocalization.Format("source.InteractionHint.2b0024641f631fe5", $"{interactKey} · Контейнер · Закреплён", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs")),security!=null?Color.yellow:normalColor);
                }
                var bounds=source.Bounds;targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
            }
        }
        public void Highlight(InteractionTarget target,bool visible)
        {
            highlightedTarget=target;
            highlightVisible=visible && target.kind is InteractionKind.Container or InteractionKind.Pickup or InteractionKind.Door or InteractionKind.Map or InteractionKind.Exit or InteractionKind.Revival or InteractionKind.Mechanism or InteractionKind.Mine;
            UpdateOutline();
        }
        public void RefreshRevival(ActorState ally,bool visible,ActorState localActor)
        {
            hasTarget=ally!=null;allowed=visible;actor=localActor;
            if(!hasTarget || !allowed) UpdateOutline();
            if(ally!=null)
            {
                var bounds=InventorySource.BodyBounds(ally);
                if(content!=Content.Revival) {content=Content.Revival;SetText(Remains.Content.GameLocalization.Format("source.InteractionHint.8fea2193a5ffee78", $"Удерживайте {interactKey} · Поднять союзника", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"),normalColor);}
                targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
            }
        }
        public void Refresh(TravelPoint point,bool visible,ActorState localActor)
        {
            hasTarget=point!=null;allowed=visible;actor=localActor;
            if(!hasTarget || !allowed) UpdateOutline();
            if(point!=null)
            {
                if(content!=Content.Travel || displayedTitle!=point.title) {content=Content.Travel;displayedTitle=point.title;SetText($"{interactKey} · {point.title}",normalColor);}
                targetTop=new Vector2(point.x+point.width*.5f,point.y+point.height+.25f);
            }
        }
        public void Refresh(DoorState door,bool visible,ActorState localActor)
        {
            hasTarget=door!=null;allowed=visible;actor=localActor;
            if(!hasTarget || !allowed) UpdateOutline();
            if(door!=null)
            {
                var next=door.open?Content.DoorOpen:Content.DoorClosed;
                if(content!=next) {content=next;SetText($"{interactKey} · {(door.open?Remains.Content.GameLocalization.UiText("source.InteractionHint.bd261b1ef70cfeeb", "Закрыть дверь", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):Remains.Content.GameLocalization.UiText("source.InteractionHint.cf1625995f4753e0", "Открыть дверь", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"))}",Color.yellow);}
                targetTop=new Vector2(door.x+door.width*.5f,door.y+door.height+.25f);
            }
        }
        public void Dispose() {Canvas.willRenderCanvases-=Place;worldOutline?.Dispose();}
        public void RefreshMechanism(MechanismButtonState button,Rect bounds,bool visible,ActorState localActor,double progress,bool pending)
        {
            hasTarget=button!=null && !button.removed;allowed=visible;actor=localActor;
            if(!hasTarget || !allowed) UpdateOutline();
            if(hasTarget)
            {
                var next=pending?Content.MechanismPending:button.repairState==MechanismRepairStates.Ready?Content.MechanismReady:
                    button.repairState==MechanismRepairStates.NeedsRepair?(progress>0?Content.MechanismProgress:Content.MechanismRepair):Content.MechanismUnavailable;
                int percent=next==Content.MechanismProgress?(int)Math.Round(progress*100):0;
                if(content!=next || percent!=displayedPercent)
                {
                    content=next;displayedPercent=percent;
                    SetText(pending?Remains.Content.GameLocalization.UiText("source.InteractionHint.a2fbffed94fdebc9", "Подтверждение взаимодействия…", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):button.repairState==MechanismRepairStates.Ready?
                        Remains.Content.GameLocalization.Format("source.InteractionHint.f3a609440565dc2a", $"{interactKey} · Запустить платформу", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):button.repairState==MechanismRepairStates.NeedsRepair?
                        progress>0?Remains.Content.GameLocalization.Format("source.InteractionHint.694f50b2b78d68b2", $"{interactKey} (удерживать) · Ремонт: {progress:P0}", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):Remains.Content.GameLocalization.Format("source.InteractionHint.900f2ad710f18ffd", $"{interactKey} (удерживать) · Починить кнопку", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):Remains.Content.GameLocalization.UiText("source.InteractionHint.5e59a8930244e3d0", "Ремонт кнопки пока недоступен", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"),Color.yellow);
                }
                targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
            }
        }
        public void RefreshMine(bool exists,Rect bounds,bool visible,ActorState localActor,bool pending)
        {
            hasTarget=exists;allowed=visible;actor=localActor;
            if(!hasTarget || !allowed) UpdateOutline();
            if(!hasTarget) return;
            var next=pending?Content.MinePending:Content.Mine;
            if(content!=next) {content=next;SetText(pending?Remains.Content.GameLocalization.UiText("source.InteractionHint.c47942fd4ba124db", "Обезвреживание подтверждается…", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):Remains.Content.GameLocalization.Format("source.InteractionHint.7c767c84daf2aac4", $"{interactKey} · Обезвредить мину", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"),Color.yellow);}
            targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
        }
        // A trap or charge: its action and name ("Обезвредить капкан", "Взвести капкан"); null when it is gone.
        public void RefreshHazard(string action,Rect bounds,bool visible,ActorState localActor,bool pending)=>RefreshAction(action,bounds,visible,localActor,pending);
        // An action over a box (a trap's disarm, lying on a bed): "E · action" above it.
        public void RefreshAction(string action,Rect bounds,bool visible,ActorState localActor,bool pending)
        {
            hasTarget=action!=null;allowed=visible;actor=localActor;
            if(!hasTarget || !allowed) UpdateOutline();
            if(!hasTarget) return;
            content=pending?Content.MinePending:Content.Hazard;
            SetText(pending?Remains.Content.GameLocalization.UiText("source.InteractionHint.e37bb80f4a94ee7c", "Действие подтверждается…", "RemainsUnity/Assets/Remains/Presentation/InteractionHint.cs"):$"{interactKey} · {action}",Color.yellow);
            targetTop=new Vector2(bounds.center.x,bounds.yMax+.25f);
        }
        private void SetText(string value,Color color)
        {
            if(label.text!=value) {label.text=value;TextRevisionCount++;}
            if(label.color!=color) label.color=color;
        }
        private void Place()
        {
            // Only the E label belongs to the overlay. The world renderer prepares
            // its rectangle after actual world poses, before either camera pipeline.
            PlacementEvaluationCount++;
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
        private void UpdateOutline()
        {
            bool show=allowed && hasTarget && highlightVisible;
            if(worldOutline!=null) {worldOutline.SetTarget(highlightedTarget,show);return;}
            if(show && !missingOutlineWarning)
            {
                missingOutlineWarning=true;
                Debug.LogWarning($"Контур взаимодействия не настроен: target={highlightedTarget.kind}/{highlightedTarget.id}; требуется ConfigureWorldOutline с фактическим представлением объекта.");
            }
            if(!show) missingOutlineWarning=false;
        }
    }
}
