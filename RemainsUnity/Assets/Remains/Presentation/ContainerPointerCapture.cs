using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Remains.Presentation
{
    // Background, cells and scrollbar relay to one capture on the window.
    public sealed class ContainerPointerCapture : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,ICancelHandler
    {
        private ContainerPointerCapture owner;
        private readonly Dictionary<int,ContainerPointerCapture> pointers=new();
        private ContainerPointerCapture Owner=>owner==null?this:owner;
        public bool Active=>Owner.pointers.Count>0;
        public void Bind(ContainerPointerCapture root)
        {
            if(root==null || root.owner!=null || root!=this && !transform.IsChildOf(root.transform))
                throw new InvalidOperationException("Окно контейнера: захват указателя должен принадлежать одному корневому окну.");
            owner=root==this?null:root;
        }
        public void OnPointerDown(PointerEventData data)
        {if(data?.button==PointerEventData.InputButton.Left) Owner.pointers[data.pointerId]=this;}
        public void OnPointerUp(PointerEventData data)
        {if(data?.button==PointerEventData.InputButton.Left) Owner.pointers.Remove(data.pointerId);}
        public void OnCancel(BaseEventData data)=>Reset();
        public void Reset()=>Owner.pointers.Clear();
        private void OnDisable()
        {
            if(owner==null) {Reset();return;}
            if(Owner.pointers.Count==0) return;
            var removed=new List<int>();
            foreach(var pointer in Owner.pointers) if(pointer.Value==this) removed.Add(pointer.Key);
            foreach(int id in removed) Owner.pointers.Remove(id);
        }
        private void OnApplicationFocus(bool focused) {if(!focused) Reset();}
    }
}
