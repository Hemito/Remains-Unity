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
        public AudioSettingsPanel(Transform parent,AudioPreferences preferences)
        {
            this.preferences=preferences;
            var view=AudioSettingsView.Create(parent);root=view.gameObject;
            master=view.Master;music=view.Music;effects=view.Effects;error=view.Notice;
            SaveRect=view.Save.GetComponent<RectTransform>();CancelRect=view.Cancel.GetComponent<RectTransform>();
            view.Save.onClick.AddListener(Save);view.Cancel.onClick.AddListener(Cancel);
            BindCaption(master,view.MasterLabel);BindCaption(music,view.MusicLabel);BindCaption(effects,view.EffectsLabel);
            master.onValueChanged.AddListener(_=>Preview());music.onValueChanged.AddListener(_=>Preview());effects.onValueChanged.AddListener(_=>Preview());
            root.SetActive(false);
        }
        private static void BindCaption(Slider slider,Text label)
        {
            string caption=label.text;
            slider.onValueChanged.AddListener(value=>label.text=$"{caption} · {value:0}%");
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
