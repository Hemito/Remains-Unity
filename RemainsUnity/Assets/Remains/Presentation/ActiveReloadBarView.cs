using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class ActiveReloadBarView : MonoBehaviour
    {
        [SerializeField,InspectorName("Окно быстрого нажатия")] private Image window;
        [SerializeField,InspectorName("Указатель времени")] private Image marker;
        [SerializeField,Min(0),InspectorName("Отступ над персонажем, м")] private float headOffset=.25f;
        private Camera worldCamera;
        public void Validate()
        {
            if(window==null || marker==null || window==marker || !window.transform.IsChildOf(transform) || !marker.transform.IsChildOf(transform) ||
                !float.IsFinite(headOffset) || headOffset<0)
                throw new InvalidOperationException("Полоса перезарядки: отсутствуют отдельные окно/указатель или неверен отступ.");
            window.raycastTarget=marker.raycastTarget=false;
        }
        public static ActiveReloadBarView Create(Transform parent,Camera camera)
        {
            var prefab=GameAssets.Load<ActiveReloadBarView>("Presentation/ActiveReloadBar");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/ActiveReloadBar.");
            var view=Instantiate(prefab,parent,false);view.Validate();view.worldCamera=camera;view.gameObject.SetActive(false);return view;
        }
        public void Refresh(ActiveReloadClock clock,Rect? bodyBounds,bool visible)
        {
            if(!visible || clock==null || !clock.Running || !bodyBounds.HasValue || worldCamera==null)
            {gameObject.SetActive(false);return;}
            var bounds=bodyBounds.Value;
            var screen=worldCamera.WorldToScreenPoint(new Vector3(bounds.center.x,bounds.yMax+headOffset,0));
            if(screen.z<=0 || screen.x<0 || screen.y<0 || screen.x>Screen.width || screen.y>Screen.height ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent,screen,null,out var point))
            {gameObject.SetActive(false);return;}
            var root=(RectTransform)transform;root.anchoredPosition=point;
            var range=window.rectTransform;range.anchorMin=new Vector2(clock.WindowStart,0);range.anchorMax=new Vector2(clock.WindowStart+clock.WindowWidth,1);
            range.anchoredPosition=range.sizeDelta=Vector2.zero;window.gameObject.SetActive(clock.Opportunity);
            var cursor=marker.rectTransform;cursor.anchorMin=new Vector2(clock.Progress,0);cursor.anchorMax=new Vector2(clock.Progress,1);
            cursor.anchoredPosition=Vector2.zero;
            gameObject.SetActive(true);
        }
    }
}
