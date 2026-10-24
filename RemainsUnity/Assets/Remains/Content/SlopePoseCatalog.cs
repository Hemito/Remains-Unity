using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class SlopePoseDefinition
    {public string anatomy,standUp,standDown,walkUp,walkDown;}
    [Serializable] public sealed class SlopePoseManifest
    {public int schema;public SlopePoseDefinition[] profiles;}

    // Capability, not an anatomical default: undeclared species keep their own poses.
    public sealed class SlopePoseProfile
    {
        public string Anatomy {get;}
        public string StandUp {get;}
        public string StandDown {get;}
        public string WalkUp {get;}
        public string WalkDown {get;}
        internal SlopePoseProfile(SlopePoseDefinition source)
        {Anatomy=source.anatomy;StandUp=source.standUp;StandDown=source.standDown;WalkUp=source.walkUp;WalkDown=source.walkDown;}
        public string Select(bool moving,bool facingUphill)=>moving?(facingUphill?WalkUp:WalkDown):(facingUphill?StandUp:StandDown);
        public bool IsStanding(string pose)=>pose==StandUp || pose==StandDown;
        public bool IsMoving(string pose)=>pose==WalkUp || pose==WalkDown;
    }

    public static class SlopePoseCatalog
    {
        public const string Resource="Content/slope-poses";
        private static IReadOnlyDictionary<string,SlopePoseProfile> profiles;
        public static void Load()
        {
            if(profiles!=null) return;
            PonyAnimationData.Load();
            var asset=GameAssets.Load<TextAsset>(Resource);
            var anatomy=GameAssets.Load<TextAsset>("Content/anatomy");
            if(asset==null || anatomy==null) throw new InvalidOperationException("Нет декларации наклонных поз или анатомии: "+Resource+", Content/anatomy.");
            profiles=Create(StateJson.FromJson<SlopePoseManifest>(asset.text),StateJson.FromJson<AnatomyCatalog>(anatomy.text),PonyAnimationData.Library);
        }
        public static bool TryGet(string anatomy,out SlopePoseProfile profile)
        {
            if(profiles==null) throw new InvalidOperationException("Каталог наклонных поз не подготовлен: "+Resource+".");
            return profiles.TryGetValue(anatomy,out profile);
        }
        public static IReadOnlyDictionary<string,SlopePoseProfile> Create(SlopePoseManifest manifest,AnatomyCatalog anatomy,PonyLibrary library)
        {
            if(manifest?.schema!=1 || manifest.profiles==null || anatomy?.anatomies==null || library?.clips==null)
                throw new InvalidOperationException("Некорректная версия или содержимое каталога наклонных поз: "+Resource+".");
            var prepared=new Dictionary<string,SlopePoseProfile>(StringComparer.Ordinal);
            foreach(var source in manifest.profiles)
            {
                if(source==null || string.IsNullOrWhiteSpace(source.anatomy) || prepared.ContainsKey(source.anatomy))
                    throw new InvalidOperationException("Пустая или повторная анатомия наклонных поз: "+source?.anatomy+".");
                var definition=Array.Find(anatomy.anatomies,a=>a?.id==source.anatomy);
                if(definition==null || !string.IsNullOrEmpty(definition.atlasArtwork))
                    throw new InvalidOperationException($"Наклонные составные позы требуют объявленную составную анатомию: anatomy={source.anatomy}, atlas={definition?.atlasArtwork}.");
                var ids=new HashSet<string>(StringComparer.Ordinal);
                void Check(string id,bool moving)
                {
                    if(string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new InvalidOperationException($"Пустая или повторная наклонная поза: anatomy={source.anatomy}, pose={id}.");
                    var clip=Array.Find(library.clips,c=>c?.id==id);
                    bool valid=clip?.frames!=null && float.IsFinite(clip.fps) && clip.fps>0;
                    valid&=moving?clip!=null && clip.frames!=null && clip.frames.Length>=2 && !clip.nonLooping && clip.playIntro && clip.loopStartFrame>0 && clip.loopStartFrame<clip.frames.Length:
                        clip!=null && clip.frames!=null && clip.frames.Length==1 && clip.nonLooping && !clip.playIntro && clip.loopStartFrame==0;
                    if(!valid) throw new InvalidOperationException($"Некорректный клип наклонной позы: anatomy={source.anatomy}, pose={id}, moving={moving}. Для стойки нужен один кадр; для движения — вступление и явный цикл.");
                }
                Check(source.standUp,false);Check(source.standDown,false);Check(source.walkUp,true);Check(source.walkDown,true);
                prepared.Add(source.anatomy,new SlopePoseProfile(source));
            }
            return new ReadOnlyDictionary<string,SlopePoseProfile>(prepared);
        }
    }
}
