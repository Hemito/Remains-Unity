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
        public static IEnumerator Click(RectTransform target,Vector2? localPoint=null,bool followTarget=false)
        {
            var mouse=InputSystem.AddDevice<Mouse>("SessionProbeMouse");
            try
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                var position=RectTransformUtility.WorldToScreenPoint(null,target.TransformPoint(localPoint??target.rect.center));
                var hits=new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=position},hits);
                if(hits.Count==0 || (hits[0].gameObject!=target.gameObject && !hits[0].gameObject.transform.IsChildOf(target)))
                    throw new System.InvalidOperationException($"Кнопка закрыта для указателя: target={target.name}, point={position}, screen={Screen.width}x{Screen.height}, hit={(hits.Count==0?"нет":hits[0].gameObject.name)}.");
                float displacement=0;
                foreach(bool down in new[]{false,true,false})
                    for(int frame=0;frame<2;frame++)
                    {
                        if(frame==0 || followTarget)
                        {
                            var point=followTarget?RectTransformUtility.WorldToScreenPoint(null,target.TransformPoint(localPoint??target.rect.center)):position;
                            displacement=Mathf.Max(displacement,Vector2.Distance(position,point));
                            var state=new MouseState {position=point};
                            if(down) state=state.WithButton(MouseButton.Left);
                            InputSystem.QueueStateEvent(mouse,state);
                        }
                        yield return null;
                    }
                if(followTarget) Debug.Log($"Указатель движущегося окна: target={target.name}, displacement={displacement:F2}px.");
            }
            finally {InputSystem.RemoveDevice(mouse);}
        }
    }
}
