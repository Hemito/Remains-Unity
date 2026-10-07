using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // The saved viewport owns the bounds; only changed text/layout is measured.
    public sealed class InventoryDescriptionView : MonoBehaviour
    {
        [InspectorName("Прокрутка описания")] public ScrollRect Scroll;
        [InspectorName("Видимая область описания")] public RectTransform Viewport;
        [InspectorName("Полный текст описания")] public Text Content;
        [InspectorName("Полоса прокрутки описания")] public Scrollbar Scrollbar;
        private string actorId,itemId,measuredText;
        private Vector2 measuredSize;
        private float measuredUnits;
        private int measuredFont;
        private bool measuring;
        public int MeasurementCount {get;private set;}
        public RectTransform Rect=>(RectTransform)transform;
        public bool CanScroll=>Content.rectTransform.rect.height>Viewport.rect.height+.1f;

        public void SetText(string text,string actor,string item,bool visible)
        {
            bool changedSelection=actorId!=actor || itemId!=item;
            actorId=actor;itemId=item;
            Content.text=text??throw new ArgumentNullException(nameof(text));
            gameObject.SetActive(visible);
            if(!visible) {Scroll.StopMovement();return;}
            Measure();
            if(changedSelection) {Scroll.StopMovement();Scroll.verticalNormalizedPosition=1;}
        }
        private void OnRectTransformDimensionsChange()
        {
            if(Content!=null && Viewport!=null && Scroll!=null && Scrollbar!=null && gameObject.activeInHierarchy) Measure();
        }
        private void Measure()
        {
            if(measuring) return;
            var size=Viewport.rect.size;float units=Content.pixelsPerUnit;
            if(measuredText==Content.text && measuredSize==size && measuredUnits==units && measuredFont==Content.fontSize) return;
            if(!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x<=0 || size.y<=0 || !float.IsFinite(units) || units<=0)
                throw new InvalidOperationException($"Описание инвентаря: неверные границы, actor={actorId}, item={itemId}, viewport={size}, pixelsPerUnit={units}.");
            measuring=true;
            try
            {
                var rect=Content.rectTransform;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,size.x);
                float height=Content.preferredHeight;
                if(!float.IsFinite(height) || height<0) throw new InvalidOperationException(
                    $"Описание инвентаря: неверная высота текста, actor={actorId}, item={itemId}, height={height}.");
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,Mathf.Max(size.y,Mathf.Ceil(height)));
                Scrollbar.gameObject.SetActive(CanScroll);
                Scroll.vertical=CanScroll;
                if(!CanScroll) {Scroll.StopMovement();Scroll.verticalNormalizedPosition=1;}
                measuredText=Content.text;measuredSize=size;measuredUnits=units;measuredFont=Content.fontSize;MeasurementCount++;
            }
            finally {measuring=false;}
        }
        public void Validate()
        {
            if(Scroll==null || Scroll.gameObject!=gameObject || Viewport==null || Viewport.parent!=transform ||
                Content==null || Content.transform.parent!=Viewport || Content.font==null || Content.fontSize<16 ||
                Scroll.viewport!=Viewport || Scroll.content!=Content.rectTransform || Scroll.horizontal ||
                Scroll.movementType!=ScrollRect.MovementType.Clamped || Scroll.inertia ||
                Scroll.verticalScrollbar!=Scrollbar || Scrollbar==null || Scrollbar.handleRect==null || Scrollbar.transform.parent!=transform ||
                Scrollbar.direction!=UnityEngine.UI.Scrollbar.Direction.BottomToTop ||
                Viewport.GetComponent<RectMask2D>()==null || Viewport.GetComponent<Image>() is not {raycastTarget:true} ||
                Content.supportRichText || Content.resizeTextForBestFit || Content.horizontalOverflow!=HorizontalWrapMode.Wrap ||
                Content.verticalOverflow!=VerticalWrapMode.Overflow || !Content.raycastTarget)
                throw new InvalidOperationException($"Описание инвентаря {name}: нужны сохранённые ScrollRect, masked viewport, полный читаемый Text и вертикальный scrollbar без горизонтальной прокрутки.");
        }
    }
}
