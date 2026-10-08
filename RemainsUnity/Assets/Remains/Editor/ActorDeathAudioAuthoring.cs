using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorDeathAudioAuthoring
    {
        private const string Manifest="Tools/Research/ActorDeathAudio68/manifest.json";
        private static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        [Serializable] private sealed class Sound {public string id,source,resource,assetPath,sha256,pcmSha256;public int samples,sampleRate,channels,sampleBytes;public long bytes;}
        [Serializable] private sealed class Data {public int schema;public Sound[] sounds;}
        private static Data Read()
        {
            string path=Path.Combine(Root,Manifest);if(!File.Exists(path)) throw new InvalidOperationException("Нет manifest голосов смерти: "+Manifest);
            var data=StateJson.FromJson<Data>(File.ReadAllText(path));
            if(data?.schema!=1 || data.sounds==null || data.sounds.Length!=10 || data.sounds.Select(s=>s?.id).Distinct().Count()!=10)
                throw new InvalidOperationException("Изменены исходные десять голосов смерти NPC.");
            var ids=Enumerable.Range(1,6).Select(i=>"rm"+i).Concat(Enumerable.Range(1,4).Select(i=>"bloat"+i)).OrderBy(id=>id,StringComparer.Ordinal);
            if(!data.sounds.Select(s=>s?.id).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(ids))
                throw new InvalidOperationException("Не подтверждены конкретные исходные варианты rm1..6/bloat1..4.");
            foreach(var sound in data.sounds)
            {
                if(sound==null || sound.resource!="Original/Audio/ActorDeath/"+sound.id || sound.assetPath!="Assets/Remains/Resources/"+sound.resource+".wav" ||
                    sound.sampleRate!=44100 || sound.channels!=1 || sound.sampleBytes!=2 || sound.samples<=0)
                    throw new InvalidOperationException("Некорректная registration голоса смерти: "+sound?.id);
                foreach(string file in new[]{Path.Combine(Root,sound.source),Path.Combine(Application.dataPath,"..",sound.assetPath)})
                    if(!File.Exists(file) || new FileInfo(file).Length!=sound.bytes || Hash(file)!=sound.sha256)
                        throw new InvalidOperationException("Изменён исходный PCM/WAV голоса смерти: "+sound.id+", file="+file);
            }
            var catalogPath=Path.Combine(Application.dataPath,"Remains/Resources/"+ActorDeathAudioCatalog.Resource+".json");
            var catalog=StateJson.FromJson<ActorDeathAudioCatalog.Settings>(File.ReadAllText(catalogPath));ActorDeathAudioCatalog.Validate(catalog);
            foreach(var expected in new[]{("raider","rm",6),("radroach","bloat",4)})
            {
                var entry=catalog.entries.SingleOrDefault(e=>e.actorType==expected.Item1);
                if(entry==null || entry.sourceBank!=expected.Item2 || entry.gain!=1 || !entry.clips.SequenceEqual(Enumerable.Range(1,expected.Item3).Select(i=>"Original/Audio/ActorDeath/"+expected.Item2+i)))
                    throw new InvalidOperationException("Изменён явный исходный банк смерти: "+expected.Item1);
            }
            return data;
        }
        public static void Install()
        {
            var data=Read(); // Verify every payload before any importer mutation.
            foreach(var sound in data.sounds)
            {
                AssetDatabase.ImportAsset(sound.assetPath,ImportAssetOptions.ForceSynchronousImport);
                var importer=AssetImporter.GetAtPath(sound.assetPath) as AudioImporter;
                if(importer==null) throw new InvalidOperationException("Нет AudioImporter голоса смерти: "+sound.assetPath);
                var settings=importer.defaultSampleSettings;
                bool changed=settings.loadType!=AudioClipLoadType.DecompressOnLoad || settings.compressionFormat!=AudioCompressionFormat.PCM ||
                    settings.sampleRateSetting!=AudioSampleRateSetting.PreserveSampleRate || importer.forceToMono || importer.loadInBackground || !settings.preloadAudioData;
                if(!changed) continue;
                settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;
                settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;settings.quality=1;
                settings.preloadAudioData=true;importer.defaultSampleSettings=settings;importer.forceToMono=false;importer.loadInBackground=false;
                importer.SaveAndReimport();
            }
            Check();Debug.Log("Голоса смерти NPC: явно подготовлены10 исходных PCM-клипов,2 банка; игровой мир/инвентарь не изменены.");
        }
        public static void Check()
        {
            foreach(var sound in Read().sounds)
            {
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(sound.assetPath);
                var importer=AssetImporter.GetAtPath(sound.assetPath) as AudioImporter;
                if(clip==null || clip.samples!=sound.samples || clip.frequency!=sound.sampleRate || clip.channels!=sound.channels || importer==null ||
                    importer.defaultSampleSettings.loadType!=AudioClipLoadType.DecompressOnLoad || importer.defaultSampleSettings.compressionFormat!=AudioCompressionFormat.PCM ||
                    importer.defaultSampleSettings.sampleRateSetting!=AudioSampleRateSetting.PreserveSampleRate || !importer.defaultSampleSettings.preloadAudioData || importer.loadInBackground || importer.forceToMono)
                    throw new InvalidOperationException($"Не подготовлен native голос смерти: id={sound.id}, path={sound.assetPath}, samples={clip?.samples}, rate={clip?.frequency}, channels={clip?.channels}.");
            }
        }
        private static string Hash(string path)
        {using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    }
}
