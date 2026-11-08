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
                catch(Exception error) {warning("Не удалось возобновить циклический звук. "+error.Message);}
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
            if(!Supported(samples)){reason="Размер DSP-буфера должен быть 256, 512 или 1024 сэмпла.";return false;}
            try {previous=device.Read();}
            catch(Exception error){reason="Не удалось прочитать конфигурацию аудиоустройства. "+error.Message;return false;}
            if(previous.dspBufferSize==samples)return true;
            var requested=previous;requested.dspBufferSize=samples;
            string failure;
            try
            {
                if(device.Reset(requested))
                {
                    var actual=device.Read();
                    if(Same(actual,requested)){changed=true;return true;}
                    failure="Аудиоустройство не применило выбранный буфер без изменения остальных параметров.";
                }
                else failure="Аудиоустройство отклонило изменение DSP-буфера.";
            }
            catch(Exception error){failure="Не удалось перезапустить звук. "+error.Message;}
            bool restored=TryRestore(previous,out string rollback);
            reason=failure+(restored?" Прежняя конфигурация сохранена.":" "+rollback);
            return false;
        }
        public bool TryRestore(AudioConfiguration previous,out string reason)
        {
            reason=null;
            try {if(Same(device.Read(),previous))return true;}
            catch(Exception) { /* The captured configuration is still usable for one recovery attempt. */ }
            try {if(device.Reset(previous) && Same(device.Read(),previous))return true;}
            catch(Exception error){reason="Не удалось восстановить конфигурацию звука. "+error.Message;return false;}
            reason="Не удалось восстановить прежнюю конфигурацию звука. Проверьте устройство или перезапустите игру.";
            return false;
        }
        private static bool Same(AudioConfiguration a,AudioConfiguration b)=>a.dspBufferSize==b.dspBufferSize &&
            a.sampleRate==b.sampleRate && a.speakerMode==b.speakerMode &&
            a.numRealVoices==b.numRealVoices && a.numVirtualVoices==b.numVirtualVoices;
    }
}
