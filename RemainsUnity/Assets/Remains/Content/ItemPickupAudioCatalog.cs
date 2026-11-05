using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    // Authored pickup assignments reuse original landing sounds. They do not claim
    // that the original played these sounds when its inventory changed.
    public sealed class ItemPickupAudioCatalog
    {
        public const string Resource="Presentation/item-pickup-audio";
        [Serializable] public sealed class Entry {public string item,clip,rule,unresolvedReason;}
        [Serializable] public sealed class Settings {public int schema;public Entry[] entries;}
        private readonly Dictionary<string,(AudioClip Clip,string Reason)> sounds=new(StringComparer.Ordinal);
        public int Count=>sounds.Count;
        public static ItemPickupAudioCatalog Load()
        {
            var source=GameAssets.Load<TextAsset>(Resource)??throw new InvalidOperationException("Нет назначений звуков подбора: "+Resource);
            return Create(JsonUtility.FromJson<Settings>(source.text),AudioClipSource.Load);
        }
        public static ItemPickupAudioCatalog Create(Settings settings,Func<string,AudioClip> load)
        {
            if(settings?.schema!=1 || settings.entries==null || load==null)throw new InvalidOperationException("Неверная схема звуков подбора.");
            var result=new ItemPickupAudioCatalog();var clips=new Dictionary<string,AudioClip>(StringComparer.Ordinal);
            foreach(var entry in settings.entries)
            {
                if(string.IsNullOrWhiteSpace(entry?.item) || string.IsNullOrWhiteSpace(entry.rule) || result.sounds.ContainsKey(entry.item) ||
                    string.IsNullOrWhiteSpace(entry.clip)==string.IsNullOrWhiteSpace(entry.unresolvedReason))
                    throw new InvalidOperationException("Нет явного/однозначного назначения звука подбора: "+entry?.item);
                AudioClip clip=null;
                if(!string.IsNullOrWhiteSpace(entry.clip) && !clips.TryGetValue(entry.clip,out clip))
                {
                    clip=load(entry.clip);
                    if(clip==null || clip.samples<=0)throw new InvalidOperationException("Нет WAV подбора: "+entry.item+" / "+entry.clip);
                    clips.Add(entry.clip,clip);
                }
                result.sounds.Add(entry.item,(clip,entry.unresolvedReason));
            }
            return result;
        }
        public bool TryGet(string item,out AudioClip clip,out string reason)
        {
            clip=null;reason="Нет явного назначения action=pickup.";
            if(item==null)return false;
            int suffix=item.IndexOf(":grade:",StringComparison.Ordinal);
            if(suffix>=0)
            {
                if(!int.TryParse(item.Substring(suffix+7),out int grade) || grade<0) {reason="Неверный грейд предмета подбора.";return false;}
                item=item.Substring(0,suffix);
            }
            if(!sounds.TryGetValue(item,out var sound))return false;
            clip=sound.Clip;reason=sound.Reason;return clip!=null;
        }
    }
}
