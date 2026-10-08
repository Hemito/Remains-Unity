using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class HealingItemAudioAuthoring
    {
        public const string ManifestPath="Tools/Research/HealingItemAudio68/manifest.json";
        public const string AssetPath="Assets/Remains/Resources/Original/Audio/healing-item-use.wav";
        public const string Resource="Original/Audio/healing-item-use";
        public const string AssetGuid="e4a5dfd6c0934ddb9e43266d0b1a4d22";
        [Serializable] public sealed class SourceFile {public string path,sha256;}
        [Serializable] public sealed class WaveEvidence
        {public string path,sha256,pcmSha256;public int bytes,frames,sampleRate,channels,sampleBytes;public bool sourcePayloadPcmEqual;}
        [Serializable] public sealed class Manifest
        {public int schema,soundId;public string definition,sourceItem,sourceUseSound,unityPath,resource;public float gain;public SourceFile soundXml;public WaveEvidence wav;}
        private static string Project=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        public static Manifest ReadManifest()
        {
            var record=StateJson.FromJson<Manifest>(File.ReadAllText(Path.Combine(Project,ManifestPath)));
            Require(record?.schema==1 && record.definition=="heal" && record.sourceItem=="pot1" && record.sourceUseSound=="pot_use" &&
                record.soundId==19 && record.resource==Resource && record.unityPath==AssetPath && record.gain==.5f && record.soundXml!=null,
                "Изменён явный source/item/sound contract");
            var wav=record.wav;
            Require(wav!=null && wav.path=="Tools/Research/HealingItemAudio68/pot_use.wav" && wav.bytes==63916 && wav.frames==31936 &&
                wav.sampleRate==44100 && wav.channels==1 && wav.sampleBytes==2 && wav.sourcePayloadPcmEqual &&
                wav.sha256=="cec64daa52cb70c4ff98fdf4b87e1d891ce832d1103045eceba825215e1b3627" &&
                wav.pcmSha256=="24b74c92ad149c61b362b8d9b760437d4619cfa7c06ab3e342823a55368fd206",
                "Изменён independent native PCM sample contract");
            byte[] bytes=File.ReadAllBytes(Path.Combine(Project,wav.path));
            Require(bytes.Length==wav.bytes && Hash(bytes)==wav.sha256 && Hash(bytes.Skip(44).ToArray())==wav.pcmSha256,
                "Изменён подготовленный WAV или исходный PCM payload");
            string xmlPath=Path.Combine(Project,record.soundXml.path);
            if(File.Exists(xmlPath))
            {
                byte[] xml=File.ReadAllBytes(xmlPath);
                Require(Hash(xml)==record.soundXml.sha256 && record.soundXml.sha256=="6fcfca182b005bb2382752d102a4e3bb4253f712291055ef402d3a570571c9cc",
                    "Изменён доступный выбранный Sound XML");
                var sound=XElement.Parse(System.Text.Encoding.UTF8.GetString(xml)).Descendants("item")
                    .Single(e=>(string)e.Attribute("type")=="DefineSoundTag" && (string)e.Attribute("soundId")=="19");
                Require((string)sound.Attribute("soundFormat")=="3" && (string)sound.Attribute("soundRate")=="3" &&
                    (string)sound.Attribute("soundSize")=="true" && (string)sound.Attribute("soundType")=="false" &&
                    (string)sound.Attribute("soundSampleCount")=="31936","Source Sound19 не является заявленным native mono44100 PCM16");
                string hex=(string)sound.Attribute("soundData");
                Require(hex?.Length==wav.frames*4,"Нет полного исходного PCM payload Sound19");
                var samples=new byte[hex.Length/2];
                for(int i=0;i<samples.Length;i++) samples[i]=Convert.ToByte(hex.Substring(i*2,2),16);
                Require(Hash(samples)==wav.pcmSha256 && samples.SequenceEqual(bytes.Skip(44)),"PCM samples не совпадают с исходным Sound19");
            }
            return record;
        }
        public static void Install()
        {
            var record=ReadManifest();string target=Path.Combine(Application.dataPath,"../",AssetPath);
            byte[] source=File.ReadAllBytes(Path.Combine(Project,record.wav.path));
            if(File.Exists(target)) Require(File.ReadAllBytes(target).SequenceEqual(source),"Другой WAV занимает native resource: "+AssetPath);
            else {Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,source);}
            string meta=target+".meta";
            if(File.Exists(meta)) Require(File.ReadAllText(meta).Contains("guid: "+AssetGuid),"Другой GUID занимает лечебный WAV");
            else File.WriteAllText(meta,Meta,new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(AssetPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var importer=AssetImporter.GetAtPath(AssetPath) as AudioImporter;Require(importer!=null,"Нет AudioImporter");
            var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=AudioCompressionFormat.PCM;settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData=true;importer.defaultSampleSettings=settings;
            importer.forceToMono=false;importer.loadInBackground=false;importer.SaveAndReimport();Check();
        }
        public static void Check()
        {
            var record=ReadManifest();string target=Path.Combine(Application.dataPath,"../",AssetPath);
            Require(Hash(File.ReadAllBytes(target))==record.wav.sha256,"Imported WAV не совпадает с native source samples");
            var importer=AssetImporter.GetAtPath(AssetPath) as AudioImporter;
            Require(importer!=null && AssetDatabase.AssetPathToGUID(AssetPath)==AssetGuid && !importer.forceToMono && !importer.loadInBackground &&
                importer.defaultSampleSettings.loadType==AudioClipLoadType.DecompressOnLoad && importer.defaultSampleSettings.preloadAudioData &&
                importer.defaultSampleSettings.compressionFormat==AudioCompressionFormat.PCM &&
                importer.defaultSampleSettings.sampleRateSetting==AudioSampleRateSetting.PreserveSampleRate,"Изменена native PCM/частота/загрузка");
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(AssetPath);
            Require(clip!=null && clip.frequency==44100 && clip.channels==1 && clip.samples==31936,"Native AudioClip dimensions не совпадают с Sound19");
        }
        private static string Hash(byte[] bytes)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void Require(bool valid,string detail)
        {if(!valid)throw new InvalidOperationException("Исходный лечебный звук: "+detail);}
        private const string Meta="fileFormatVersion: 2\nguid: "+AssetGuid+"\nAudioImporter:\n  externalObjects: {}\n  serializedVersion: 8\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 0\n    quality: 1\n    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 0\n  loadInBackground: 0\n  ambisonic: 0\n  3D: 1\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
    }
}
