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
        public static Rect ScreenBounds(RectTransform target)
        {
            if(target==null) throw new System.ArgumentNullException(nameof(target));
            var corners=new Vector3[4];target.GetWorldCorners(corners);
            var minimum=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            var maximum=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var corner in corners)
            {
                var point=RectTransformUtility.WorldToScreenPoint(null,corner);
                minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);
            }
            return Rect.MinMaxRect(minimum.x,minimum.y,maximum.x,maximum.y);
        }
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
        public static IEnumerator HoldOutside(RectTransform target,System.Action afterPress,
            System.Action<Vector2> verifyHeld,System.Action<Vector2> verifyReleased)
        {
            var mouse=InputSystem.AddDevice<Mouse>("SessionProbeMouse");
            try
            {
                yield return null;Canvas.ForceUpdateCanvases();
                var point=RectTransformUtility.WorldToScreenPoint(null,target.TransformPoint(target.rect.center));
                var outside=new Vector2(2,2);
                foreach(bool down in new[]{false,true})
                {
                    var state=new MouseState {position=point};
                    InputSystem.QueueStateEvent(mouse,down?state.WithButton(MouseButton.Left):state);
                    yield return null;yield return null;
                }
                afterPress();
                InputSystem.QueueStateEvent(mouse,new MouseState {position=outside}.WithButton(MouseButton.Left));
                for(int frame=0;frame<4;frame++) {yield return null;verifyHeld(outside);}
                InputSystem.QueueStateEvent(mouse,new MouseState {position=outside});
                yield return null;yield return null;verifyReleased(outside);
            }
            finally {InputSystem.RemoveDevice(mouse);}
        }
    }
}
