using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Presentation
{
    // Optional original presentation banks. The inventory operation remains the only gameplay authority.
    public sealed class ContainerOpenAudio : MonoBehaviour
    {
        [Serializable] public sealed class Entry {public string definition,clip;public float gain;}
        [Serializable] public sealed class Catalog {public int version;public Entry[] entries;}
        private readonly Dictionary<string,(AudioClip clip,float gain)> clips=new(StringComparer.Ordinal);
        private readonly HashSet<string> reported=new();
        private GameplayAudio playback;
        public int Openings {get;private set;}
        public event Action<string,string,AudioClip> Opened;

        public static Catalog Load()
        {
            var text=Resources.Load<TextAsset>("Presentation/container-open-audio");
            if(text==null) throw new InvalidOperationException("Нет каталога Presentation/container-open-audio.");
            var catalog=StateJson.FromJson<Catalog>(text.text);Validate(catalog);return catalog;
        }
        public static void Validate(Catalog catalog)
        {
            if(catalog?.version!=1 || catalog.entries==null)
                throw new InvalidOperationException("Неподдерживаемая версия звуков открывания контейнеров.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in catalog.entries)
                if(entry==null || string.IsNullOrWhiteSpace(entry.definition) || string.IsNullOrWhiteSpace(entry.clip) ||
                    !float.IsFinite(entry.gain) || entry.gain<=0 || entry.gain>1 || !ids.Add(entry.definition))
                    throw new InvalidOperationException("Некорректный/повторный банк контейнера: definition="+entry?.definition+", clip="+entry?.clip);
        }
        public void Initialize(GameplayAudio playback)
        {
            if(this.playback!=null) throw new InvalidOperationException("Звуки контейнеров уже подготовлены.");
            if(playback==null) throw new ArgumentNullException(nameof(playback));
            var prepared=new Dictionary<string,(AudioClip clip,float gain)>(StringComparer.Ordinal);
            var banks=new Dictionary<string,AudioClip>(StringComparer.Ordinal);
            foreach(var entry in Load().entries)
            {
                if(!banks.TryGetValue(entry.clip,out var clip))
                {
                    clip=Resources.Load<AudioClip>(entry.clip);
                    if(clip==null || clip.samples<=0 || clip.frequency<=0 || clip.channels<=0)
                        throw new InvalidOperationException("Нет звука открывания: definition="+entry.definition+", action=open, clip="+entry.clip);
                    banks.Add(entry.clip,clip);
                }
                prepared.Add(entry.definition,(clip,entry.gain));
            }
            foreach(var entry in prepared) clips.Add(entry.Key,entry.Value);
            this.playback=playback;
        }
        public bool TryGet(string definition,out AudioClip clip,out float gain)
        {
            clip=null;gain=0;
            if(definition==null || !clips.TryGetValue(definition,out var entry)) return false;
            clip=entry.clip;gain=entry.gain;return true;
        }
        public void Play(InventorySource source)
        {
            if(source==null || source.Body!=null || source.IsLooseItem) return;
            if(!TryGet(source.Definition,out var clip,out float gain))
            {
                if(reported.Add(source.Definition??"<нет типа>"))
                    Debug.LogWarning($"Открывание контейнера {source.id}, type={source.Definition}, action=open, level={source.level}: исходный банк не объявлен в Presentation/container-open-audio; звук мира пропущен. Обычный отклик интерфейса остаётся независимым.",this);
                return;
            }
            if(playback.PlayContact(clip,source.level,source.Position,gain))
            {Openings++;Opened?.Invoke(source.id,source.Definition,clip);}
        }
    }
}
