using System;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit resource installation, never called by runtime initialization.
    public static class ContainerOpenAudioAuthoring
    {
        public const string ManifestPath="Tools/Research/ContainerOpenAudio/manifest.json";
        public const string AssetPath="Assets/Remains/Resources/Original/Audio/container-small-open.wav";
        private const string Guid="7198d9e26bf54ac8b6d7ec427c68bbcd";
        [Serializable] public sealed class SourceDeclaration {public string definition,sourceXml,sourceOpen,clip,classification;}
        [Serializable] public sealed class SourceFile {public string path,sha256;}
        [Serializable] public sealed class ReusedBank {public string sourceSound,sourcePath,unityPath,resource,sha256,authorityManifest;}
        [Serializable] public sealed class AudioEvidence
        {public string sourcePath,sourceSha256,originalPath,stagePath,unityPath,resource,guid,sha256,pcmSha256;public int sourceBytes,bytes,channels,sampleRate,sampleBytes,frames;}
        [Serializable] public sealed class Manifest {public int schema;public SourceFile definitions;public SourceDeclaration[] declarations;public ReusedBank[] reusedBanks;public AudioEvidence audio;}
        public static string ProjectRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        public static Manifest ReadManifest()
        {
            var manifest=StateJson.FromJson<Manifest>(File.ReadAllText(Path.Combine(ProjectRoot,ManifestPath)));
            Require(manifest?.schema==1 && manifest.definitions!=null && manifest.audio!=null && manifest.declarations!=null,
                "Нет независимого source manifest");
            var ids=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in manifest.declarations)
            {
                Require(entry!=null && !string.IsNullOrEmpty(entry.definition) && ids.Add(entry.definition),"Повторный/пустой source type");
                var xml=XElement.Parse(entry.sourceXml);
                Require((string)xml.Attribute("id")==entry.definition && (string)xml.Attribute("open")==entry.sourceOpen &&
                    (entry.sourceOpen==null?entry.clip==null:!string.IsNullOrWhiteSpace(entry.clip)),
                    "Изменено исходное назначение open: type="+entry.definition);
            }
            var audio=manifest.audio;
            Require(audio.guid==Guid && audio.unityPath=="RemainsUnity/"+AssetPath && audio.resource=="Original/Audio/container-small-open" &&
                audio.stagePath==".reference/tests/container-open-audio-r68/resources/container-small-open.wav" &&
                audio.channels==2 && audio.sampleRate==44100 && audio.sampleBytes==2 && audio.frames==35712 && audio.bytes==142892,
                "Изменён явный PCM/native resource contract");
            Require(audio.sourceSha256=="a5d7ab79962d6f26dfa13ac8690342e5f8aabb598077ec33fcf0805d43ff3aa5" &&
                audio.sha256=="d779f569d3ee545d5a34b1219eef02be1bcafafe2c2f09a4c0b548ab189dc630" &&
                audio.pcmSha256=="cc9c1ea122b3c5aa8d20c135764b697347fb57e971dea894e30be99632ad9330",
                "Изменена выбранная конверсия исходного small_open");
            string source=Path.Combine(ProjectRoot,manifest.definitions.path);
            if(File.Exists(source)) Require(Hash(File.ReadAllBytes(source))==manifest.definitions.sha256,"Изменён доступный AllData source");
            source=Path.Combine(ProjectRoot,audio.sourcePath);
            Require(File.Exists(source) && Hash(File.ReadAllBytes(source))==audio.sourceSha256,"Нет/изменён tracked исходный MP3");
            source=Path.Combine(ProjectRoot,audio.originalPath);
            if(File.Exists(source)) Require(Hash(File.ReadAllBytes(source))==audio.sourceSha256,"Tracked MP3 не совпадает с выбранным оригинальным export");
            Require(manifest.reusedBanks?.Length==3,"Нет provenance трёх прежних банков");
            foreach(var reused in manifest.reusedBanks)
            {
                Require(reused!=null && File.Exists(Path.Combine(ProjectRoot,reused.authorityManifest)) &&
                    Hash(File.ReadAllBytes(Path.Combine(ProjectRoot,reused.unityPath)))==reused.sha256,
                    "Изменён прежний исходный банк: "+reused?.sourceSound);
                source=Path.Combine(ProjectRoot,reused.sourcePath);
                if(File.Exists(source)) Require(Hash(File.ReadAllBytes(source))==reused.sha256,"Изменён доступный original reused bank: "+reused.sourceSound);
            }
            return manifest;
        }
        public static void Install()
        {
            var evidence=ReadManifest().audio;
            string destination=Path.Combine(ProjectRoot,evidence.unityPath);
            if(File.Exists(destination)) CheckPcm(File.ReadAllBytes(destination),evidence);
            else
            {
                string stage=Path.Combine(ProjectRoot,evidence.stagePath);
                Require(File.Exists(stage),"Не подготовлен PCM stage; выполните Tools/Research/ContainerOpenAudio/export_audio.py --av-path .reference/dependencies/video-python");
                byte[] bytes=File.ReadAllBytes(stage);CheckPcm(bytes,evidence);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllBytes(destination,bytes);
            }
            string meta=destination+".meta";
            if(File.Exists(meta)) Require(File.ReadAllText(meta).Contains("guid: "+Guid),"Другой GUID уже занимает PCM asset: "+AssetPath);
            else File.WriteAllText(meta,Meta,new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(AssetPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var importer=(AudioImporter)AssetImporter.GetAtPath(AssetPath);
            Require(importer!=null,"Нет native AudioImporter");
            var settings=importer.defaultSampleSettings;
            settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;
            settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;settings.preloadAudioData=true;
            importer.defaultSampleSettings=settings;importer.forceToMono=false;importer.loadInBackground=false;importer.SaveAndReimport();
            Check();
        }
        public static void Check()
        {
            var evidence=ReadManifest().audio;
            CheckPcm(File.ReadAllBytes(Path.Combine(ProjectRoot,evidence.unityPath)),evidence);
            var importer=AssetImporter.GetAtPath(AssetPath) as AudioImporter;
            Require(importer!=null && AssetDatabase.AssetPathToGUID(AssetPath)==Guid && !importer.forceToMono && !importer.loadInBackground &&
                importer.defaultSampleSettings.loadType==AudioClipLoadType.DecompressOnLoad && importer.defaultSampleSettings.preloadAudioData &&
                importer.defaultSampleSettings.compressionFormat==AudioCompressionFormat.PCM &&
                importer.defaultSampleSettings.sampleRateSetting==AudioSampleRateSetting.PreserveSampleRate,
                "Native importer изменил PCM/каналы/частоту/загрузку");
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(AssetPath);
            Require(clip!=null && clip.channels==evidence.channels && clip.frequency==evidence.sampleRate && clip.samples==evidence.frames,
                "Native AudioClip не сохраняет число исходных stereo PCM frames");
        }
        private static void CheckPcm(byte[] bytes,AudioEvidence evidence)
        {
            Require(bytes.Length==evidence.bytes && Hash(bytes)==evidence.sha256,"PCM файл изменён");
            using var input=new BinaryReader(new MemoryStream(bytes));
            Require(new string(input.ReadChars(4))=="RIFF" && input.ReadUInt32()==bytes.Length-8 && new string(input.ReadChars(4))=="WAVE",
                "Некорректный RIFF/WAVE");
            Require(new string(input.ReadChars(4))=="fmt " && input.ReadUInt32()==16 && input.ReadUInt16()==1 &&
                input.ReadUInt16()==evidence.channels && input.ReadUInt32()==evidence.sampleRate &&
                input.ReadUInt32()==evidence.sampleRate*evidence.channels*evidence.sampleBytes &&
                input.ReadUInt16()==evidence.channels*evidence.sampleBytes && input.ReadUInt16()==16,"Изменён PCM format");
            Require(new string(input.ReadChars(4))=="data" && input.ReadUInt32()==evidence.frames*evidence.channels*evidence.sampleBytes,
                "Изменена длина PCM samples");
            Require(Hash(input.ReadBytes(bytes.Length-44))==evidence.pcmSha256,"Изменены decoded PCM samples");
        }
        private static string Hash(byte[] bytes)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void Require(bool valid,string detail)
        {if(!valid) throw new InvalidOperationException("Банк открывания контейнеров: "+detail);}
        private const string Meta="fileFormatVersion: 2\nguid: "+Guid+"\nAudioImporter:\n  externalObjects: {}\n  serializedVersion: 8\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 0\n    quality: 1\n    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 0\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 1\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
    }
}
