using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class HudFeedbackView : MonoBehaviour
    {
        [InspectorName("Уведомление")] public Text Notice;
        [InspectorName("Поднятие союзника")] public Text Revival;
        [SerializeField,Header("Размер уведомления по тексту"),InspectorName("Минимальная ширина")] private float minimumWidth=220;
        [SerializeField,InspectorName("Максимальная ширина")] private float maximumWidth=880;
        [SerializeField,InspectorName("Минимальная высота")] private float minimumHeight=26;
        [SerializeField,InspectorName("Внутренние отступы")] private Vector2 padding=new(12,8);
        [SerializeField,Header("Отступ уведомления от верхнего края"),InspectorName("Во время игры")] private float gameplayTop=140;
        [SerializeField,InspectorName("При открытом инвентаре")] private float inventoryTop=16;
        public void Validate()
        {
            if(Notice==null || Revival==null || Notice==Revival || Notice.transform.parent==transform ||
                !Notice.transform.IsChildOf(transform) || !Revival.transform.IsChildOf(transform))
                throw new InvalidOperationException("Обратная связь HUD: не назначены отдельные тексты и подложка уведомления.");
            foreach(float value in new[]{minimumWidth,maximumWidth,minimumHeight,padding.x,padding.y,gameplayTop,inventoryTop})
                if(!float.IsFinite(value) || value<0) throw new InvalidOperationException("Обратная связь HUD: размеры и отступы должны быть конечными и неотрицательными.");
            if(minimumWidth<=0 || maximumWidth<minimumWidth || minimumHeight<=0)
                throw new InvalidOperationException("Обратная связь HUD: неверные границы размера уведомления.");
        }
        // Called only when text or inventory visibility changes.
        public void LayoutNotice(bool inventoryOpen)
        {
            float width=Mathf.Clamp(Notice.preferredWidth,minimumWidth,maximumWidth);
            Notice.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
            float height=Mathf.Max(minimumHeight,Notice.preferredHeight);
            var panel=(RectTransform)Notice.transform.parent;
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,1);
            panel.anchoredPosition=new Vector2(0,-(inventoryOpen?inventoryTop:gameplayTop));
            panel.sizeDelta=new Vector2(width+padding.x*2,height+padding.y*2);
            HudElements.Rect(Notice.rectTransform,new Vector2(padding.x,-padding.y),new Vector2(width,height));
        }
        private void OnValidate()
        {
            if(float.IsFinite(minimumWidth)) minimumWidth=Mathf.Max(1,minimumWidth);
            if(float.IsFinite(maximumWidth)) maximumWidth=Mathf.Max(minimumWidth,maximumWidth);
            if(float.IsFinite(minimumHeight)) minimumHeight=Mathf.Max(1,minimumHeight);
            padding=Vector2.Max(Vector2.zero,padding);gameplayTop=Mathf.Max(0,gameplayTop);inventoryTop=Mathf.Max(0,inventoryTop);
        }
        public static HudFeedbackView Create(Transform parent)
        {
            var prefab=Resources.Load<HudFeedbackView>("Presentation/HudFeedback");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/HudFeedback.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
