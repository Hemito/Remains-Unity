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
    // Pose-animator clips (Authored/clips/<file>.json, 17 baked nodes per frame) → the game's own authored bank.
    // Every frame borrows one original frame whose nodes carry the same drawings (its donor): parts, draw order,
    // depth paths and wearable instances come from it, each node moved by M·L⁻¹ (M the authored node, L the donor's
    // PonySymbolLayout placement), so the compiled rig reproduces the animator's nodes exactly.
    public static class PonyAuthoredClipImporter
    {
        private const string Folder="Assets/Remains/Editor/PonyAnimation/Authored/clips/";
        private const string Output="Assets/Remains/Resources/Original/Pony/pony-authored.json";
        // Which baked clips the game takes, under which id, and which of their frames.
        private static readonly (string File,string Id,bool NonLooping,int[] Frames)[] Imports=
        {
            ("bed-back",BedRestClip,true,new[] {0}),
        };
        private const string BedRestClip="bedBack";

        [MenuItem("Remains/Импортировать свои анимации пони")]
        public static void Import()
        {
            string baseHash=PonyRenderAsset.SourceHash();
            var original=JsonUtility.FromJson<PonyLibrary>(GameAssets.Load<TextAsset>("Original/Pony/pony").text);
            var layout=new PonySymbolLayout(original);
            var candidates=new List<(PonyClip Clip,int Frame,PonyRigPilotArt.Group[] Groups,Dictionary<string,Matrix4x4> Placed,Dictionary<string,string> Symbols)>();
            foreach(var clip in original.clips)for(int f=0;f<clip.frames.Length;f++)
            {
                var groups=PonyRigPilotArt.Groups(clip.frames[f]).ToArray();
                candidates.Add((clip,f,groups,groups.ToDictionary(g=>g.Socket,g=>layout.Get(clip.id,f,g.Top).Matrix,StringComparer.Ordinal),
                    groups.ToDictionary(g=>g.Socket,g=>layout.Get(clip.id,f,g.Top).Id,StringComparer.Ordinal)));
            }
            var result=new List<PonyAuthoredClip>();float maxError=0;
            foreach(var import in Imports)
            {
                var file=JObject.Parse(File.ReadAllText(Folder+import.File+".json"));
                if((string)file["format"]!="pony-pose-atlas-clip" || (int)file["version"]!=1)throw new InvalidDataException("Не файл анимации аниматора поз: "+import.File+".");
                if((string)file["sourceHash"]!=baseHash)throw new InvalidDataException($"Анимация {import.File} сделана по другой исходной анимации пони: {file["sourceHash"]}, current={baseHash}.");
                var baked=(JArray)file["frames"];
                var clip=new PonyClip {id=import.Id,fps=(float)file["fps"],nonLooping=import.NonLooping,frames=new PonyFrame[import.Frames.Length]};
                var plans=new PonyAuthoredFrame[import.Frames.Length];
                for(int i=0;i<import.Frames.Length;i++)
                {
                    int index=import.Frames[i];
                    if(index<0 || index>=baked.Count)throw new InvalidDataException($"Нет кадра {index} в {import.File}.");
                    var nodes=baked[index]["nodes"].ToDictionary(n=>(string)n["socket"],n=>((string)n["symbol"],Matrix(n["matrix"].Select(v=>(float)v).ToArray())),StringComparer.Ordinal);
                    var donor=candidates.Where(c=>c.Symbols.Count==nodes.Count && nodes.All(n=>c.Symbols.TryGetValue(n.Key,out var s) && s==n.Value.Item1))
                        .OrderBy(c=>nodes.Sum(n=>Vector2.Distance(Translation(c.Placed[n.Key]),Translation(n.Value.Item2)))).FirstOrDefault();
                    if(donor.Clip==null)
                        throw new InvalidDataException($"Для кадра {import.File}/{index} нет исходного кадра с теми же рисунками: {string.Join(", ",nodes.Select(n=>n.Value.Item1))}. Свои рисунки (~N) пока не импортируются.");
                    var parts=JsonUtility.FromJson<PonyFrame>(JsonUtility.ToJson(donor.Clip.frames[donor.Frame])).parts;var moves=new List<PonyAuthoredNode>();
                    foreach(var group in donor.Groups)
                    {
                        var move=nodes[group.Socket].Item2*donor.Placed[group.Socket].inverse;var values=new[] {move.m00,move.m10,move.m01,move.m11,move.m03,move.m13};
                        foreach(int p in group.Indices)parts[p].matrix=PonyAuthoredClips.Move(values,parts[p].matrix);
                        moves.Add(new PonyAuthoredNode {top=group.Top,move=values});
                    }
                    clip.frames[i]=new PonyFrame {parts=parts};
                    plans[i]=new PonyAuthoredFrame {donorClip=donor.Clip.id,donorFrame=donor.Frame,nodes=moves.ToArray()};
                    Debug.Log($"Своя анимация {import.Id}/{i}: кадр-основа {donor.Clip.id}/{donor.Frame}.");
                }
                // The compiled rig places nodes by PonySymbolLayout: it must give back the animator's matrices.
                var check=new PonySymbolLayout(new PonyLibrary {clips=original.clips.Append(clip).ToArray()});
                for(int i=0;i<clip.frames.Length;i++)
                {
                    var nodes=baked[import.Frames[i]]["nodes"];
                    foreach(var group in PonyRigPilotArt.Groups(clip.frames[i]))
                    {
                        var expected=Matrix(nodes.Single(n=>(string)n["socket"]==group.Socket)["matrix"].Select(v=>(float)v).ToArray());var got=check.Get(clip.id,i,group.Top).Matrix;
                        for(int k=0;k<16;k++)maxError=Math.Max(maxError,Math.Abs(expected[k]-got[k]));
                    }
                }
                result.Add(new PonyAuthoredClip {source=import.File+"#"+string.Join(",",import.Frames),clip=clip,frames=plans});
            }
            if(maxError>.0005f)throw new InvalidDataException("Свои анимации не повторяют узлы аниматора: error="+maxError);
            File.WriteAllText(Output,JsonUtility.ToJson(new PonyAuthoredBank {schema=PonyAuthoredClips.Schema,baseSourceHash=baseHash,clips=result.ToArray()},true));
            AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceUpdate);
            PonyAnimationData.Reset();PonyRenderSelection.ResetPrepared();
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
            try{WearableRenderCompiler.Build();PonyRigMotionCompiler.Build();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static Matrix4x4 Matrix(float[] m)
        {
            if(m.Length!=6)throw new InvalidDataException("Узел аниматора: нужна матрица из 6 чисел.");
            var result=Matrix4x4.identity;result.m00=m[0];result.m10=m[1];result.m01=m[2];result.m11=m[3];result.m03=m[4];result.m13=m[5];return result;
        }
        private static Vector2 Translation(Matrix4x4 m)=>new(m.m03,m.m13);
    }
}
