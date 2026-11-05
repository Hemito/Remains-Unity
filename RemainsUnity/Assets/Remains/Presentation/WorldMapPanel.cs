using System.Globalization;
using System.Linq;
using System.Text;
using Remains.Content;
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
            view.Travel.onClick.AddListener(()=>{if(selected && source!=null && TryNumber(out int index)) session.Travel.RequestFactory(source.id,index);});
            view.Index.contentType=UnityEngine.UI.InputField.ContentType.IntegerNumber;
            view.Index.onValueChanged.AddListener(_=>Refresh());
            view.Previous.onClick.AddListener(()=>Step(-1));
            view.Next.onClick.AddListener(()=>Step(1));
            view.gameObject.SetActive(false);session.Changed+=Refresh;session.Pause.Changed+=Refresh;
        }
        public void Open(TravelPoint point)
        {
            var reason=point.Rejection(session.LocalActor);
            if(reason!=null) {session.ShowNotice(reason);return;}
            source=point;selected=false;view.Index.SetTextWithoutNotify("0");view.gameObject.SetActive(true);view.transform.SetAsLastSibling();Refresh();StateChanged?.Invoke();
        }
        public void Close() {if(!IsOpen) return;source=null;view.gameObject.SetActive(false);StateChanged?.Invoke();}
        public void Refresh()
        {
            if(!IsOpen) return;
            var actor=session.LocalActor;
            if(source==null || source.Rejection(actor)!=null) {Close();return;}
            bool valid=TryNumber(out int index);
            string level=valid?WorldFactories.Level(index):null;
            string rejection=!valid?$"Номер завода — целое число от −{WorldFactories.MaxIndex} до {WorldFactories.MaxIndex}.":
                TravelRules.Rejection(actor,actor.level,level,actor.generation,actor.lifecycleSequence,session.Pause.IsPaused(level));
            view.Travel.interactable=selected && rejection==null && !session.Travel.Pending;
            view.Description.text=!selected?"Выберите место на карте.":
                Describe(valid,index,level)+"\n\n"+(rejection??"Отправиться ко входу на завод.")+
                (session.Travel.Pending?"\n\nОжидание подтверждения перехода…":"")+Visited();
            OriginalButtonStyle.SetSelected(view.Factory,selected);
        }
        private bool TryNumber(out int index)=>int.TryParse(view.Index.text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out index) &&
            System.Math.Abs((long)index)<=WorldFactories.MaxIndex;
        private void Step(int delta)
        {
            TryNumber(out int index);
            view.Index.text=Mathf.Clamp(index+delta,-WorldFactories.MaxIndex,WorldFactories.MaxIndex).ToString(CultureInfo.InvariantCulture);
        }
        private static string Name(int index)=>index==0?"Основной завод · 0":"Завод · "+(index<0?"−"+(-index):index.ToString(CultureInfo.InvariantCulture));
        private string Describe(bool valid,int index,string level)
        {
            if(!valid) return "Завод";
            var world=session.World;
            if(world==null) return Name(index);
            var catalog=FactoryProfileCatalog.Load();
            var saved=WorldFactories.Find(world,level);
            var profile=saved!=null?catalog.Saved(saved):catalog.For(WorldFactories.Seed(world),index);
            return $"{Name(index)}\n{profile.title}\n\n{profile.description}"+(saved==null?"\n\nЕщё не посещён: будет создан при первом входе.":"");
        }
        private string Visited()
        {
            var world=session.World;
            if(world==null) return "";
            var numbers=world.factories.Select(f=>WorldFactories.TryIndex(f.level,out int index)?index:(int?)null).Where(i=>i!=null).Select(i=>i.Value).OrderBy(i=>i).ToArray();
            if(numbers.Length==0) return "";
            var text=new StringBuilder("\n\nПосещённые: ");
            for(int i=0;i<numbers.Length;i++) text.Append(i>0?", ":"").Append(numbers[i]<0?"−"+(-numbers[i]):numbers[i].ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
