using System;
using UnityEngine;

namespace Remains.Presentation
{
    public interface IAudioConfigurationDevice
    {
        AudioConfiguration Read();
        bool Reset(AudioConfiguration configuration);
    }
    public sealed class UnityAudioConfigurationDevice : IAudioConfigurationDevice
    {
        private readonly Func<AudioConfiguration> read;
        private readonly Func<AudioConfiguration,bool> reset;
        private readonly Func<Action> captureLoops;
        private readonly Action<string> warning;
        public UnityAudioConfigurationDevice(Func<AudioConfiguration> read=null,Func<AudioConfiguration,bool> reset=null,
            Func<Action> captureLoops=null,Action<string> warning=null)
        {
            this.read=read??AudioSettings.GetConfiguration;this.reset=reset??AudioSettings.Reset;
            this.captureLoops=captureLoops??AudioLoopResume.Capture;this.warning=warning??(message=>Debug.LogWarning(message));
        }
        public AudioConfiguration Read()=>read();
        public bool Reset(AudioConfiguration configuration)
        {
            var resume=captureLoops();
            try {return reset(configuration);}
            finally
            {
                // A false/throwing reset can still stop sources, as can the later recovery reset.
                try {resume?.Invoke();}
                catch(Exception error) {warning(Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.a8aa4adce4acaf15", "Не удалось возобновить циклический звук. ", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs")+error.Message);}
            }
        }
    }
    // One deliberate reset per apply, at most one rollback. Never driven by volume preview or buffer selection.
    public sealed class AudioDspBuffer
    {
        private readonly IAudioConfigurationDevice device;
        public AudioDspBuffer(IAudioConfigurationDevice device)=>this.device=device??throw new ArgumentNullException(nameof(device));
        public static bool Supported(int samples)=>samples is 256 or 512 or 1024;
        public int CurrentSamples=>device.Read().dspBufferSize;
        public bool TryApply(int samples,out AudioConfiguration previous,out bool changed,out string reason)
        {
            previous=default;changed=false;reason=null;
            if(!Supported(samples)){reason=Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.c3e35f15a9b57902", "Размер DSP-буфера должен быть 256, 512 или 1024 сэмпла.", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs");return false;}
            try {previous=device.Read();}
            catch(Exception error){reason=Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.dc6dacbdc716857d", "Не удалось прочитать конфигурацию аудиоустройства. ", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs")+error.Message;return false;}
            if(previous.dspBufferSize==samples)return true;
            var requested=previous;requested.dspBufferSize=samples;
            string failure;
            try
            {
                if(device.Reset(requested))
                {
                    var actual=device.Read();
                    if(Same(actual,requested)){changed=true;return true;}
                    failure=Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.d9937f3b84cc5cf3", "Аудиоустройство не применило выбранный буфер без изменения остальных параметров.", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs");
                }
                else failure=Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.56082123140acc72", "Аудиоустройство отклонило изменение DSP-буфера.", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs");
            }
            catch(Exception error){failure=Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.13033f36feff988d", "Не удалось перезапустить звук. ", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs")+error.Message;}
            bool restored=TryRestore(previous,out string rollback);
            reason=failure+(restored?Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.82143a5370026e9e", " Прежняя конфигурация сохранена.", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs"):" "+rollback);
            return false;
        }
        public bool TryRestore(AudioConfiguration previous,out string reason)
        {
            reason=null;
            try {if(Same(device.Read(),previous))return true;}
            catch(Exception) { /* The captured configuration is still usable for one recovery attempt. */ }
            try {if(device.Reset(previous) && Same(device.Read(),previous))return true;}
            catch(Exception error){reason=Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.f8668f9072179544", "Не удалось восстановить конфигурацию звука. ", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs")+error.Message;return false;}
            reason=Remains.Content.GameLocalization.UiText("source.AudioDspBuffer.c1d56d73e352ec7a", "Не удалось восстановить прежнюю конфигурацию звука. Проверьте устройство или перезапустите игру.", "RemainsUnity/Assets/Remains/Presentation/AudioDspBuffer.cs");
            return false;
        }
        private static bool Same(AudioConfiguration a,AudioConfiguration b)=>a.dspBufferSize==b.dspBufferSize &&
            a.sampleRate==b.sampleRate && a.speakerMode==b.speakerMode &&
            a.numRealVoices==b.numRealVoices && a.numVirtualVoices==b.numVirtualVoices;
    }
}
