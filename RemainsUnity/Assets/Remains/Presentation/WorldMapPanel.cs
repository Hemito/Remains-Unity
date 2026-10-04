using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class WorldMapPanel
    {
        private readonly CoopSession session;
        private readonly WorldMapView view;
        private TravelPoint source;
        private bool selected;
        public event System.Action StateChanged;
        public bool IsOpen=>view.gameObject.activeSelf;
        public RectTransform FactoryRect=>view.Factory.GetComponent<RectTransform>();
        public RectTransform TravelRect=>view.Travel.GetComponent<RectTransform>();
        public bool CanTravel=>view.Travel.interactable;
        public WorldMapPanel(Transform parent,CoopSession session)
        {
            this.session=session;view=WorldMapView.Create(parent);
            view.Factory.onClick.AddListener(()=>{selected=true;Refresh();});
            view.Close.onClick.AddListener(Close);
            view.Travel.onClick.AddListener(()=>{if(selected && source!=null) session.Travel.RequestVia(source.id);});
            view.gameObject.SetActive(false);session.Changed+=Refresh;session.Pause.Changed+=Refresh;
        }
        public void Open(TravelPoint point)
        {
            var reason=point.Rejection(session.LocalActor);
            if(reason!=null) {session.ShowNotice(reason);return;}
            source=point;selected=false;view.gameObject.SetActive(true);view.transform.SetAsLastSibling();Refresh();StateChanged?.Invoke();
        }
        public void Close() {if(!IsOpen) return;source=null;view.gameObject.SetActive(false);StateChanged?.Invoke();}
        public void Refresh()
        {
            if(!IsOpen) return;
            var actor=session.LocalActor;
            if(source==null || source.Rejection(actor)!=null) {Close();return;}
            string rejection=TravelRules.Rejection(actor,actor.level,source.destination,actor.generation,
                actor.lifecycleSequence,session.Pause.IsPaused(source.destination));
            view.Travel.interactable=selected && rejection==null && !session.Travel.Pending;
            view.Description.text=!selected?"Выберите место на карте.":
                "Основной завод · 0\n\n"+(rejection??"Отправиться ко входу на завод.")+
                (session.Travel.Pending?"\n\nОжидание подтверждения перехода…":"");
            OriginalButtonStyle.SetSelected(view.Factory,selected);
        }
    }
}
