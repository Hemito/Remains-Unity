using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class AudioSettingsPanel
    {
        private readonly GameObject root;
        private readonly AudioPreferences preferences;
        private readonly Slider master,music,effects;
        private readonly Text error;
        private AudioLevels before;
        public bool IsOpen=>root.activeSelf;
        public RectTransform SaveRect {get;}
        public RectTransform CancelRect {get;}
        public RectTransform MasterRect=>master.GetComponent<RectTransform>();
        public RectTransform MusicRect=>music.GetComponent<RectTransform>();
        public AudioSettingsPanel(Transform parent,HudElements ui,AudioPreferences preferences)
        {
            this.preferences=preferences;
            root=ui.Panel(parent,"Настройки звука",Vector2.zero,new Vector2(1280,720));
            var overlay=root.GetComponent<RectTransform>();overlay.anchorMin=Vector2.zero;overlay.anchorMax=Vector2.one;overlay.offsetMin=overlay.offsetMax=Vector2.zero;
            root.GetComponent<Image>().color=new Color(0,0,0,.85f);
            var panel=ui.Panel(root.transform,"Звук",new Vector2(350,-145),new Vector2(580,420));
            panel.GetComponent<Image>().color=new Color(.04f,.08f,.07f,1);
            var rect=panel.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
            ui.Label(panel.transform,"Звук",new Vector2(24,-18),new Vector2(532,35)).fontSize=26;
            master=Slider(ui,panel.transform,"Общая громкость",80);
            music=Slider(ui,panel.transform,"Музыка",150);
            effects=Slider(ui,panel.transform,"Игровые звуки",220);
            error=ui.Label(panel.transform,"Изменения слышны сразу. Настройки действуют только на этом ПК.",new Vector2(24,-282),new Vector2(532,58));error.fontSize=16;
            SaveRect=ui.Button(panel.transform,"Сохранить",new Vector2(24,-352),new Vector2(250,44),Save).GetComponent<RectTransform>();
            CancelRect=ui.Button(panel.transform,"Отмена",new Vector2(306,-352),new Vector2(250,44),Cancel).GetComponent<RectTransform>();
            master.onValueChanged.AddListener(_=>Preview());music.onValueChanged.AddListener(_=>Preview());effects.onValueChanged.AddListener(_=>Preview());
            root.SetActive(false);
        }
        private static Slider Slider(HudElements ui,Transform parent,string caption,float y)
        {
            var label=ui.Label(parent,caption,new Vector2(24,-y),new Vector2(532,28));
            var bar=ui.Panel(parent,caption,new Vector2(24,-y-34),new Vector2(532,20));
            bar.GetComponent<Image>().color=new Color(.15f,.22f,.18f);
            var handle=ui.Panel(bar.transform,"Регулятор",Vector2.zero,new Vector2(18,26));
            handle.GetComponent<Image>().color=new Color(.65f,1,.75f,1);
            var handleRect=handle.GetComponent<RectTransform>();handleRect.pivot=new Vector2(.5f,.5f);
            handleRect.anchorMin=handleRect.anchorMax=new Vector2(0,.5f);handleRect.anchoredPosition=Vector2.zero;
            var slider=bar.AddComponent<Slider>();slider.handleRect=handleRect;slider.targetGraphic=handle.GetComponent<Image>();
            slider.minValue=0;slider.maxValue=100;slider.wholeNumbers=true;
            slider.onValueChanged.AddListener(value=>label.text=$"{caption} · {value:0}%");
            return slider;
        }
        public void Open()
        {
            if(IsOpen) return;
            before=preferences.Current;
            master.value=before.master*100;music.value=before.music*100;effects.value=before.effects*100;
            foreach(var slider in new[]{master,music,effects}) slider.onValueChanged.Invoke(slider.value);
            error.text="Изменения слышны сразу. Настройки действуют только на этом ПК.";
            root.SetActive(true);root.transform.SetAsLastSibling();
        }
        private void Preview()
        {if(IsOpen) preferences.Preview(new AudioLevels {master=master.value/100,music=music.value/100,effects=effects.value/100});}
        private void Save()
        {
            try {preferences.Save();root.SetActive(false);}
            catch(Exception failure) {error.text="Не удалось сохранить настройки. "+failure.Message;Debug.LogError(error.text);}
        }
        public void Cancel() {if(!IsOpen) return;preferences.Preview(before);root.SetActive(false);}
    }
}
