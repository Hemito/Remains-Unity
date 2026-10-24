using System;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    [Serializable] public sealed class AudioMixSettings
    {
        public float effectsVolume,panDistance;
        public float Pan(float sourceX,float cameraX)=>Mathf.Lerp(-1,1,Mathf.InverseLerp(-panDistance,panDistance,sourceX-cameraX));
        public static AudioMixSettings Load()
        {
            var asset=GameAssets.Load<TextAsset>("Presentation/audio-mix");
            if(asset==null) throw new InvalidOperationException("Нет настроек Presentation/audio-mix.");
            var mix=JsonUtility.FromJson<AudioMixSettings>(asset.text);
            if(mix==null || !float.IsFinite(mix.effectsVolume) || mix.effectsVolume<0 || mix.effectsVolume>1 ||
                !float.IsFinite(mix.panDistance) || mix.panDistance<=0)
                throw new InvalidOperationException("Некорректная громкость или ширина панорамы Presentation/audio-mix.");
            return mix;
        }
    }
}
