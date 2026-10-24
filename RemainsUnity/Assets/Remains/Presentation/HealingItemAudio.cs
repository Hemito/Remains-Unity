using System;
using System.Collections.Generic;
using Remains.Networking;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    public sealed class HealingItemAudio : MonoBehaviour
    {
        public const string Resource="Presentation/healing-item-audio";
        [Serializable] public sealed class Entry {public string definition,clip;public float gain;}
        [Serializable] public sealed class Catalog {public int version;public Entry[] entries;}
        private readonly Dictionary<string,(AudioClip clip,float gain)> prepared=new(StringComparer.Ordinal);
        private readonly HashSet<string> missing=new(StringComparer.Ordinal);
        private SessionHealingAudio source;
        private GameplayAudio playback;
        public int Accepted {get;private set;}
        public int Played {get;private set;}
        public event Action<HealingSoundCue,AudioClip> HealingPlayed;
        public static Catalog Load()
        {
            var data=GameAssets.Load<TextAsset>(Resource);
            if(data==null) throw new InvalidOperationException("Нет каталога звуков лечебных предметов: "+Resource);
            var catalog=JsonUtility.FromJson<Catalog>(data.text);Validate(catalog);return catalog;
        }
        public static void Validate(Catalog catalog)
        {
            if(catalog?.version!=1 || catalog.entries==null)
                throw new InvalidOperationException("Неподдерживаемые данные звуков лечебных предметов.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in catalog.entries)
                if(entry==null || string.IsNullOrWhiteSpace(entry.definition) || string.IsNullOrWhiteSpace(entry.clip) ||
                    !float.IsFinite(entry.gain) || entry.gain<=0 || entry.gain>1 || !ids.Add(entry.definition))
                    throw new InvalidOperationException($"Неверная привязка лечебного звука: definition={entry?.definition}, clip={entry?.clip}.");
        }
        public void Initialize(SessionHealingAudio source,GameplayAudio playback,Catalog catalog=null)
        {
            if(this.source!=null) throw new InvalidOperationException("Звуки лечения уже подготовлены.");
            this.playback=playback??throw new ArgumentNullException(nameof(playback));
            if(source==null) throw new ArgumentNullException(nameof(source));
            catalog??=Load();Validate(catalog);
            foreach(var entry in catalog.entries)
            {
                var clip=GameAssets.Load<AudioClip>(entry.clip);
                if(clip==null || clip.samples<=0 || clip.frequency<=0 || clip.channels<=0)
                    throw new InvalidOperationException($"Нет лечебного звука: definition={entry.definition}, clip={entry.clip}.");
                prepared.Add(entry.definition,(clip,entry.gain));
            }
            this.source=source;source.Received+=Play;
        }
        public bool TryGet(string definition,out AudioClip clip,out float gain)
        {
            clip=null;gain=0;
            if(definition==null || !prepared.TryGetValue(definition,out var entry)) return false;
            clip=entry.clip;gain=entry.gain;return true;
        }
        private void Play(HealingSoundCue cue)
        {
            Accepted++;
            if(!TryGet(cue.definition,out var clip,out float gain))
            {
                if(missing.Add(cue.definition??"<null>"))
                    Debug.LogWarning($"Нет лечебного звука: actor={cue.actor}, item={cue.definition}, transaction={cue.transaction}, world={cue.world}, catalog={Resource}. Звук пропущен.",this);
                return;
            }
            if(!playback.PlayContact(clip,cue.level,cue.Position,gain)) return;
            Played++;HealingPlayed?.Invoke(cue,clip);
        }
        private void OnDestroy()
        {if(source!=null) {source.Received-=Play;source=null;}}
    }
}
