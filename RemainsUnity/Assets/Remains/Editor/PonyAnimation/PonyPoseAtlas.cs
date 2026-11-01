using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Every distinct state of every limb chain in the game clips, relative to the torso, for example-driven posing.
    // A state keeps each part's drawing (symbol) and its affine relative to that drawing's standing registration, so
    // any state can be put onto the standing body and states of one chain can be blended. Variant registration and torso
    // normalization follow PonyLegShapeDataset, generalised from one hind leg to all sockets.
    public static class PonyPoseAtlas
    {
        public sealed class Chain
        {
            public string Id,Label,Effector;public string[] Sockets;public string End;public Vector2 Offset;
        }
        // End: the socket whose point is dragged; Effector: the pose-document joint at that point, plus Offset in rest
        // actor space where the joint is not at the tip (the tail's registration origin is its root).
        public static readonly Chain[] Chains=
        {
            new() {Id="hindNear",Label="Задняя ближняя",Sockets=new[]{"hindUpperNear","hindLowerNear"},End="hindLowerNear",Effector="hindHoofNear"},
            new() {Id="hindFar",Label="Задняя дальняя",Sockets=new[]{"hindUpperFar","hindLowerFar"},End="hindLowerFar",Effector="hindHoofFar"},
            new() {Id="frontNear",Label="Передняя ближняя",Sockets=new[]{"frontUpperNear","frontMiddleNear","frontLowerNear","pipbuckNear"},End="frontLowerNear",Effector="frontHoofNear"},
            new() {Id="frontFar",Label="Передняя дальняя",Sockets=new[]{"frontUpperFar","frontMiddleFar","frontLowerFar","pipbuckFar"},End="frontLowerFar",Effector="frontHoofFar"},
            new() {Id="head",Label="Шея и голова",Sockets=new[]{"neck","head","mane"},End="head",Effector="head"},
            new() {Id="tail",Label="Хвост",Sockets=new[]{"tail"},End="tail",Effector="tail",Offset=new Vector2(-.35f,-.35f)}
        };
        private const float RegistrationTolerance=.00005f;

        public static void RunBatch()
        {
            int exit=0;Directory.CreateDirectory(PonyPointStudy.Output);
            try
            {
                var data=Export(out var summary);
                string template=File.ReadAllText("Assets/Remains/Editor/PonyAnimation/PonyPoseAtlas.html");
                if(!template.Contains("/*DATA*/"))throw new InvalidOperationException("Атлас поз: в шаблоне нет места для данных.");
                File.WriteAllText(Path.Combine(PonyPointStudy.Output,"pose-atlas.html"),template.Replace("/*DATA*/",data.ToString(Formatting.None)));
                File.WriteAllText(Path.Combine(PonyPointStudy.Output,"pose-atlas-report.json"),summary.ToString());
                Debug.Log("Атлас поз: PASS; "+summary.ToString(Formatting.None));
            }
            catch(Exception error){Debug.LogException(error);File.WriteAllText(Path.Combine(PonyPointStudy.Output,"pose-atlas-failure.txt"),error.ToString());exit=1;}
            EditorApplication.Exit(exit);
        }

        public static JObject Export(out JObject summary)
        {
            PonyAnimationData.Load();var library=PonyAnimationData.Library;var layout=new PonySymbolLayout(library);
            var wear=StateJson.FromJson<WearableSourceBank>(GameAssets.Load<TextAsset>("Original/Pony/Wearables/pip").text);
            var reference=PonyAnimationArtwork.Prepare();var restWear=wear.clips.Single(c=>c.id=="idle").frames[0];
            var restTorso=Node(reference,"torso").transform.Matrix;
            var outerRest=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
            // Rigid followers (the PipBucks) have no wearable registration: their drawing is placed by the standing node
            // itself, which is exact only while the symbol stays the standing one (checked below, never silently).
            var followers=new Dictionary<string,(Matrix4x4 Standing,string Symbol)>(StringComparer.Ordinal);
            foreach(var node in reference.Frame.Nodes)
            {
                var instance=restWear.instances.SingleOrDefault(i=>i.basePath==node.top);
                if(instance!=null)outerRest.Add(node.socket,PonyRigPilotArt.Affine(instance.matrix));
                else followers.Add(node.socket,(node.transform.Matrix,layout.Get("idle",0,node.top).Id));
            }
            if(followers.Count>0)Debug.LogWarning("Атлас поз: без регистрации рисунка, размещаются по узлу стойки: "+string.Join(", ",followers.Keys));
            var document=PonyAnimationArtwork.CreateDocument();var joints=PonyPoseMath.Worlds(document,PonyPoseMath.Rest(document));
            Vector2 Joint(string id)
            {
                int index=Array.FindIndex(document.joints,j=>j.id==id);
                if(index<0)throw new InvalidOperationException("Атлас поз: нет сустава "+id);
                return PonyPoseMath.Position(joints[index]);
            }
            foreach(var chain in Chains)foreach(string socket in chain.Sockets)
                if(!outerRest.ContainsKey(socket) && !followers.ContainsKey(socket))throw new InvalidOperationException("Атлас поз: в рисунке нет гнезда "+socket);

            var canonical=new Dictionary<string,(Matrix4x4 Matrix,string Clip,int Frame)>(StringComparer.Ordinal);
            var registration=new Dictionary<string,float>(StringComparer.Ordinal);var skipped=new List<string>();
            var states=Chains.ToDictionary(c=>c.Id,_=>new List<JObject>(),StringComparer.Ordinal);
            var keys=Chains.ToDictionary(c=>c.Id,_=>new Dictionary<string,int>(StringComparer.Ordinal),StringComparer.Ordinal);
            var frames=new JArray();int raw=0;float restError=0;
            foreach(var clip in library.clips)
            {
                var ownWear=wear.clips.SingleOrDefault(c=>c.id==clip.id)??throw new InvalidOperationException("Атлас поз: нет регистрации клипа "+clip.id);
                for(int f=0;f<clip.frames.Length;f++)
                {
                    raw++;var art=PonyAnimationArtwork.Prepare(sourceClip:clip.id,sourceFrame:f);
                    var frameTorso=Node(art,"torso").transform.Matrix;var bodyInverse=restTorso*frameTorso.inverse;
                    var deltas=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);var symbols=new Dictionary<string,string>(StringComparer.Ordinal);
                    foreach(var node in art.Frame.Nodes)
                    {
                        var symbol=layout.Get(clip.id,f,node.top);
                        if(followers.TryGetValue(node.socket,out var follower))
                        {
                            if(symbol.Id!=follower.Symbol)skipped.Add($"{clip.id}/{f}/{node.socket}: ведомый рисунок {symbol.Id} вместо {follower.Symbol}");
                            else {symbols[node.socket]=symbol.Id;deltas[node.socket]=bodyInverse*node.transform.Matrix*follower.Standing.inverse;
                                canonical.TryAdd(node.socket+"|"+symbol.Id,(follower.Standing,"idle",0));}
                            continue;
                        }
                        var instance=ownWear.frames[f].instances.SingleOrDefault(i=>i.basePath==node.top);
                        if(instance==null){skipped.Add($"{clip.id}/{f}/{node.socket}: нет регистрации символа {symbol.Id}");continue;}
                        var value=outerRest[node.socket]*PonyRigPilotArt.Affine(instance.matrix).inverse*symbol.Matrix;
                        string key=node.socket+"|"+symbol.Id;
                        if(canonical.TryGetValue(key,out var saved))
                        {
                            float error=0;for(int i=0;i<16;i++)error=Mathf.Max(error,Mathf.Abs(saved.Matrix[i]-value[i]));
                            registration[node.socket]=Mathf.Max(registration.TryGetValue(node.socket,out var known)?known:0,error);
                            value=saved.Matrix;
                        }
                        else canonical.Add(key,(value,clip.id,f));
                        symbols[node.socket]=symbol.Id;deltas[node.socket]=bodyInverse*node.transform.Matrix*value.inverse;
                    }
                    if(clip.id=="idle" && f==0)foreach(var pair in deltas)for(int i=0;i<16;i++)restError=Mathf.Max(restError,Mathf.Abs(pair.Value[i]-Matrix4x4.identity[i]));
                    var indices=new JObject();
                    foreach(var chain in Chains)
                    {
                        if(chain.Sockets.Any(s=>!deltas.ContainsKey(s)))continue;
                        var effector=(Vector2)deltas[chain.End].MultiplyPoint3x4(Joint(chain.Effector)+chain.Offset);
                        string key=string.Join(";",chain.Sockets.Select(s=>symbols[s]+":"+string.Join(",",Six(deltas[s]).Select(v=>Math.Round(v,4).ToString("F4",CultureInfo.InvariantCulture)))));
                        if(!keys[chain.Id].TryGetValue(key,out int index))
                        {
                            index=states[chain.Id].Count;keys[chain.Id].Add(key,index);
                            states[chain.Id].Add(new JObject { ["id"]=clip.id+"/"+f,["sources"]=new JArray(),["effector"]=new JArray(effector.x,effector.y),
                                ["symbols"]=new JObject(chain.Sockets.Select(s=>new JProperty(s,symbols[s]))),
                                ["deltas"]=new JObject(chain.Sockets.Select(s=>new JProperty(s,PonyPointPreview.Matrix(deltas[s])))) });
                        }
                        ((JArray)states[chain.Id][index]["sources"]).Add(clip.id+"/"+f);indices[chain.Id]=index;
                    }
                    frames.Add(new JObject { ["id"]=clip.id+"/"+f,["states"]=indices,["torso"]=PonyPointPreview.Matrix(frameTorso*restTorso.inverse) });
                }
            }
            if(restError>.0001f)throw new InvalidOperationException("Атлас поз: стойка не совпала сама с собой: "+restError);
            var unstable=registration.Where(p=>p.Value>RegistrationTolerance).ToArray();
            if(unstable.Length>0)Debug.LogWarning("Атлас поз: нестабильная регистрация рисунков (берётся первый кадр символа): "+string.Join(", ",unstable.Select(p=>p.Key+"="+p.Value.ToString("G3"))));
            if(skipped.Count>0)Debug.LogWarning("Атлас поз: пропущены части без регистрации, "+skipped.Count+": "+string.Join("; ",skipped.Take(20)));

            using var assets=new PonyPointPreviewAssets();
            var restLooks=new JArray(PonyPointStudy.Outfits.Select(o=>assets.Look(PonyAnimationArtwork.Prepare(o))));
            var used=new HashSet<string>(Chains.SelectMany(c=>states[c.Id]).SelectMany(s=>((JObject)s["symbols"]).Properties().Select(p=>p.Name+"|"+(string)p.Value)),StringComparer.Ordinal);
            var symbolLooks=new JObject();
            foreach(string key in used.OrderBy(k=>k,StringComparer.Ordinal))
            {
                var (matrix,clip,frame)=canonical[key];string socket=key.Substring(0,key.IndexOf('|'));var looks=new JArray();
                foreach(string outfit in PonyPointStudy.Outfits)
                {
                    var art=PonyAnimationArtwork.ReplaceNodes(PonyAnimationArtwork.Prepare(outfit),PonyAnimationArtwork.Prepare(outfit,clip,frame),
                        new Dictionary<string,Matrix4x4>(StringComparer.Ordinal){[socket]=matrix});
                    var look=assets.Look(art);var nodes=(JArray)look["nodes"];
                    looks.Add(new JArray(((JArray)look["draws"]).Where(d=>(string)nodes[(int)d["node"]]["socket"]==socket).Select(d=>new JObject {["image"]=d["image"],["rect"]=d["rect"]})));
                }
                symbolLooks[key]=new JObject { ["canonical"]=PonyPointPreview.Matrix(matrix),["looks"]=looks };
            }
            summary=new JObject { ["rawFrames"]=raw,["symbols"]=symbolLooks.Count,["images"]=assets.Images.Count,["skippedParts"]=skipped.Count,
                ["registration"]=new JObject(registration.OrderBy(p=>p.Key).Select(p=>new JProperty(p.Key,p.Value))),
                ["states"]=new JObject(Chains.Select(c=>new JProperty(c.Id,states[c.Id].Count))) };
            return new JObject { ["version"]=1,["sourceHash"]=PonyRenderAsset.SourceHash(),
                ["world"]=new JArray(PonyPointStudy.World.x,PonyPointStudy.World.y,PonyPointStudy.World.width,PonyPointStudy.World.height),
                ["images"]=assets.Images,["rest"]=restLooks,["symbols"]=symbolLooks,["frames"]=frames,["skipped"]=new JArray(skipped),
                ["restSymbols"]=new JObject(reference.Frame.Nodes.Select(n=>new JProperty(n.socket,layout.Get("idle",0,n.top).Id))),
                ["chains"]=new JArray(Chains.Select(c=>new JObject { ["id"]=c.Id,["label"]=c.Label,["sockets"]=new JArray(c.Sockets),
                    ["rest"]=new JArray(Joint(c.Effector).x+c.Offset.x,Joint(c.Effector).y+c.Offset.y),["states"]=new JArray(states[c.Id]) })) };
        }
        private static PonyRigNode Node(PonyAnimationArtwork art,string socket)=>art.Frame.Nodes.SingleOrDefault(n=>n.socket==socket)??throw new InvalidOperationException("Атлас поз: нет узла "+socket);
        private static float[] Six(Matrix4x4 m)=>new[]{m.m00,m.m10,m.m01,m.m11,m.m03,m.m13};
    }
}
