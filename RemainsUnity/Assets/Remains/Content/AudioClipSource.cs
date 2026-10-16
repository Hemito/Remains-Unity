using System;
using UnityEngine;

namespace Remains.Content
{
    // One way the audio catalogs name a clip: a Resources path, or "exported:<resource>" for an original sound imported
    // with the content package (ExportedContentCatalog). A missing clip is an error, never a substitute.
    public static class AudioClipSource
    {
        public const string ExportedPrefix="exported:";
        public static AudioClip Load(string key)
        {
            if(string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Пустой ключ звука.");
            var clip=key.StartsWith(ExportedPrefix,StringComparison.Ordinal)
                ?ExportedContentCatalog.Load().Resource(key.Substring(ExportedPrefix.Length)).sounds is {Length:>0} sounds?sounds[0]:null
                :Resources.Load<AudioClip>(key);
            if(clip==null || clip.samples<=0) throw new InvalidOperationException("Нет или пуст звук "+key);
            return clip;
        }
    }
}
