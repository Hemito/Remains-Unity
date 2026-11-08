using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Published pose-animator clips (17 baked nodes per frame) → the game's own authored bank.
    // Source-symbol registration is Editor-only preparation. The game bank contains only resolved clip frames;
    // wardrobe preparation recipes and provenance never ship as runtime animation data.
    public static class PonyAuthoredClipImporter
    {
        private const string Folder="Assets/Remains/Editor/PonyAnimation/Authored/Published/";
        private const string Output="Assets/Remains/Resources/Original/Pony/pony-authored.json";
        // Which baked clips the game takes, under which id, and which of their frames.
        private static readonly (string File,string Id,bool NonLooping,int[] Frames)[] Imports=
        {
            ("bed-back-confirmed",BedRestClip,true,new[] {0}),
        };
        private const string BedRestClip="bedBack";

        [MenuItem("Remains/Импортировать свои анимации пони")]
        public static void Import()
        {
            string baseHash=PonyRenderAsset.SourceHash();
            var original=JsonUtility.FromJson<PonyLibrary>(GameAssets.Load<TextAsset>("Original/Pony/pony").text);
            var layout=new PonySymbolLayout(original);
            var destinationGroups=PonyRigPilotArt.Groups(original.clips.Single(c=>c.id=="idle").frames[0]).ToDictionary(g=>g.Socket,StringComparer.Ordinal);
            var candidates=new List<(PonyClip Clip,int Frame,PonyRigPilotArt.Group[] Groups,Dictionary<string,Matrix4x4> Placed,Dictionary<string,string> Symbols)>();
            foreach(var clip in original.clips)for(int f=0;f<clip.frames.Length;f++)
            {
                var groups=PonyRigPilotArt.Groups(clip.frames[f]).ToArray();
                candidates.Add((clip,f,groups,groups.ToDictionary(g=>g.Socket,g=>layout.Get(clip.id,f,g.Top).Matrix,StringComparer.Ordinal),
                    groups.ToDictionary(g=>g.Socket,g=>layout.Get(clip.id,f,g.Top).Id,StringComparer.Ordinal)));
            }
            // A targeted reimport (for example bedBack) must not erase other finished clips.
            var result=PonyStrikeClipAuthoring.ExistingExcept(baseHash,Imports.Select(i=>i.Id)).ToList();float maxError=0;
            foreach(var import in Imports)
            {
                var file=JObject.Parse(File.ReadAllText(Folder+import.File+".json"));
                if((string)file["format"]!="pony-pose-atlas-clip" || (int)file["version"]!=1)throw new InvalidDataException("Не файл анимации аниматора поз: "+import.File+".");
                // Baked frames are absolute node matrices: another pony.json only matters when a drawing they name is gone,
                // which the donor search below reports by name.
                if((string)file["sourceHash"]!=baseHash)Debug.LogWarning($"Анимация {import.File} сделана по другой исходной анимации пони: {file["sourceHash"]}, current={baseHash}. Импорт проверит, что все её рисунки есть в игре.");
                var baked=(JArray)file["frames"];
                var clip=new PonyClip {id=import.Id,fps=(float)file["fps"],nonLooping=import.NonLooping,frames=new PonyFrame[import.Frames.Length]};
                var plans=new PonyAuthoredFrame[import.Frames.Length];
                for(int i=0;i<import.Frames.Length;i++)
                {
                    int index=import.Frames[i];
                    if(index<0 || index>=baked.Count)throw new InvalidDataException($"Нет кадра {index} в {import.File}.");
                    var nodes=baked[index]["nodes"].ToDictionary(n=>(string)n["socket"],n=>((string)n["symbol"],Matrix(n["matrix"].Select(v=>(float)v).ToArray())),StringComparer.Ordinal);
                    if(nodes.Count!=17 || nodes.Keys.Any(k=>!destinationGroups.ContainsKey(k)))throw new InvalidDataException($"Для кадра {import.File}/{index} требуется17 известных гнёзд.");
                    var parts=new List<PonyPart>();var moves=new List<PonyAuthoredNode>();
                    // A cutout pose may mix a running tail with bent legs. Resolve donors independently, preserving
                    // the exported node order instead of substituting one whole original frame.
                    foreach(var node in nodes)
                    {
                        var donor=candidates.Where(c=>c.Symbols.TryGetValue(node.Key,out var symbol) && symbol==node.Value.Item1)
                            .OrderBy(c=>Vector2.Distance(Translation(c.Placed[node.Key]),Translation(node.Value.Item2))).FirstOrDefault();
                        if(donor.Clip==null)throw new InvalidDataException($"Нет исходного рисунка {node.Value.Item1}: {import.File}/{index}/{node.Key}. Свои рисунки ~N пока не поддержаны.");
                        var group=donor.Groups.Single(g=>g.Socket==node.Key);string top=destinationGroups[node.Key].Top;
                        var move=node.Value.Item2*donor.Placed[node.Key].inverse;var values=new[] {move.m00,move.m10,move.m01,move.m11,move.m03,move.m13};
                        var indices=new List<int>();
                        foreach(int old in group.Indices)
                        {
                            var part=JsonUtility.FromJson<PonyPart>(JsonUtility.ToJson(donor.Clip.frames[donor.Frame].parts[old]));
                            part.matrix=PonyAuthoredBuildPlans.Move(values,part.matrix);part.path=top+PonySymbolLayout.LocalPath(part.path);
                            part.drawOrder=parts.Count;indices.Add(parts.Count);parts.Add(part);
                        }
                        moves.Add(new PonyAuthoredNode {top=top,donorTop=group.Top,donorClip=donor.Clip.id,donorFrame=donor.Frame,
                            donorPartIndices=group.Indices.ToArray(),partIndices=indices.ToArray(),move=values});
                    }
                    clip.frames[i]=new PonyFrame {parts=parts.ToArray()};
                    plans[i]=new PonyAuthoredFrame {donorClip="per-node",nodes=moves.ToArray()};
                    Debug.Log($"Своя анимация {import.Id}/{i}: {string.Join(", ",moves.Select(m=>$"{m.top}←{m.donorClip}/{m.donorFrame}"))}.");
                }
                // The compiled rig places nodes by PonySymbolLayout: it must give back the animator's matrices.
                var check=new PonySymbolLayout(new PonyLibrary {clips=original.clips.Append(clip).ToArray()});
                for(int i=0;i<clip.frames.Length;i++)
                {
                    var nodes=baked[import.Frames[i]]["nodes"];
                    foreach(var group in PonyRigPilotArt.Groups(clip.frames[i]))
                    {
                        var node=nodes.Single(n=>(string)n["socket"]==group.Socket);
                        var expected=Matrix(node["matrix"].Select(v=>(float)v).ToArray());var placement=check.Get(clip.id,i,group.Top);var got=placement.Matrix;
                        if(placement.Id!=(string)node["symbol"])throw new InvalidDataException($"Импорт изменил рисунок {clip.id}/{i}/{group.Socket}: {placement.Id}");
                        for(int k=0;k<16;k++)maxError=Math.Max(maxError,Math.Abs(expected[k]-got[k]));
                    }
                }
                result.Add(new PonyAuthoredClip {source=import.File+"#"+string.Join(",",import.Frames),clip=clip,frames=plans});
            }
            if(maxError>.0005f)throw new InvalidDataException("Свои анимации не повторяют узлы аниматора: error="+maxError);
            File.WriteAllText(PonyAuthoredBuildPlans.PathAsset,JsonUtility.ToJson(new PonyAuthoredPlanBank {schema=PonyAuthoredBuildPlans.Schema,baseSourceHash=baseHash,clips=result.ToArray()},true));
            File.WriteAllText(Output,JsonUtility.ToJson(new PonyAuthoredBank {schema=PonyAuthoredClips.Schema,clips=result.Select(c=>c.clip).ToArray()},true));
            AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceUpdate);
            PonyAuthoredBuildPlans.Reset();PonyAnimationData.Reset();PonyRenderSelection.ResetPrepared();
            Debug.Log($"Свои анимации пони: {result.Count} ({string.Join(", ",result.Select(c=>c.clip.id))}), error={maxError:G4}. Дальше: Remains → Пересобрать графику пони и движения куклы.");
        }
        public static void ImportBatch()
        {
            try{Import();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        // A separate process after ImportBatch: the library caches the bank on first load.
        public static void RebuildBatch()
        {
            try
            {
                PonyAuthoredSupportAuthoring.Build();
                PonyRenderCompiler.Build();
                var catalog=AssetDatabase.LoadAssetAtPath<WearableRenderCatalogAsset>(WearableRenderCompiler.CatalogPath)??throw new InvalidDataException("Нет каталога одежды.");
                foreach(var profile in catalog.Profiles)
                {
                    string output=AssetDatabase.GetAssetPath(profile),resource=output.Replace("Assets/Remains/Resources/","").Replace("-render.asset","");
                    var json=GameAssets.Load<TextAsset>(resource)??throw new InvalidDataException("Нет исходного банка одежды "+resource);
                    WearableRenderCompiler.BuildAsset(json.text,output,resource);
                }
                PonyRigMotionCompiler.Build();PonyRenderSelection.ResetPrepared();AssetDatabase.SaveAssets();EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static Matrix4x4 Matrix(float[] m)
        {
            if(m.Length!=6 || m.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Узел аниматора: нужна матрица из6 конечных чисел.");
            var result=Matrix4x4.identity;result.m00=m[0];result.m10=m[1];result.m01=m[2];result.m11=m[3];result.m03=m[4];result.m13=m[5];
            if(Math.Abs(result.determinant)<1e-10)throw new InvalidDataException("Вырожденный узел аниматора.");return result;
        }
        private static Vector2 Translation(Matrix4x4 m)=>new(m.m03,m.m13);
    }
}
