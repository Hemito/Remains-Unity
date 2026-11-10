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
        private readonly Text error,bufferLabel;
        private readonly Button buffer256,buffer512,buffer1024;
        private int pendingBuffer;
        private AudioLevels before;
        public bool IsOpen=>root.activeSelf;
        public RectTransform SaveRect {get;}
        public RectTransform CancelRect {get;}
        public RectTransform MasterRect=>master.GetComponent<RectTransform>();
        public RectTransform MusicRect=>music.GetComponent<RectTransform>();
        public RectTransform BufferRect(int samples)=>(samples==256?buffer256:samples==512?buffer512:samples==1024?buffer1024:
            throw new ArgumentOutOfRangeException(nameof(samples))).GetComponent<RectTransform>();
        public AudioSettingsPanel(Transform parent,AudioPreferences preferences)
        {
            this.preferences=preferences;
            var view=AudioSettingsView.Create(parent);root=view.gameObject;
            master=view.Master;music=view.Music;effects=view.Effects;error=view.Notice;
            bufferLabel=view.BufferLabel;buffer256=view.Buffer256;buffer512=view.Buffer512;buffer1024=view.Buffer1024;
            buffer256.onClick.AddListener(()=>SelectBuffer(256));buffer512.onClick.AddListener(()=>SelectBuffer(512));buffer1024.onClick.AddListener(()=>SelectBuffer(1024));
            SaveRect=view.Save.GetComponent<RectTransform>();CancelRect=view.Cancel.GetComponent<RectTransform>();
            view.Save.onClick.AddListener(Save);view.Cancel.onClick.AddListener(Cancel);
            BindCaption(master,view.MasterLabel);BindCaption(music,view.MusicLabel);BindCaption(effects,view.EffectsLabel);
            master.onValueChanged.AddListener(_=>Preview());music.onValueChanged.AddListener(_=>Preview());effects.onValueChanged.AddListener(_=>Preview());
            LocalizedUiText.BindTree(root.transform);root.SetActive(false);
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
            int current=before.dspBufferSize;
            try {current=preferences.Dsp.CurrentSamples;}
            catch(Exception failure){Debug.LogWarning("Не удалось прочитать текущий DSP-буфер. "+failure.Message);}
            pendingBuffer=AudioDspBuffer.Supported(current)?current:before.dspBufferSize!=0?before.dspBufferSize:512;
            SelectBuffer(pendingBuffer);
            error.text="Громкость слышна сразу. DSP-буфер применяется по «Сохранить» с кратким перезапуском звука. Только на этом ПК.";
            root.SetActive(true);root.transform.SetAsLastSibling();
        }
        private void SelectBuffer(int samples)
        {
            pendingBuffer=samples;
            bufferLabel.text=$"DSP-буфер: {samples} сэмплов · 256 — меньше задержка, 1024 — больше устойчивость";
            OriginalButtonStyle.SetSelected(buffer256,samples==256);OriginalButtonStyle.SetSelected(buffer512,samples==512);OriginalButtonStyle.SetSelected(buffer1024,samples==1024);
        }
        private void Preview()
        {if(IsOpen) preferences.Preview(new AudioLevels {master=master.value/100,music=music.value/100,effects=effects.value/100});}
        private void Save()
        {
            try
            {
                if(preferences.TrySaveBuffer(pendingBuffer,out string reason))root.SetActive(false);
                else {error.text=reason;Debug.LogWarning(error.text);}
            }
            catch(Exception failure) {error.text="Не удалось сохранить настройки. "+failure.Message;Debug.LogError(error.text);}
        }
        public void Cancel() {if(!IsOpen) return;preferences.Preview(before);root.SetActive(false);}
    }
}
