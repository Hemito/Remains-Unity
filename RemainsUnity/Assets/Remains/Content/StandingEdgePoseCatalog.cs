using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    public static class StandingEdgePoseCatalog
    {
        private static Dictionary<string,StandingEdgePoseProfile> profiles;
        public static void Load()
        {
            if(profiles!=null) return;
            PonyAnimationData.Load();
            var asset=Resources.Load<TextAsset>("Content/standing-edge-poses");
            if(asset==null) throw new InvalidOperationException("Нет декларации стоячих краевых поз Content/standing-edge-poses.");
            var manifest=JsonUtility.FromJson<StandingEdgePoseManifest>(asset.text);
            if(manifest==null || manifest.schema!=1 || manifest.sourceHash!=PonyRenderAsset.SourceHash() ||
                manifest.alphaMinimum<1 || manifest.alphaMinimum>255 || !float.IsFinite(manifest.soleBandHeight) || manifest.soleBandHeight<=0 ||
                manifest.shapes==null || manifest.shapes.Length==0 || manifest.profiles==null || manifest.profiles.Length==0)
                throw new InvalidOperationException("Некорректная или устаревшая декларация стоячих поз. Перезапеките интервалы из текущей графики.");
            var shapes=new HashSet<string>();
            foreach(var shape in manifest.shapes)
                if(string.IsNullOrWhiteSpace(shape) || !shapes.Add(shape)) throw new InvalidOperationException("Повторная или пустая графическая часть стоячей опоры.");
            var prepared=new Dictionary<string,StandingEdgePoseProfile>();
            foreach(var source in manifest.profiles)
            {
                var profile=new StandingEdgePoseProfile(source);
                void Check(StandingFootPoseDefinition pose)
                {
                    var clip=Array.Find(PonyAnimationData.Library.clips,c=>c.id==pose.poseId);
                    if(clip==null || clip.frames.Length!=1) throw new InvalidOperationException("Нет одно-кадровой краевой позы: "+pose.poseId);
                    foreach(var pad in pose.contacts)
                        if(!shapes.Contains(pad.shape) || !Array.Exists(clip.frames[0].parts,p=>p.shape==pad.shape && p.path==pad.path))
                            throw new InvalidOperationException($"Нет графической подошвы: anatomy={source.anatomy}, pose={pose.poseId}, shape={pad.shape}, path={pad.path}.");
                }
                Check(source.standing);Check(source.crouching);foreach(var pose in source.candidates) Check(pose);
                prepared.Add(profile.Anatomy,profile);
            }
            profiles=prepared;
        }
        public static bool TryGet(string anatomy,out StandingEdgePoseProfile profile)
        {
            if(profiles==null) throw new InvalidOperationException("Каталог стоячих краевых поз не подготовлен.");
            return profiles.TryGetValue(anatomy,out profile);
        }
    }
    // One contextual warning per entity's missing-contact episode. The result
    // remains MissingContact; an ordinary pose is never evidence of support.
    public sealed class StandingEdgePoseDiagnostics
    {
        private readonly HashSet<string> warned=new();
        public void Observe(ActorState actor,IReadOnlyList<StandingSupportInterval> supports,StandingEdgePoseResult result)
        {
            if(result.Outcome!=StandingEdgePoseOutcome.MissingContact) {warned.Remove(actor.id);return;}
            if(!warned.Add(actor.id)) return;
            Debug.LogWarning($"Не найдена контактная краевая поза: actor={actor.id}, anatomy={actor.anatomy}, level={actor.level}, "+
                $"position={actor.Position}, facing={actor.facing}, crouched={actor.crouched}, supports={string.Join(",",supports)}. {result.Reason}");
        }
        public void Clear()=>warned.Clear();
        public void Clear(string actorId)=>warned.Remove(actorId);
    }
}
