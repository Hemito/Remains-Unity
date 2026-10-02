using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    [Serializable]
    public sealed class MusicTrackDefinition
    {
        [InspectorName("ID записи")] public string id;
        [InspectorName("Префикс локации (пусто — меню)")] public string levelPrefix;
        [InspectorName("Музыкальная запись")] public AudioClip clip;
    }
    [CreateAssetMenu(menuName="Remains/Музыка локаций",fileName="MusicCatalog")]
    public sealed class MusicCatalogAsset : ScriptableObject
    {
        [SerializeField,Min(.01f),InspectorName("Переход между записями, с")] private float fadeSeconds=1.5f;
        [SerializeField,Range(0,1),InspectorName("Базовая громкость")] private float volume=.2f;
        [SerializeField,InspectorName("Привязки")] private MusicTrackDefinition[] tracks=Array.Empty<MusicTrackDefinition>();
        public float FadeSeconds=>fadeSeconds;
        public float Volume=>volume;
        public MusicTrackDefinition[] CreateTracks()
        {
            Validate();
            var result=new MusicTrackDefinition[tracks.Length];
            for(int i=0;i<tracks.Length;i++) result[i]=new MusicTrackDefinition {id=tracks[i].id,levelPrefix=tracks[i].levelPrefix,clip=tracks[i].clip};
            return result;
        }
        public void Validate()
        {
            if(!float.IsFinite(fadeSeconds) || fadeSeconds<=0 || !float.IsFinite(volume) || volume<0 || volume>1 || tracks==null || tracks.Length==0)
                throw new InvalidOperationException($"Музыка {name}: неверная громкость, время перехода или пустой список.");
            var ids=new HashSet<string>(StringComparer.Ordinal);var prefixes=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<tracks.Length;i++)
            {
                var track=tracks[i];
                if(track==null || string.IsNullOrWhiteSpace(track.id) || track.clip==null || track.clip.samples<=0)
                    throw new InvalidOperationException($"Музыка {name}: запись {i} без ID или аудиоресурса.");
                if(!ids.Add(track.id) || !prefixes.Add(track.levelPrefix??""))
                    throw new InvalidOperationException($"Музыка {name}: повтор ID/префикса, запись {track.id}, локация {track.levelPrefix}.");
            }
        }
        private void OnValidate()
        {
            try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}
        }
    }
}
