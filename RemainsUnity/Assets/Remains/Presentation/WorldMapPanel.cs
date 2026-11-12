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
            string rejection=!valid?Remains.Content.GameLocalization.Format("source.WorldMapPanel.710299722d4c2cc8", $"Номер завода — целое число от −{WorldFactories.MaxIndex} до {WorldFactories.MaxIndex}.", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs"):
                TravelRules.Rejection(actor,actor.level,level,actor.generation,actor.lifecycleSequence,session.Pause.IsPaused(level));
            view.Travel.interactable=selected && rejection==null && !session.Travel.Pending;
            view.Description.text=!selected?Remains.Content.GameLocalization.UiText("source.WorldMapPanel.02fad132bb0f1292", "Выберите место на карте.", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs"):
                Describe(valid,index,level)+"\n\n"+(rejection??Remains.Content.GameLocalization.UiText("source.WorldMapPanel.84a992b8f91b615d", "Отправиться ко входу на завод.", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs"))+
                (session.Travel.Pending?Remains.Content.GameLocalization.UiText("source.WorldMapPanel.8218d666da6be18a", "\n\nОжидание подтверждения перехода…", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs"):"")+Visited();
            OriginalButtonStyle.SetSelected(view.Factory,selected);
        }
        private bool TryNumber(out int index)=>int.TryParse(view.Index.text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out index) &&
            System.Math.Abs((long)index)<=WorldFactories.MaxIndex;
        private void Step(int delta)
        {
            TryNumber(out int index);
            view.Index.text=Mathf.Clamp(index+delta,-WorldFactories.MaxIndex,WorldFactories.MaxIndex).ToString(CultureInfo.InvariantCulture);
        }
        private static string Name(int index)=>index==0?Remains.Content.GameLocalization.UiText("source.WorldMapPanel.94bb6e3f1bb85658", "Основной завод · 0", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs"):Remains.Content.GameLocalization.UiText("source.WorldMapPanel.215b097420e2edba", "Завод · ", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs")+(index<0?"−"+(-index):index.ToString(CultureInfo.InvariantCulture));
        private string Describe(bool valid,int index,string level)
        {
            if(!valid) return Remains.Content.GameLocalization.UiText("source.WorldMapPanel.375a930de738b8e4", "Завод", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs");
            var world=session.World;
            if(world==null) return Name(index);
            var catalog=FactoryProfileCatalog.Load();
            var saved=WorldFactories.Find(world,level);
            var profile=saved!=null?catalog.Saved(saved):catalog.For(WorldFactories.Seed(world),index);
            string title=GameLocalization.UiText("factory.profile."+profile.id+".title",profile.title,"WorldMapPanel"),
                description=GameLocalization.UiText("factory.profile."+profile.id+".description",profile.description,"WorldMapPanel");
            return $"{Name(index)}\n{title}\n\n{description}"+(saved==null?Remains.Content.GameLocalization.UiText("source.WorldMapPanel.9cf90bffcb9523d9", "\n\nЕщё не посещён: будет создан при первом входе.", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs"):"");
        }
        private string Visited()
        {
            var world=session.World;
            if(world==null) return "";
            var numbers=world.factories.Select(f=>WorldFactories.TryIndex(f.level,out int index)?index:(int?)null).Where(i=>i!=null).Select(i=>i.Value).OrderBy(i=>i).ToArray();
            if(numbers.Length==0) return "";
            var text=new StringBuilder(Remains.Content.GameLocalization.UiText("source.WorldMapPanel.b929e90358965481", "\n\nПосещённые: ", "RemainsUnity/Assets/Remains/Presentation/WorldMapPanel.cs"));
            for(int i=0;i<numbers.Length;i++) text.Append(i>0?", ":"").Append(numbers[i]<0?"−"+(-numbers[i]):numbers[i].ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
