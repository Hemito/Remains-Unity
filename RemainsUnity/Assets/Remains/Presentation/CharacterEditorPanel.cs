using System;
using System.Globalization;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;
namespace Remains.Presentation
{
    public sealed class CharacterEditorPanel : MonoBehaviour
    {
        public static readonly int[] Palette={0xA3A3A3,0xF4EFE0,0x433E3B,0x854609,0xE0A626,0xCD4545,0xBB5795,0x794ABD,0x2382BA,0x3BAE9F,0x16F343,0xFFFFFF};
        private CharacterEditorView view;
        private CharacterLibrary library;
        private BackgroundSaveOperation operation;
        private InventoryArtwork artwork;
        private Action<CharacterLibraryEntry> created;
        private Action cancelled;
        private ActorState preview;
        private int slot=1;
        private bool saving;
        public CharacterEditorView View=>view;
        public ActorState Preview=>preview.Copy();
        public bool Busy=>operation.Busy;
        public static CharacterEditorPanel Open(Transform parent,CharacterLibrary library,InventoryArtwork artwork,Action<CharacterLibraryEntry> created,Action cancelled)
        {
            var view=CharacterEditorView.CreateView(parent);var panel=view.gameObject.AddComponent<CharacterEditorPanel>();
            panel.Initialize(view,library,artwork,created,cancelled);return panel;
        }
        public void Initialize(CharacterEditorView value,CharacterLibrary saves,InventoryArtwork graphics,Action<CharacterLibraryEntry> accepted,Action cancel)
        {
            view=value;library=saves;artwork=graphics;created=accepted;cancelled=cancel;
            operation=gameObject.AddComponent<BackgroundSaveOperation>();
            view.Name.onValueChanged.AddListener(_=>{if(!saving) view.Status.text="";});
            preview=new ActorState {appearance=new CharacterAppearance {version=1},weaponInventoryVersion=1,weapon=null};
            view.HairPrevious.onClick.AddListener(()=>Form(true,-1));view.HairNext.onClick.AddListener(()=>Form(true,1));
            view.EyePrevious.onClick.AddListener(()=>Form(false,-1));view.EyeNext.onClick.AddListener(()=>Form(false,1));
            for(int i=0;i<5;i++) {int index=i;view.Colors[i].onClick.AddListener(()=>{slot=index;Refresh();});}
            for(int i=0;i<Palette.Length;i++) {int index=i;view.Swatches[i].onClick.AddListener(()=>SetColor(Palette[index]));}
            for(int i=0;i<3;i++) {int channel=i;view.Channels[i].onValueChanged.AddListener(value=>Channel(channel,(int)value));}
            view.Hex.onEndEdit.AddListener(text=>
            {
                if(int.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int color) && text.Length==6 && color>=0 && color<=0xFFFFFF) SetColor(color);
                else {view.Status.text="Цвет: шесть цифр RGB, например 2382BA.";Refresh();}
            });
            view.SecondaryEnabled.onValueChanged.AddListener(value=>{preview.appearance.secondaryHairEnabled=value;Refresh();});
            view.Reset.onClick.AddListener(()=>{preview.appearance=new CharacterAppearance {version=1};slot=1;view.Status.text="";Refresh();});
            view.Create.onClick.AddListener(Create);view.Cancel.onClick.AddListener(Cancel);Refresh();
        }
        private int Color()=>slot switch {0=>preview.appearance.fur,1=>preview.appearance.hair,2=>preview.appearance.secondaryHair,3=>preview.appearance.eyes,4=>preview.appearance.magic,_=>throw new InvalidOperationException("Неизвестная область цвета.")};
        private void SetColor(int color)
        {
            switch(slot) {case 0:preview.appearance.fur=color;break;case 1:preview.appearance.hair=color;break;case 2:preview.appearance.secondaryHair=color;break;case 3:preview.appearance.eyes=color;break;case 4:preview.appearance.magic=color;break;}
            Refresh();
        }
        private void Channel(int channel,int value)
        {int shift=(2-channel)*8;SetColor((Color()&~(255<<shift))|(value<<shift));}
        private void Form(bool hair,int change)
        {
            int count=hair?PonyAnimationData.Library.hairStyles:PonyAnimationData.Library.eyeStyles;
            if(hair) preview.appearance.hairStyle=(preview.appearance.hairStyle-1+change+count)%count+1;
            else preview.appearance.eyeStyle=(preview.appearance.eyeStyle-1+change+count)%count+1;
            Refresh();
        }
        private void Refresh()
        {
            view.Portrait.Show(preview,artwork);
            view.HairLabel.text=$"Причёска {preview.appearance.hairStyle} / {PonyAnimationData.Library.hairStyles}";
            view.EyeLabel.text=$"Глаза {preview.appearance.eyeStyle} / {PonyAnimationData.Library.eyeStyles}";
            string[] names={"Шерсть","Грива","Второй цвет гривы","Глаза","Магия"};view.ColorLabel.text=names[slot];
            int[] colors={preview.appearance.fur,preview.appearance.hair,preview.appearance.secondaryHair,preview.appearance.eyes,preview.appearance.magic};
            for(int i=0;i<5;i++) {view.ColorSamples[i].color=PonyAppearance.FromRgb(colors[i]);OriginalButtonStyle.SetSelected(view.Colors[i],i==slot);}
            bool second=preview.appearance.secondaryHairEnabled;view.SecondaryEnabled.SetIsOnWithoutNotify(second);
            view.Colors[2].interactable=second && !saving;
            bool editable=slot!=2 || second;
            view.ColorLabel.color=editable?new Color(.65f,1,.75f):new Color(.4f,.5f,.45f);
            int rgb=Color();view.Hex.SetTextWithoutNotify(rgb.ToString("X6"));view.Hex.interactable=editable && !saving;
            for(int i=0;i<3;i++) {int channel=(rgb>>((2-i)*8))&255;view.Channels[i].SetValueWithoutNotify(channel);view.ChannelValues[i].text=channel.ToString();view.Channels[i].interactable=editable && !saving;}
            foreach(var swatch in view.Swatches) swatch.interactable=editable && !saving;
        }
        private void Create()
        {
            if(operation.Busy) return;
            ActorState actor;
            try
            {
                CharacterName.ValidateNew(view.Name.text);
                actor=new ActorState {id="player:"+view.Name.text,character=view.Name.text,player=true,level="hub",appearance=Remains.Serialization.StateJson.Copy(preview.appearance),controlStateVersion=1};
                PlayerStarterInventory.Give(actor);actor.weaponContent=WeaponCatalog.ManifestCopy();
            }
            catch(Exception error) {view.Status.text=error.Message;return;}
            SetBusy(true);view.Status.text="Создание персонажа…";
            operation.Run(_=>library.Create(actor),entry=>{created(entry);Destroy(gameObject);},error=>
            {SetBusy(false);view.Status.text="Не удалось создать персонажа: "+error.Message;Debug.LogWarning("Создание персонажа "+actor.character+": "+error.Message,this);});
        }
        private void SetBusy(bool busy)
        {
            saving=busy;
            foreach(var button in GetComponentsInChildren<UnityEngine.UI.Button>(true)) button.interactable=!busy;
            view.Name.interactable=!busy;view.SecondaryEnabled.interactable=!busy;
            if(!busy) Refresh();else {view.Hex.interactable=false;foreach(var channel in view.Channels) channel.interactable=false;}
        }
        public void Cancel() {if(operation.Busy) return;cancelled();Destroy(gameObject);}
    }
}
