using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    // Presentation-only assignments. A dropped container keeps its real item definition.
    public sealed class ItemLandingAudioCatalog
    {
        public const string Resource="Presentation/item-landing-audio";
        [Serializable] public sealed class Entry
        {
            public string item,action,sourceItem,sourceSound,clip,unresolvedReason;
        }
        [Serializable] public sealed class Settings {public int schema;public Entry[] entries;}
        private readonly struct Sound
        {
            public readonly AudioClip Clip;
            public readonly string Reason;
            public Sound(AudioClip clip,string reason) {Clip=clip;Reason=reason;}
        }
        private readonly Dictionary<string,Sound> sounds=new(StringComparer.Ordinal);
        private ItemLandingAudioCatalog() { }
        public int Count=>sounds.Count;
        public static ItemLandingAudioCatalog Load()
        {
            var asset=GameAssets.Load<TextAsset>(Resource);
            if(asset==null) throw new InvalidOperationException("Нет каталога звуков личных вещей: "+Resource+".");
            return Create(JsonUtility.FromJson<Settings>(asset.text),GameAssets.Load<AudioClip>);
        }
        public static ItemLandingAudioCatalog Create(Settings settings,Func<string,AudioClip> loadClip)
        {
            if(settings?.schema!=1 || settings.entries==null || loadClip==null)
                throw new InvalidOperationException("Некорректная версия/содержимое каталога звуков приземления личных вещей.");
            // Validate the whole declaration before loading any native asset.
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in settings.entries)
            {
                bool hasClip=!string.IsNullOrWhiteSpace(entry?.clip),unresolved=!string.IsNullOrWhiteSpace(entry?.unresolvedReason);
                if(entry==null || string.IsNullOrWhiteSpace(entry.item) || entry.action!="landing" ||
                    string.IsNullOrWhiteSpace(entry.sourceItem) || string.IsNullOrWhiteSpace(entry.sourceSound) ||
                    hasClip==unresolved || !keys.Add(entry.item))
                    throw new InvalidOperationException($"Некорректное/повторное назначение звука: item={entry?.item}, action={entry?.action}, source={entry?.sourceItem}/{entry?.sourceSound}.");
            }
            var catalog=new ItemLandingAudioCatalog();
            var loaded=new Dictionary<string,AudioClip>(StringComparer.Ordinal);
            foreach(var entry in settings.entries)
            {
                AudioClip clip=null;
                if(!string.IsNullOrWhiteSpace(entry.clip) && !loaded.TryGetValue(entry.clip,out clip))
                {
                    clip=loadClip(entry.clip);
                    if(clip==null || clip.samples<=0)
                        throw new InvalidOperationException($"Нет действительного звука приземления: item={entry.item}, action=landing, clip={entry.clip}.");
                    loaded.Add(entry.clip,clip);
                }
                catalog.sounds.Add(entry.item,new Sound(clip,entry.unresolvedReason));
            }
            return catalog;
        }
        public bool TryGet(string itemDefinition,out AudioClip clip,out string reason)
        {
            clip=null;
            if(itemDefinition!=null && sounds.TryGetValue(itemDefinition,out var sound))
            {
                clip=sound.Clip;reason=sound.Reason;
                if(clip==null && reason==null) reason="Объявленный WAV приземления больше не существует: item="+itemDefinition+", action=landing.";
                return clip!=null;
            }
            reason="Не объявлено назначение звука личной вещи для action=landing.";
            return false;
        }
    }
}
