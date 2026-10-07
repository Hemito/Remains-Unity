using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    // Content/code mods register during startup. Rendering reads the same prepared item sprites.
    public static class CosmeticItemArtwork
    {
        private sealed class Entry {public Sprite Sprite;public string Source;public bool Replace;}
        private static readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private static bool frozen;
        public static void Register(string id,Sprite sprite,string source,bool replace=false)
        {
            if(frozen) throw new InvalidOperationException("Иконки снаряжения уже используются; регистрируйте их при запуске: "+source);
            if(string.IsNullOrWhiteSpace(id) || sprite==null || sprite.texture==null || string.IsNullOrWhiteSpace(source))
                throw new InvalidOperationException($"Неполная иконка снаряжения: id={id}, source={source}.");
            if(!replace && entries.ContainsKey(id)) throw new InvalidOperationException($"Повтор иконки снаряжения: id={id}, source={source}.");
            entries[id]=new Entry {Sprite=sprite,Source=source,Replace=replace};
        }
        public static void ApplyTo(Dictionary<string,Sprite> target)
        {
            // Validate before publishing any entries into this consumer's dictionary.
            foreach(var entry in entries)
                if(target.ContainsKey(entry.Key) && !entry.Value.Replace) throw new InvalidOperationException(
                    $"Модовая иконка перекрывает базовую без явной замены: id={entry.Key}, source={entry.Value.Source}.");
            frozen=true;
            foreach(var entry in entries) target[entry.Key]=entry.Value.Sprite;
        }
    }
}
