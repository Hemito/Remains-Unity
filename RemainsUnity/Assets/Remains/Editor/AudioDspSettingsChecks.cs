using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Uses real Unity UI/prefab/configuration types, but deliberately never resets the host audio device.
    public static class AudioDspSettingsChecks
    {
        private sealed class Device : IAudioConfigurationDevice
        {
            public AudioConfiguration configuration=new() {dspBufferSize=512,sampleRate=48000,
                speakerMode=AudioSpeakerMode.Surround,numRealVoices=31,numVirtualVoices=113};
            public readonly List<AudioConfiguration> calls=new();
            public bool reject;
            public AudioConfiguration Read()=>configuration;
            public bool Reset(AudioConfiguration value)
            {calls.Add(value);if(reject)return false;configuration=value;return true;}
        }
        public static void Run()
        {
            var root=new GameObject("Проверка DSP-настроек",typeof(RectTransform));
            var device=new Device();string saved=null;bool failWrite=false;int writes=0,checks=0;
            void Require(bool valid,string detail)
            {if(!valid)throw new InvalidOperationException("DSP-настройки: "+detail);checks++;}
            var preferences=new AudioPreferences(null,device,json=>
            {if(failWrite)throw new IOException("fixture write failed");saved=json;writes++;});
            try
            {
                var panel=new AudioSettingsPanel(root.transform,preferences);
                var view=root.GetComponentInChildren<AudioSettingsView>(true);view.Validate();
                panel.Open();Require(panel.IsOpen && device.calls.Count==0,"открытие меняет устройство");
                foreach(int samples in new[]{256,512,1024})
                {
                    panel.BufferRect(samples).GetComponent<Button>().onClick.Invoke();
                    Require(view.BufferLabel.text.Contains(samples.ToString()) && device.calls.Count==0,"выбор немедленно сбрасывает звук или не обновляет подпись");
                }
                view.Master.value=43;view.Music.value=27;view.Effects.value=61;
                Require(device.calls.Count==0 && Mathf.Approximately(preferences.Current.master,.43f),"предпросмотр громкости сбрасывает DSP");
                view.Cancel.onClick.Invoke();
                Require(!panel.IsOpen && preferences.Current.master==1 && device.calls.Count==0 && writes==0,"отмена применяет буфер/сохраняет или не восстанавливает громкость");
                panel.Open();view.Buffer256.onClick.Invoke();view.Master.value=70;view.Save.onClick.Invoke();
                var stored=JsonConvert.DeserializeObject<AudioLevels>(saved);
                Require(!panel.IsOpen && writes==1 && device.calls.Count==1 && stored.dspBufferSize==256 && Mathf.Approximately(stored.master,.7f),"сохранение не применяет и не сохраняет выбор");
                var actual=device.configuration;
                Require(actual.dspBufferSize==256 && actual.sampleRate==48000 && actual.speakerMode==AudioSpeakerMode.Surround &&
                    actual.numRealVoices==31 && actual.numVirtualVoices==113,"сброс потерял прежние параметры устройства");
                panel.Open();view.Save.onClick.Invoke();
                Require(device.calls.Count==1 && writes==2,"неизменённый буфер сбрасывает устройство повторно");
                device.reject=true;panel.Open();view.Buffer1024.onClick.Invoke();view.Save.onClick.Invoke();
                Require(panel.IsOpen && device.calls.Count==2 && writes==2 && preferences.Current.dspBufferSize==256 &&
                    !string.IsNullOrWhiteSpace(view.Notice.text),"отказ устройства скрыт или сохранён");
                view.Cancel.onClick.Invoke();device.reject=false;failWrite=true;
                panel.Open();view.Buffer512.onClick.Invoke();view.Save.onClick.Invoke();
                Require(panel.IsOpen && device.calls.Count==4 && writes==2 && device.configuration.dspBufferSize==256 &&
                    preferences.Current.dspBufferSize==256 && view.Notice.text.Contains("сохранить"),"отказ записи не откатывает устройство/выбор");
                view.Cancel.onClick.Invoke();
                Require(!panel.IsOpen && Mathf.Approximately(preferences.Current.master,.7f),"отмена после ошибки потеряла сохранённую громкость");
                var backdrop=view.transform.GetChild(0).GetComponent<RectTransform>();
                Require(backdrop.rect.height>=532 && view.Buffer256.transform.parent==backdrop &&
                    view.Save.GetComponent<RectTransform>().anchoredPosition.y<=-468,"устаревшая геометрия префаба перекрывает DSP-кнопки");
                Debug.Log($"AUDIO_DSP_SETTINGS_PASS: {checks} checks; real prefab/UI and fake device; no hardware latency/listening claim.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
