using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class ContainerWindowView : MonoBehaviour, IScrollHandler
    {
        [InspectorName("Ячейки (три ряда по три)")] public Button[] Slots;
        [InspectorName("Иконки предметов")] public Image[] Icons;
        [InspectorName("Выбор предмета при нажатии")] public ContainerSlotIntent[] Intents;
        [InspectorName("Прокрутка содержимого")] public Scrollbar Scrollbar;
        [InspectorName("Ожидание подтверждения подбора")] public Image Pending;
        [SerializeField,Min(0),InspectorName("Отступ над объектом")] private float anchorGap=12;
        [SerializeField,Min(0),InspectorName("Отступ от края экрана")] private float screenMargin=8;
        [SerializeField,Min(0),InspectorName("Отступ от интерфейса и рисунка")] private float clearance=6;
        private RectTransform[] reserved=Array.Empty<RectTransform>();
        private Rect[] obstacles=Array.Empty<Rect>(),previousObstacles=Array.Empty<Rect>();
        private readonly Vector3[] corners=new Vector3[4];
        private Func<Rect?> worldActorBounds;
        private Func<Rect?> worldWeaponBounds,worldHeldItemBounds;
        private Func<string> actorContext;
        private bool requireActorBounds,missingActorWarning;
        private Canvas canvas;
        private RectTransform layoutParent;
        private string sourceId;
        private ContainerPointerCapture pointerCapture;
        public bool PointerCaptured=>pointerCapture?.Active==true;
        public bool PlacementDeferred=>PointerCaptured && cached && PlacementAvailable;
        private bool configured,legacyWarning,packingWarning,cached;
        private int previousCount;
        private Rect previousCanvas,previousSource,placement;
        private Vector2 previousSize;
        public bool PlacementAvailable {get;private set;}
        public bool PlacementPinned=>cached && PlacementAvailable;
        public Rect PlacementRect=>placement;
        public Rect SourceCanvasBounds=>previousSource;
        public Rect CanvasArea=>previousCanvas;
        public bool LayoutConfigured=>configured;
        public int ReservationCount=>previousCount;
        public int LayoutEvaluationCount {get;private set;}
        public Rect ReservedCanvasBounds(int index)
        {
            if(index<0 || index>=previousCount) throw new ArgumentOutOfRangeException(nameof(index));
            return previousObstacles[index];
        }
        public event Action<int> ScrollRows;
        public RectTransform Rect=>(RectTransform)transform;
        public void OnScroll(PointerEventData data)
        {
            if(!isActiveAndEnabled || data==null || data.scrollDelta.y==0) return;
            if(ScrollRows==null) {Debug.LogWarning("Окно контейнера: не подключена прокрутка.",this);return;}
            ScrollRows(data.scrollDelta.y<0?1:-1);data.Use();
        }
        public bool ContainsPointer(Vector2 position)=>gameObject.activeInHierarchy &&
            (PointerCaptured || RectTransformUtility.RectangleContainsScreenPoint(Rect,position,null));
        public void ConfigureLayout(RectTransform[] protectedWidgets,Func<Rect?> actorBounds=null)
            =>Configure(protectedWidgets,actorBounds,null,null,null,false);
        public void ConfigureArtworkLayout(RectTransform[] protectedWidgets,Func<Rect?> actorBounds,Func<Rect?> weaponBounds=null,Func<Rect?> heldItemBounds=null,Func<string> context=null)
        {
            if(actorBounds==null) throw new ArgumentNullException(nameof(actorBounds));
            Configure(protectedWidgets,actorBounds,weaponBounds,heldItemBounds,context,true);
        }
        private void Configure(RectTransform[] protectedWidgets,Func<Rect?> actorBounds,Func<Rect?> weaponBounds,Func<Rect?> heldItemBounds,Func<string> context,bool required)
        {
            ValidateCanvas();InitializePointerCapture();
            if(protectedWidgets==null) throw new ArgumentNullException(nameof(protectedWidgets));
            var unique=new HashSet<RectTransform>();
            foreach(var item in protectedWidgets)
                if(item==null || item==Rect || item.IsChildOf(transform) || !unique.Add(item) ||
                    item.GetComponentInParent<Canvas>(true)?.rootCanvas!=canvas.rootCanvas)
                    throw new InvalidOperationException("Окно контейнера: препятствия должны быть уникальными виджетами того же Canvas, вне самого окна.");
            reserved=(RectTransform[])protectedWidgets.Clone();worldActorBounds=actorBounds;
            worldWeaponBounds=weaponBounds;worldHeldItemBounds=heldItemBounds;actorContext=context;requireActorBounds=required;missingActorWarning=false;
            obstacles=new Rect[reserved.Length+3];previousObstacles=new Rect[obstacles.Length];
            configured=true;cached=false;PlacementAvailable=false;packingWarning=false;pointerCapture.Reset();
        }
        public void BeginSource(string id)
        {
            if(string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Окно контейнера: пустой ID источника.",nameof(id));
            sourceId=id;cached=false;PlacementAvailable=false;packingWarning=false;missingActorWarning=false;pointerCapture?.Reset();
        }
        public void EndSource()
        {sourceId=null;cached=false;PlacementAvailable=false;pointerCapture?.Reset();}
        // Compatibility for callers that only know the source's upper point.
        public void Follow(Camera camera,Vector3 worldTop)=>FollowAtPlane(camera,new Rect(worldTop.x,worldTop.y,0,0),worldTop.z);
        public void Follow(Camera camera,Rect worldSourceBounds)=>FollowAtPlane(camera,worldSourceBounds,0);
        private void FollowAtPlane(Camera camera,Rect worldSourceBounds,float worldZ)
        {
            if(camera==null) throw new InvalidOperationException("Окно контейнера: не назначена камера мира.");
            if(canvas==null) ValidateCanvas();
            if(transform.parent!=layoutParent || canvas.rootCanvas.renderMode!=RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException("Окно контейнера: изменился родитель или режим Canvas; повторно вызовите ConfigureLayout для Overlay Canvas.");
            if(!configured && !legacyWarning)
            {
                legacyWarning=true;
                Debug.LogWarning($"Окно контейнера {name}: ConfigureLayout не вызван; обязательные виджеты HUD не защищены. Canvas={canvas.name}.",this);
            }
            if(PlacementDeferred) return;
            var parent=(RectTransform)transform.parent;
            Vector2 size=Rect.rect.size;var bounds=parent.rect;
            // Once opened, this is an interaction surface in screen space.
            // Aiming changes weapon/actor bounds and camera framing: repacking
            // from those bounds would move the target away from its cursor.
            if(PlacementPinned && previousCanvas==bounds && previousSize==size)
            {ApplyPlacement(parent);return;}
            var source=Project(camera,parent,worldSourceBounds,worldZ);
            int count=0;
            foreach(var item in reserved)
            {
                if(item==null) throw new InvalidOperationException("Окно контейнера: защищённый виджет уничтожен; обновите ConfigureLayout.");
                if(item.gameObject.activeInHierarchy) obstacles[count++]=BoundsInParent(item,parent);
            }
            var actor=worldActorBounds?.Invoke();
            if(requireActorBounds && !actor.HasValue)
            {
                cached=false;PlacementAvailable=false;gameObject.SetActive(false);
                if(!missingActorWarning)
                {
                    missingActorWarning=true;
                    Debug.LogWarning($"Окно контейнера {name}: сетка скрыта — нет границ фактически отрисованного персонажа; sourceId={sourceId??"не назначен"}, {actorContext?.Invoke()??"контекст актёра не подключён"}. Подключите ActorVisual.TryGetDrawnBounds после отрисовки; физическое тело не подставляется.",this);
                }
                return;
            }
            missingActorWarning=false;
            if(actor.HasValue) obstacles[count++]=Project(camera,parent,actor.Value);
            var weapon=worldWeaponBounds?.Invoke();if(weapon.HasValue) obstacles[count++]=Project(camera,parent,weapon.Value);
            var heldItem=worldHeldItemBounds?.Invoke();if(heldItem.HasValue) obstacles[count++]=Project(camera,parent,heldItem.Value);
            bool changed=!cached || previousCanvas!=bounds || previousSource!=source || previousSize!=size || previousCount!=count;
            if(!changed) for(int i=0;i<count;i++) if(previousObstacles[i]!=obstacles[i]) {changed=true;break;}
            if(changed)
            {
                LayoutEvaluationCount++;
                PlacementAvailable=ContainerWindowLayout.TryPlace(bounds,size,source,obstacles,count,anchorGap,screenMargin,clearance,out placement);
                previousCanvas=bounds;previousSource=source;previousSize=size;previousCount=count;
                Array.Copy(obstacles,previousObstacles,count);cached=true;
            }
            if(PlacementAvailable)
                ApplyPlacement(parent);
            else
            {
                if(gameObject.activeSelf) gameObject.SetActive(false);
                if(!packingWarning)
                {
                    packingWarning=true;
                    Debug.LogWarning($"Окно контейнера {name}: сетка скрыта — нет свободного места без перекрытия HUD/источника/рисунка. sourceId={sourceId??"не назначен"}, sourceWorld={worldSourceBounds}, sourceCanvas={source}, Canvas={bounds}, grid={size}, obstacles={count}. Подбор и ожидание подтверждения сохраняются; освободите место или закройте окно по E/Escape.",this);
                }
            }
        }
        private void ApplyPlacement(RectTransform parent)
        {
            var position=new Vector2(placement.center.x,placement.yMin)-parent.rect.center;
            if(Rect.anchoredPosition!=position) Rect.anchoredPosition=position;
            if(!gameObject.activeSelf) gameObject.SetActive(true);
        }
        private void InitializePointerCapture()
        {
            if(pointerCapture!=null) return;
            pointerCapture=GetComponent<ContainerPointerCapture>()??gameObject.AddComponent<ContainerPointerCapture>();
            pointerCapture.Bind(pointerCapture);
            foreach(var button in Slots)
                (button.GetComponent<ContainerPointerCapture>()??button.gameObject.AddComponent<ContainerPointerCapture>()).Bind(pointerCapture);
            (Scrollbar.GetComponent<ContainerPointerCapture>()??Scrollbar.gameObject.AddComponent<ContainerPointerCapture>()).Bind(pointerCapture);
        }
        private void ValidateCanvas()
        {
            if((layoutParent=transform.parent as RectTransform)==null || (canvas=GetComponentInParent<Canvas>(true))==null ||
                canvas.rootCanvas.renderMode!=RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException($"Окно контейнера {name}: нужен RectTransform-родитель в ScreenSpaceOverlay Canvas; parent={transform.parent?.name}, Canvas={canvas?.name}, mode={canvas?.rootCanvas.renderMode}, active={gameObject.activeInHierarchy}.");
        }
        private Rect BoundsInParent(RectTransform item,RectTransform parent)
        {
            item.GetWorldCorners(corners);
            var minimum=(Vector2)parent.InverseTransformPoint(corners[0]);var maximum=minimum;
            for(int i=1;i<4;i++)
            {var point=(Vector2)parent.InverseTransformPoint(corners[i]);minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);}
            return UnityEngine.Rect.MinMaxRect(minimum.x,minimum.y,maximum.x,maximum.y);
        }
        private static Rect Project(Camera camera,RectTransform parent,Rect world,float z=0)
        {
            if(!float.IsFinite(world.x) || !float.IsFinite(world.y) || !float.IsFinite(world.width) || !float.IsFinite(world.height) ||
                world.width<0 || world.height<0 || !float.IsFinite(world.xMax) || !float.IsFinite(world.yMax) || !float.IsFinite(z))
                throw new InvalidOperationException("Окно контейнера: неверная world область/плоскость: "+world+", Z="+z+".");
            var minimum=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var maximum=-minimum;
            for(int i=0;i<4;i++)
            {
                var screen=camera.WorldToScreenPoint(new Vector3(i%2==0?world.xMin:world.xMax,i<2?world.yMin:world.yMax,z));
                if(screen.z<=0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,screen,null,out var point))
                    throw new InvalidOperationException("Окно контейнера: источник/рисунок не проецируется перед камерой: "+world+".");
                minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);
            }
            return UnityEngine.Rect.MinMaxRect(minimum.x,minimum.y,maximum.x,maximum.y);
        }
        public void Validate()
        {
            if(!float.IsFinite(anchorGap) || anchorGap<0 || !float.IsFinite(screenMargin) || screenMargin<0 ||
                !float.IsFinite(clearance) || clearance<0 || Rect.localScale!=Vector3.one || Rect.localRotation!=Quaternion.identity)
                throw new InvalidOperationException("Окно контейнера: нужны конечные неотрицательные отступы, единичный масштаб и отсутствие поворота сетки.");
            if(Slots?.Length!=9 || Icons?.Length!=9 || Intents?.Length!=9 || Scrollbar==null || Scrollbar.handleRect==null || Pending==null ||
                Scrollbar.direction!=UnityEngine.UI.Scrollbar.Direction.TopToBottom || GetComponent<Image>()?.raycastTarget!=true ||
                Rect.anchorMin!=new Vector2(.5f,.5f) || Rect.anchorMax!=Rect.anchorMin || Rect.pivot!=new Vector2(.5f,0))
                throw new InvalidOperationException("Окно контейнера: нужны сетка 3×3, фон для ввода, прокрутка и нижняя точка привязки.");
            var unique=new HashSet<Button>();
            for(int i=0;i<9;i++)
                if(Slots[i]==null || Icons[i]==null || Intents[i]==null || Intents[i].gameObject!=Slots[i].gameObject || !unique.Add(Slots[i]) || !Slots[i].transform.IsChildOf(transform) ||
                    !Icons[i].transform.IsChildOf(Slots[i].transform) || Icons[i].raycastTarget)
                    throw new InvalidOperationException("Окно контейнера: неверная ячейка "+i);
            if(GetComponentsInChildren<Text>(true).Length!=0)
                throw new InvalidOperationException("Окно контейнера: в сетке не должно быть текстовых подписей или прежних кнопок.");
        }
        private void OnValidate() {anchorGap=Mathf.Max(0,anchorGap);screenMargin=Mathf.Max(0,screenMargin);clearance=Mathf.Max(0,clearance);cached=false;}
        public static ContainerWindowView Create(Transform parent)
        {
            var source=Resources.Load<ContainerWindowView>("Presentation/ContainerWindow");
            if(source==null) throw new InvalidOperationException("Нет префаба Presentation/ContainerWindow.");
            source.Validate();var instance=Instantiate(source,parent,false);instance.InitializePointerCapture();return instance;
        }
    }
}
