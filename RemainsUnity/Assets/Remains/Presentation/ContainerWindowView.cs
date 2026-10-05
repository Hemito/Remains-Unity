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
        public event Action<int> ScrollRows;
        public RectTransform Rect=>(RectTransform)transform;
        public void OnScroll(PointerEventData data)
        {
            if(!isActiveAndEnabled || data==null || data.scrollDelta.y==0) return;
            if(ScrollRows==null) {Debug.LogWarning("Окно контейнера: не подключена прокрутка.",this);return;}
            ScrollRows(data.scrollDelta.y<0?1:-1);data.Use();
        }
        public bool ContainsPointer(Vector2 position)=>gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(Rect,position,null);
        public void Follow(Camera camera,Vector3 worldTop)
        {
            if(camera==null) throw new InvalidOperationException("Окно контейнера: не назначена камера мира.");
            var parent=(RectTransform)transform.parent;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,camera.WorldToScreenPoint(worldTop),null,out var anchor))
                throw new InvalidOperationException("Окно контейнера: не удалось расположить окно над источником.");
            Vector2 size=Rect.rect.size;
            Rect.anchoredPosition=new Vector2(
                Mathf.Clamp(anchor.x,parent.rect.xMin+screenMargin+size.x*.5f,parent.rect.xMax-screenMargin-size.x*.5f),
                Mathf.Clamp(anchor.y+anchorGap,parent.rect.yMin+screenMargin,parent.rect.yMax-screenMargin-size.y));
        }
        public void Validate()
        {
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
        private void OnValidate() {anchorGap=Mathf.Max(0,anchorGap);screenMargin=Mathf.Max(0,screenMargin);}
        public static ContainerWindowView Create(Transform parent)
        {
            var source=Resources.Load<ContainerWindowView>("Presentation/ContainerWindow");
            if(source==null) throw new InvalidOperationException("Нет префаба Presentation/ContainerWindow.");
            source.Validate();return Instantiate(source,parent,false);
        }
    }
}
