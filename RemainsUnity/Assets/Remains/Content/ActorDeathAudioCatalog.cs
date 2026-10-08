using System;
using System.Collections.Generic;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    // Prepared presentation data. Code/content mods can supply a catalog explicitly at Initialize.
    public sealed class ActorDeathAudioCatalog
    {
        public const string Resource="Presentation/actor-death-audio";
        [Serializable] public sealed class Entry {public string actorType,sourceBank;public string[] clips;public float gain;}
        [Serializable] public sealed class Settings {public int schema;public Entry[] entries;}
        public readonly struct Sound
        {
            public readonly AudioClip Clip;
            public readonly string Resource;
            public readonly float Gain;
            public Sound(AudioClip clip,string resource,float gain) {Clip=clip;Resource=resource;Gain=gain;}
        }
        private readonly Dictionary<string,Sound[]> sounds=new(StringComparer.Ordinal);
        public int Count=>sounds.Count;
        private ActorDeathAudioCatalog() { }
        public static ActorDeathAudioCatalog Load()
        {
            var text=Resources.Load<TextAsset>(Resource);
            if(text==null) throw new InvalidOperationException("Нет каталога голосов смерти NPC: "+Resource+".");
            return Create(StateJson.FromJson<Settings>(text.text),Resources.Load<AudioClip>);
        }
        public static void Validate(Settings settings)
        {
            if(settings?.schema!=1 || settings.entries==null)
                throw new InvalidOperationException("Некорректная версия/содержимое каталога голосов смерти NPC.");
            var types=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in settings.entries)
            {
                if(entry==null || string.IsNullOrWhiteSpace(entry.actorType) || string.IsNullOrWhiteSpace(entry.sourceBank) ||
                    !types.Add(entry.actorType) || entry.clips==null || entry.clips.Length==0 ||
                    !float.IsFinite(entry.gain) || entry.gain<=0 || entry.gain>1)
                    throw new InvalidOperationException($"Некорректный/повторный голос смерти: type={entry?.actorType}, bank={entry?.sourceBank}.");
                var clips=new HashSet<string>(StringComparer.Ordinal);
                foreach(string clip in entry.clips)
                    if(string.IsNullOrWhiteSpace(clip) || !clips.Add(clip))
                        throw new InvalidOperationException($"Пустой/повторный вариант голоса смерти: type={entry.actorType}, clip={clip}.");
            }
        }
        public static ActorDeathAudioCatalog Create(Settings settings,Func<string,AudioClip> loadClip)
        {
            Validate(settings);
            if(loadClip==null) throw new ArgumentNullException(nameof(loadClip));
            var result=new ActorDeathAudioCatalog();var loaded=new Dictionary<string,AudioClip>(StringComparer.Ordinal);
            foreach(var entry in settings.entries)
            {
                var bank=new Sound[entry.clips.Length];
                for(int i=0;i<bank.Length;i++)
                {
                    string key=entry.clips[i];
                    if(!loaded.TryGetValue(key,out var clip))
                    {
                        clip=loadClip(key);
                        if(clip==null || clip.samples<=0 || clip.frequency<=0 || clip.channels<=0)
                            throw new InvalidOperationException($"Нет действительного голоса смерти: type={entry.actorType}, bank={entry.sourceBank}, clip={key}.");
                        loaded.Add(key,clip);
                    }
                    bank[i]=new Sound(clip,key,entry.gain);
                }
                result.sounds.Add(entry.actorType,bank);
            }
            return result;
        }
        // Selection randomness belongs to presentation; it must not advance a gameplay generator.
        public bool TrySelect(string actorType,int variant,out Sound sound)
        {
            sound=default;
            if(actorType==null || !sounds.TryGetValue(actorType,out var bank)) return false;
            if(variant<0) throw new ArgumentOutOfRangeException(nameof(variant),"Индекс варианта голоса смерти должен быть неотрицательным.");
            sound=bank[variant%bank.Length];return true;
        }
    }
}
