using System;
using System.IO;
using Newtonsoft.Json;
using Remains.Persistence;

namespace Remains.Presentation
{
    public sealed class AudioLevels
    {
        [JsonProperty(Required=Required.Always)] public float master=1;
        [JsonProperty(Required=Required.Always)] public float music=1;
        [JsonProperty(Required=Required.Always)] public float effects=1;
        // Missing in old local preferences: keep the device/project default until an explicit save.
        public int dspBufferSize;
        public AudioLevels Copy()=>new() {master=master,music=music,effects=effects,dspBufferSize=dspBufferSize};
        public void Validate()
        {
            if(dspBufferSize!=0 && !AudioDspBuffer.Supported(dspBufferSize))throw new InvalidDataException("Неверный размер DSP-буфера.");
            foreach(float value in new[]{master,music,effects})
                if(!float.IsFinite(value) || value<0 || value>1)
                    throw new InvalidDataException("Громкость должна быть числом от 0 до 1.");
        }
    }

    // Local user preferences. No character/world data and no network replication.
    public sealed class AudioPreferences
    {
        private AudioLevels levels;
        private readonly Action<string> persist;
        public AudioDspBuffer Dsp {get;}
        public AudioLevels Current=>levels.Copy();
        public event Action Changed;
        public AudioPreferences(string path,IAudioConfigurationDevice device=null,Action<string> persist=null)
        {
            this.persist=persist??(json=>{if(path!=null)AtomicSaveFile.Write(path,json);});
            Dsp=new AudioDspBuffer(device??new UnityAudioConfigurationDevice());levels=new AudioLevels();
            if(path==null || !File.Exists(path)) return;
            try
            {
                levels=JsonConvert.DeserializeObject<AudioLevels>(File.ReadAllText(path));
                if(levels==null) throw new InvalidDataException("Пустые настройки звука.");
                levels.Validate();
            }
            catch(Exception error) {throw new InvalidDataException($"Не удалось прочитать настройки звука: {path}. {error.Message}",error);}
        }
        public void Preview(AudioLevels value)
        {value.Validate();var preview=value.Copy();preview.dspBufferSize=levels.dspBufferSize;levels=preview;Changed?.Invoke();}
        public bool ApplySavedBuffer(out string reason)
        {
            reason=null;
            return levels.dspBufferSize==0 || Dsp.TryApply(levels.dspBufferSize,out _,out _,out reason);
        }
        public bool TrySaveBuffer(int samples,out string reason)
        {
            if(!Dsp.TryApply(samples,out var previous,out bool changed,out reason))return false;
            var candidate=levels.Copy();candidate.dspBufferSize=samples;
            try {persist(JsonConvert.SerializeObject(candidate,Formatting.Indented));}
            catch(Exception error)
            {
                bool restored=!changed || Dsp.TryRestore(previous,out _);
                reason=Remains.Content.GameLocalization.UiText("source.AudioPreferences.b9f961492f1a8bcb", "Не удалось сохранить настройки звука. ", "RemainsUnity/Assets/Remains/Presentation/AudioPreferences.cs")+error.Message+(restored?Remains.Content.GameLocalization.UiText("source.AudioPreferences.d0a8c804020825da", " Прежний DSP-буфер восстановлен.", "RemainsUnity/Assets/Remains/Presentation/AudioPreferences.cs"):
                    Remains.Content.GameLocalization.UiText("source.AudioPreferences.d46ec6cc6f3bb3b0", " Прежний DSP-буфер восстановить не удалось; проверьте устройство или перезапустите игру.", "RemainsUnity/Assets/Remains/Presentation/AudioPreferences.cs"));
                return false;
            }
            levels=candidate;Changed?.Invoke();return true;
        }
        public void Save()
        {
            persist(JsonConvert.SerializeObject(levels,Formatting.Indented));
        }
    }
}
