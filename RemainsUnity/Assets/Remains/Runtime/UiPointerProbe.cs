using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Remains.Runtime
{
    // A virtual Unity device only. Never injects input into Windows or another process.
    public static class UiPointerProbe
    {
        public static IEnumerator Click(RectTransform target)
        {
            var mouse=InputSystem.AddDevice<Mouse>("SessionProbeMouse");
            try
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                var position=RectTransformUtility.WorldToScreenPoint(null,target.TransformPoint(target.rect.center));
                var hits=new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=position},hits);
                if(hits.Count==0 || (hits[0].gameObject!=target.gameObject && !hits[0].gameObject.transform.IsChildOf(target)))
                    throw new System.InvalidOperationException($"Кнопка закрыта для указателя: target={target.name}, point={position}, screen={Screen.width}x{Screen.height}, hit={(hits.Count==0?"нет":hits[0].gameObject.name)}.");
                InputSystem.QueueStateEvent(mouse,new MouseState {position=position});
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(mouse,new MouseState {position=position}.WithButton(MouseButton.Left));
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(mouse,new MouseState {position=position});
                yield return null;
                yield return null;
            }
            finally {InputSystem.RemoveDevice(mouse);}
        }
    }
}
