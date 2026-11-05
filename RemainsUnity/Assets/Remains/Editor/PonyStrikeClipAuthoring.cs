using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // PT-20261103-04/31: import both COMPLETE original body timelines. Layer 72
    // is the separate swipe effect on frames 5..7, not one of the 17 body sockets.
    // It is explicitly excluded; no other body part/frame is dropped or invented.
    public static class PonyStrikeClipAuthoring
    {
        private const string Output="Assets/Remains/Resources/Original/Pony/pony-authored.json";
        private const string Wardrobe="Assets/Remains/Resources/Original/Pony/Wearables";
        private static readonly string[] Ids={"punch","kick"};
        private sealed class Donor
        {
            public PonyClip Clip;public int Frame;public PonyRigPilotArt.Group Group;public PonyPart First;
        }

        public static void ImportBatch()
        {
            try {Import();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void CheckBatch()
        {
            try {Check();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        [MenuItem("Remains/Пони/Импортировать полные удары оригинала")]
        public static void Import()
        {
            string hash=PonyRenderAsset.SourceHash();
            var retained=ExistingExcept(hash,Ids);
            var strikes=Build(out float error,out int excludedEffects);
            var plans=new PonyAuthoredPlanBank {schema=PonyAuthoredBuildPlans.Schema,baseSourceHash=hash,
                clips=retained.Concat(strikes).ToArray()};
            int banks=ValidateWardrobe(plans);
            // All source/body/wardrobe validation precedes writing either finished asset.
            File.WriteAllText(PonyAuthoredBuildPlans.PathAsset,JsonUtility.ToJson(plans,true));
            File.WriteAllText(Output,JsonUtility.ToJson(new PonyAuthoredBank {schema=PonyAuthoredClips.Schema,
                clips=plans.clips.Select(c=>c.clip).ToArray()},true));
            AssetDatabase.ImportAsset(PonyAuthoredBuildPlans.PathAsset,ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceUpdate);
            PonyAuthoredBuildPlans.Reset();PonyAnimationData.Reset();PonyRenderSelection.ResetPrepared();
            Debug.Log($"Полные удары пони: импортированы punch/kick по20кадров при30FPS, 680узлов; сохранено других клипов {retained.Length}; банков одежды {banks}; ошибка affine {error:G6}; отдельно исключён слой взмаха72 в {excludedEffects}кадрах. Далее PonyAuthoredClipImporter.RebuildBatch отдельным процессом.");
        }
        public static void Check()
        {
            var expected=Build(out float error,out int excludedEffects);
            PonyAuthoredBuildPlans.Reset();var plans=PonyAuthoredBuildPlans.Bank;
            var runtime=JsonUtility.FromJson<PonyAuthoredBank>(File.ReadAllText(Output));
            Require(runtime?.schema==PonyAuthoredClips.Schema && runtime.clips!=null,"Нет готового банка анимаций ударов.");
            foreach(var strike in expected)
            {
                var actual=plans.clips.SingleOrDefault(c=>c.clip.id==strike.clip.id);
                var drawn=runtime.clips.SingleOrDefault(c=>c.id==strike.clip.id);
                Require(actual!=null && drawn!=null,$"Нет полного клипа {strike.clip.id}.");
                Require(JsonUtility.ToJson(actual)==JsonUtility.ToJson(strike),$"Редакторский план {strike.clip.id} не совпадает с полным источником.");
                Require(JsonUtility.ToJson(drawn)==JsonUtility.ToJson(strike.clip),$"Игровой клип {strike.clip.id} отличается от20исходных кадров.");
            }
            ExistingExcept(plans.baseSourceHash,Array.Empty<string>()); // Every other finished clip still matches its own plan.
            int banks=ValidateWardrobe(plans);
            Debug.Log($"Полные удары пони: PASS — 40кадров, 680узлов, {banks}банков одежды, ошибка {error:G6}, слой72 отдельно в {excludedEffects}кадрах.");
        }

        // Shared by targeted importers: preserve every non-target runtime clip and its
        // wardrobe recipe. A changed base or incomplete pair is a visible error, never data loss.
        internal static PonyAuthoredClip[] ExistingExcept(string hash,IEnumerable<string> replaced)
        {
            bool planExists=File.Exists(PonyAuthoredBuildPlans.PathAsset),runtimeExists=File.Exists(Output);
            if(!planExists && !runtimeExists)return Array.Empty<PonyAuthoredClip>(); // Initial authored-bank creation.
            Require(planExists && runtimeExists,"Сохранение своих анимаций: есть только один из runtime-банка и редакторского плана.");
            var plans=JsonUtility.FromJson<PonyAuthoredPlanBank>(File.ReadAllText(PonyAuthoredBuildPlans.PathAsset));
            var runtime=JsonUtility.FromJson<PonyAuthoredBank>(File.ReadAllText(Output));
            Require(plans?.schema==PonyAuthoredBuildPlans.Schema && plans.baseSourceHash==hash && plans.clips!=null,
                "Сохранение своих анимаций: редакторский план относится к другому исходному банку либо имеет неверную схему.");
            Require(runtime?.schema==PonyAuthoredClips.Schema && runtime.clips!=null,"Сохранение своих анимаций: неверный готовый банк.");
            Require(plans.clips.Length==runtime.clips.Length,"Сохранение своих анимаций: число клипов runtime и плана различается.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var plan in plans.clips)
            {
                Require(plan?.clip!=null && ids.Add(plan.clip.id),"Сохранение своих анимаций: пустой либо повторный ID плана.");
                var drawn=runtime.clips.SingleOrDefault(c=>c.id==plan.clip.id);
                Require(drawn!=null && JsonUtility.ToJson(drawn)==JsonUtility.ToJson(plan.clip),
                    $"Сохранение своих анимаций: готовый клип {plan.clip.id} не соответствует редакторскому плану.");
            }
            var targets=new HashSet<string>(replaced,StringComparer.Ordinal);
            return plans.clips.Where(c=>!targets.Contains(c.clip.id)).ToArray();
        }

        private static PonyAuthoredClip[] Build(out float maximumError,out int excludedEffects)
        {
            var source=GameAssets.Load<TextAsset>("Original/Pony/pony")??throw new InvalidOperationException("Нет исходной анимации пони.");
            var original=JsonUtility.FromJson<PonyLibrary>(source.text);
            string extraPath=Path.GetFullPath(PonyPoseAtlas.ExtraLibrary);
            Require(File.Exists(extraPath),"Нет выгруженных полных ударов оригинала: "+extraPath);
            byte[] extraBytes=File.ReadAllBytes(extraPath);
            var extra=JsonUtility.FromJson<PonyLibrary>(System.Text.Encoding.UTF8.GetString(extraBytes));
            Require(original?.clips!=null && extra?.clips!=null,"Неполный исходный банк движений пони.");
            using var sha=SHA256.Create();string digest=BitConverter.ToString(sha.ComputeHash(extraBytes)).Replace("-","").ToLowerInvariant();
            var destination=PonyRigPilotArt.Groups(original.clips.Single(c=>c.id=="idle").frames[0])
                .ToDictionary(g=>g.Socket,g=>g.Top,StringComparer.Ordinal);
            var donors=new Dictionary<string,List<Donor>>(StringComparer.Ordinal);
            foreach(var clip in original.clips)for(int f=0;f<clip.frames.Length;f++)
                foreach(var group in PonyRigPilotArt.Groups(clip.frames[f]))
                {
                    string key=Signature(clip.frames[f],group);
                    if(!donors.TryGetValue(key,out var list))donors.Add(key,list=new List<Donor>());
                    list.Add(new Donor {Clip=clip,Frame=f,Group=group,First=Sorted(clip.frames[f],group)[0]});
                }
            var result=new List<PonyAuthoredClip>();maximumError=0;excludedEffects=0;int nodes=0;
            foreach(string id in Ids)
            {
                var raw=extra.clips.SingleOrDefault(c=>c.id=="x_"+id);
                Require(raw?.frames?.Length==20 && Math.Abs(raw.fps-30)<.0001f,$"Для {id} требуются все20исходных кадров при30FPS.");
                var output=new PonyClip {id=id,fps=raw.fps,nonLooping=true,frames=new PonyFrame[20]};
                var frames=new PonyAuthoredFrame[20];
                for(int f=0;f<20;f++)
                {
                    var body=new PonyFrame {parts=raw.frames[f].parts.Where(p=>Top(p.path)!="72").ToArray()};
                    if(body.parts.Length!=raw.frames[f].parts.Length)excludedEffects++;
                    var groups=PonyRigPilotArt.Groups(body).ToArray();
                    Require(groups.Length==17 && groups.Select(g=>g.Socket).Distinct().Count()==17,$"Удар {id}/{f}: нужны17частей тела без слоя72.");
                    var parts=new List<PonyPart>();var recipes=new List<PonyAuthoredNode>();
                    foreach(var group in groups)
                    {
                        string key=Signature(body,group);var first=Sorted(body,group)[0];
                        Require(donors.TryGetValue(key,out var candidates),$"Удар {id}/{f}/{group.Socket}: нет исходного донора для одежды.");
                        var targetMatrix=PonyRigPilotArt.Affine(first.matrix);
                        var donor=candidates.OrderBy(d=>(PonyRigPilotArt.Affine(d.First.matrix).GetColumn(3)-targetMatrix.GetColumn(3)).sqrMagnitude).First();
                        var move=targetMatrix*PonyRigPilotArt.Affine(donor.First.matrix).inverse;
                        var six=new[]{move.m00,move.m10,move.m01,move.m11,move.m03,move.m13};
                        var sourceFrame=donor.Clip.frames[donor.Frame];
                        var mapping=donor.Group.Indices.ToDictionary(i=>PartKey(sourceFrame.parts[i]),i=>i,StringComparer.Ordinal);
                        string top=destination[group.Socket];var oldIndices=new List<int>();var newIndices=new List<int>();
                        foreach(int index in group.Indices)
                        {
                            var sourcePart=body.parts[index];int old=mapping[PartKey(sourcePart)];
                            var moved=PonyRigPilotArt.Affine(PonyAuthoredBuildPlans.Move(six,sourceFrame.parts[old].matrix));
                            var expected=PonyRigPilotArt.Affine(sourcePart.matrix);
                            for(int k=0;k<16;k++)maximumError=Math.Max(maximumError,Math.Abs(moved[k]-expected[k]));
                            var part=StateJson.Copy(sourcePart);part.path=top+PonySymbolLayout.LocalPath(part.path);
                            part.drawOrder=parts.Count;oldIndices.Add(old);newIndices.Add(parts.Count);parts.Add(part);
                        }
                        recipes.Add(new PonyAuthoredNode {top=top,donorTop=donor.Group.Top,donorClip=donor.Clip.id,
                            donorFrame=donor.Frame,move=six,donorPartIndices=oldIndices.ToArray(),partIndices=newIndices.ToArray()});nodes++;
                    }
                    output.frames[f]=new PonyFrame {parts=parts.ToArray()};
                    frames[f]=new PonyAuthoredFrame {donorClip="per-node",nodes=recipes.ToArray()};
                }
                result.Add(new PonyAuthoredClip {source=$"original/pony-extra/pony-extra.json#x_{id}/0..19;sha256={digest};excludedEffectLayer=72",clip=output,frames=frames});
            }
            Require(nodes==680 && maximumError<.00005f,$"Удары пони: узлов={nodes}/680, ошибка affine={maximumError:G6}.");
            // The same canonical symbol registration that the compiled rig will consume.
            _=new PonySymbolLayout(new PonyLibrary {clips=original.clips.Concat(result.Select(c=>c.clip)).ToArray()});
            return result.ToArray();
        }

        private static int ValidateWardrobe(PonyAuthoredPlanBank plans)
        {
            int count=0;
            var catalog=AssetDatabase.LoadAssetAtPath<WearableRenderCatalogAsset>(WearableRenderCompiler.CatalogPath);
            Require(catalog!=null && catalog.Profiles.Length>0,"Нет явного каталога банков одежды.");
            foreach(string path in catalog.Profiles.Select(profile=>AssetDatabase.GetAssetPath(profile).Replace("-render.asset",".json")).OrderBy(p=>p,StringComparer.Ordinal))
            {
                var bank=StateJson.FromJson<WearableSourceBank>(File.ReadAllText(path));
                Require(bank?.clips!=null,"Нет кадров исходного банка одежды: "+path);
                PonyAuthoredBuildPlans.Extend(bank,path,plans);
                PonyAuthoredBuildPlans.Extend(bank,path,plans); // Validate completed depth/index bindings too.
                foreach(string id in Ids)
                    Require(bank.clips.Single(c=>c.id==id).frames.Length==20,$"Одежда {path}: потеряны кадры {id}.");
                count++;
            }
            Require(count>0,"Не найдены исходные банки одежды для полной проверки ударов.");
            return count;
        }
        private static PonyPart[] Sorted(PonyFrame frame,PonyRigPilotArt.Group group)=>group.Indices.Select(i=>frame.parts[i])
            .OrderBy(p=>PonySymbolLayout.LocalPath(p.path),StringComparer.Ordinal).ThenBy(p=>p.shape,StringComparer.Ordinal)
            .ThenBy(p=>p.hairStyle).ThenBy(p=>p.eyeStyle).ToArray();
        private static string Signature(PonyFrame frame,PonyRigPilotArt.Group group)=>group.Socket+"|"+string.Join("|",Sorted(frame,group).Select(PartKey));
        private static string PartKey(PonyPart part)=>PonySymbolLayout.LocalPath(part.path)+":"+part.shape+":"+part.hairStyle+":"+part.eyeStyle;
        private static string Top(string path)=>path.Split('/')[0];
        private static void Require(bool condition,string message) {if(!condition)throw new InvalidDataException(message);}
    }
}

