using System;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    // Local interface feedback remains audible when the level simulation is paused.
    public sealed class InventoryAudio : MonoBehaviour
    {
        private AudioPreferences preferences;
        private AudioMixSettings mix;
        private AudioSource source;
        private AudioClip toggle,page;
        public void Initialize(AudioPreferences preferences)
        {
            this.preferences=preferences;
            mix=AudioMixSettings.Load();
            toggle=Load("pip3");page=Load("pip2");
            source=gameObject.AddComponent<AudioSource>();
            source.playOnAwake=false;source.spatialBlend=0;
            source.ignoreListenerPause=true;source.mute=Application.isBatchMode;
            preferences.Changed+=ApplyVolume;ApplyVolume();
        }
        private static AudioClip Load(string key)
        {
            var clip=GameAssets.Load<AudioClip>("Original/Audio/"+key);
            if(clip==null || clip.samples<=0)
                throw new InvalidOperationException("Отсутствует или пуст звук интерфейса Original/Audio/"+key);
            return clip;
        }
        private void ApplyVolume()
        {
            var levels=preferences.Current;
            source.volume=.25f*mix.effectsVolume*levels.master*levels.effects;
        }
        public void Toggle()=>source.PlayOneShot(toggle);
        public void Page()=>source.PlayOneShot(page);
        private void OnDestroy()
        {
            if(preferences!=null) preferences.Changed-=ApplyVolume;
        }
    }
}
