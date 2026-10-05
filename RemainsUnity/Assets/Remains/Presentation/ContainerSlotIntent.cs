using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // Keep the displayed ID even if a remote claim shifts the grid during a press.
    public sealed class ContainerSlotIntent : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, ISubmitHandler
    {
        private string displayed,pressed;
        private Button button;
        private Action<string> claim;
        public void Initialize(Button control,Action<string> action) {button=control;claim=action;}
        public void Bind(string item)=>displayed=item;
        public void OnPointerDown(PointerEventData data)
        {if(data.button==PointerEventData.InputButton.Left) pressed=displayed;}
        public void OnPointerClick(PointerEventData data)
        {
            if(data.button!=PointerEventData.InputButton.Left) return;
            string item=pressed;pressed=null;Submit(item);
        }
        public void OnSubmit(BaseEventData data) {Submit(displayed);data.Use();}
        private void Submit(string item)
        {
            if(item==null || button==null || !button.IsInteractable()) return;
            if(claim==null) {Debug.LogWarning("Ячейка контейнера: не подключён подбор, item="+item,this);return;}
            claim(item);
        }
        private void OnDisable()=>pressed=null;
    }
}
