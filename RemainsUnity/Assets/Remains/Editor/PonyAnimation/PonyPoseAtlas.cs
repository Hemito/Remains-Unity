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
            var wear=StateJson.FromJson<WearableSourceBank>(GameAssets.Load<TextAsset>("Original/Pony/Wearables/pip").text);PonyAuthoredClips.Extend(wear,"pip");
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
            void Record(string id,Dictionary<string,Matrix4x4> deltas,Dictionary<string,string> symbols,Matrix4x4 frameTorso)
            {
                var indices=new JObject();
                foreach(var chain in Chains)
                {
                    if(chain.Sockets.Any(s=>!deltas.ContainsKey(s)))continue;
                    var effector=(Vector2)deltas[chain.End].MultiplyPoint3x4(Joint(chain.Effector)+chain.Offset);
                    string key=string.Join(";",chain.Sockets.Select(s=>symbols[s]+":"+string.Join(",",Six(deltas[s]).Select(v=>Math.Round(v,4).ToString("F4",CultureInfo.InvariantCulture)))));
                    if(!keys[chain.Id].TryGetValue(key,out int index))
                    {
                        index=states[chain.Id].Count;keys[chain.Id].Add(key,index);
                        states[chain.Id].Add(new JObject { ["id"]=id,["sources"]=new JArray(),["effector"]=new JArray(effector.x,effector.y),
                            ["symbols"]=new JObject(chain.Sockets.Select(s=>new JProperty(s,symbols[s]))),
                            ["deltas"]=new JObject(chain.Sockets.Select(s=>new JProperty(s,PonyPointPreview.Matrix(deltas[s])))) });
                    }
                    ((JArray)states[chain.Id][index]["sources"]).Add(id);indices[chain.Id]=index;
                }
                frames.Add(new JObject { ["id"]=id,["states"]=indices,["torso"]=PonyPointPreview.Matrix(frameTorso*restTorso.inverse) });
            }
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
                            // Another drawing of an unregistered follower (the running tail and mane) is registered at its
                            // first frame, where it sits relative to the torso; it can then be chosen by hand in any pose.
                            string drawn=node.socket+"|"+symbol.Id;
                            if(!canonical.ContainsKey(drawn))canonical.Add(drawn,(symbol.Id==follower.Symbol?follower.Standing:bodyInverse*node.transform.Matrix,clip.id,f));
                            symbols[node.socket]=symbol.Id;deltas[node.socket]=bodyInverse*node.transform.Matrix*canonical[drawn].Matrix.inverse;
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
                    Record(clip.id+"/"+f,deltas,symbols,frameTorso);
                }
            }
            var own=new Dictionary<string,OwnSymbol>(StringComparer.Ordinal);int extraFrames=AddExtra(library,canonical,followers,restTorso,skipped,own,Record);
            if(restError>.0001f)throw new InvalidOperationException("Атлас поз: стойка не совпала сама с собой: "+restError);
            var unstable=registration.Where(p=>p.Value>RegistrationTolerance).ToArray();
            if(unstable.Length>0)Debug.LogWarning("Атлас поз: нестабильная регистрация рисунков (берётся первый кадр символа): "+string.Join(", ",unstable.Select(p=>p.Key+"="+p.Value.ToString("G3"))));
            if(skipped.Count>0)Debug.LogWarning("Атлас поз: пропущены части без регистрации, "+skipped.Count+": "+string.Join("; ",skipped.Take(20)));

            using var assets=new PonyPointPreviewAssets();
            var restLooks=new JArray(PonyPointStudy.Outfits.Select(o=>assets.Look(PonyAnimationArtwork.Prepare(o))));
            var used=new HashSet<string>(Chains.SelectMany(c=>states[c.Id]).SelectMany(s=>((JObject)s["symbols"]).Properties().Select(p=>p.Name+"|"+(string)p.Value)),StringComparer.Ordinal);
            var symbolLooks=new JObject();
            var ownImages=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(string key in used.OrderBy(k=>k,StringComparer.Ordinal))
            {
                if(own.TryGetValue(key,out var drawn))
                {
                    symbolLooks[key]=new JObject { ["canonical"]=PonyPointPreview.Matrix(drawn.Canonical),["own"]=true,
                        ["looks"]=new JArray(PonyPointStudy.Outfits.Select(o=>OwnLook(drawn,PonyAnimationArtwork.Prepare(o).Appearance,assets,ownImages))) };
                    continue;
                }
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
            summary=new JObject { ["rawFrames"]=raw,["extraFrames"]=extraFrames,["ownSymbols"]=own.Count,["symbols"]=symbolLooks.Count,["images"]=assets.Images.Count,["skippedParts"]=skipped.Count,
                ["registration"]=new JObject(registration.OrderBy(p=>p.Key).Select(p=>new JProperty(p.Key,p.Value))),
                ["states"]=new JObject(Chains.Select(c=>new JProperty(c.Id,states[c.Id].Count))) };
            return new JObject { ["version"]=1,["sourceHash"]=PonyRenderAsset.SourceHash(),
                ["world"]=new JArray(PonyPointStudy.World.x,PonyPointStudy.World.y,PonyPointStudy.World.width,PonyPointStudy.World.height),
                ["images"]=assets.Images,["rest"]=restLooks,["symbols"]=symbolLooks,["frames"]=frames,["skipped"]=new JArray(skipped),
                ["restSymbols"]=new JObject(reference.Frame.Nodes.Select(n=>new JProperty(n.socket,layout.Get("idle",0,n.top).Id))),
                // Registration origins of the pose document (graphic joints, not recovered anatomy), standing actor space.
                ["joints"]=new JObject(reference.Frame.Nodes.Select(n=>new JProperty(n.socket,new JArray(Joint(n.socket).x,Joint(n.socket).y)))),
                ["chains"]=new JArray(Chains.Select(c=>new JObject { ["id"]=c.Id,["label"]=c.Label,["sockets"]=new JArray(c.Sockets),["end"]=c.End,
                    ["rest"]=new JArray(Joint(c.Effector).x+c.Offset.x,Joint(c.Effector).y+c.Offset.y),["states"]=new JArray(states[c.Id]) })) };
        }
        // Original body timelines the game bank does not take (strikes, stealth, levitation...), exported by
        // Tools/export_pony.py --extra-poses. A node whose drawing the game bank has is placed like PonySymbolLayout
        // (its first part relative to the symbol's first occurrence in the bank) and shares the game's registration.
        // A node composed differently from the bank (sitjump keeps the croup and its mark on separate layers) becomes an
        // own symbol: registered at its first frame, drawn from the game's base shape PNGs with the slot tint. Shapes the
        // bank never draws are dropped first: sitjump, which the original code never plays, steps its near croup and a
        // neck overlay through the armour drawings frame by frame.
        public const string ExtraLibrary="../.reference/original/pony-extra/pony-extra.json";
        // sitjump keeps the near croup, its mark and two hip overlays on separate layers, and a neck overlay; layer → socket.
        private static readonly Dictionary<string,Dictionary<string,string>> LayerSockets=new(StringComparer.Ordinal)
        {
            ["x_sitjump"]=new(StringComparer.Ordinal)
            {
                ["1"]="hindLowerFar",["5"]="hindUpperFar",["sleg1"]="hindUpperFar",["11"]="frontUpperFar",["15"]="frontMiddleFar",["flleg3"]="frontLowerFar",["pip2"]="pipbuckFar",
                ["tail"]="tail",["29"]="torso",["33"]="neck",["35"]="neck",["mane"]="mane",["head"]="head",
                ["42"]="frontUpperNear",["46"]="frontMiddleNear",["frleg3"]="frontLowerNear",["54"]="hindLowerNear",
                ["58"]="hindUpperNear",["mark"]="hindUpperNear",["61"]="hindUpperNear",["62"]="hindUpperNear",["pip1"]="pipbuckNear"
            }
        };
        // The swipe trail of the strikes is an effect layer, not a body part.
        private static readonly Dictionary<string,string[]> EffectLayers=new(StringComparer.Ordinal) {["x_punch"]=new[]{"72"},["x_kick"]=new[]{"72"}};
        public sealed class OwnSymbol {public string Socket,Name;public Matrix4x4 Canonical;public readonly List<(PonyPart Part,Matrix4x4 Local)> Parts=new();}

        private static int AddExtra(PonyLibrary game,Dictionary<string,(Matrix4x4 Matrix,string Clip,int Frame)> canonical,
            Dictionary<string,(Matrix4x4 Standing,string Symbol)> followers,Matrix4x4 restTorso,List<string> skipped,Dictionary<string,OwnSymbol> own,
            Action<string,Dictionary<string,Matrix4x4>,Dictionary<string,string>,Matrix4x4> record)
        {
            string path=Path.GetFullPath(ExtraLibrary);
            if(!File.Exists(path)){Debug.LogWarning("Атлас поз: нет дополнительных клипов оригинала "+path+"; атлас только по игровому банку.");return 0;}
            var extra=JsonUtility.FromJson<PonyLibrary>(File.ReadAllText(path));
            if(extra?.clips==null || extra.clips.Length==0)throw new InvalidOperationException("Атлас поз: пустой файл дополнительных клипов "+path);
            var anchors=new Dictionary<string,(Matrix4x4 Matrix,string Signature)>(StringComparer.Ordinal);
            foreach(var clip in game.clips)foreach(var frame in clip.frames)foreach(var group in PonyRigPilotArt.Groups(frame))
            {
                var (id,signature,first)=Describe(frame,group);
                anchors.TryAdd(id,(PonyRigPilotArt.Affine(first.matrix),signature));
            }
            var gameShapes=new HashSet<string>(game.clips.SelectMany(c=>c.frames).SelectMany(f=>f.parts).Select(p=>p.shape),StringComparer.Ordinal);
            var idle=game.clips.Single(c=>c.id=="idle").frames[0];
            var mainShape=PonyRigPilotArt.Groups(idle).ToDictionary(g=>g.Socket,g=>Describe(idle,g).First.shape,StringComparer.Ordinal);
            int count=0;var unknown=new List<string>();var bySignature=new Dictionary<string,OwnSymbol>(StringComparer.Ordinal);
            foreach(var clip in extra.clips)for(int f=0;f<clip.frames.Length;f++)
            {
                string frameId=clip.id+"/"+f;var frame=clip.frames[f];PonyRigPilotArt.Group[] groups;
                if(EffectLayers.TryGetValue(clip.id,out var effects) && frame.parts.Any(p=>effects.Contains(p.path.Split('/')[0])))
                {
                    unknown.Add(frameId+": слой эффекта "+string.Join(",",effects)+" не входит в тело");
                    frame=new PonyFrame {parts=frame.parts.Where(p=>!effects.Contains(p.path.Split('/')[0])).ToArray()};
                }
                var foreign=frame.parts.Where(p=>!gameShapes.Contains(p.shape)).Select(p=>p.shape).Distinct().ToArray();
                if(foreign.Length>0)
                {
                    unknown.Add(frameId+": убраны рисунки, которых нет в игровом банке (броня): "+string.Join(",",foreign));
                    frame=new PonyFrame {parts=frame.parts.Where(p=>gameShapes.Contains(p.shape)).ToArray()};
                }
                if(LayerSockets.TryGetValue(clip.id,out var table))
                {
                    var missing=frame.parts.Select(p=>p.path.Split('/')[0]).Where(t=>!table.ContainsKey(t)).Distinct().ToArray();
                    if(missing.Length>0)throw new InvalidOperationException($"Атлас поз: слои {string.Join(",",missing)} в {frameId} не сопоставлены частям тела.");
                    groups=frame.parts.Select((p,i)=>(Socket:table[p.path.Split('/')[0]],Index:i)).GroupBy(p=>p.Socket)
                        .Select(g=>new PonyRigPilotArt.Group {Socket=g.Key,Top=frame.parts[g.First().Index].path.Split('/')[0],Indices=g.Select(p=>p.Index).ToArray()}).ToArray();
                }
                else
                {
                    try{groups=PonyRigPilotArt.Groups(frame).ToArray();}
                    catch(InvalidDataException error){unknown.Add(frameId+": "+error.Message);continue;}
                }
                var nodes=new Dictionary<string,(string Symbol,Matrix4x4 Matrix,Matrix4x4? Standing)>(StringComparer.Ordinal);
                var fresh=new List<(PonyRigPilotArt.Group Group,string Id,string Signature)>();
                foreach(var group in groups)
                {
                    var (id,signature,first)=Describe(frame,group);
                    if(anchors.TryGetValue(id,out var anchor) && anchor.Signature==signature)
                        nodes[group.Socket]=(id,PonyRigPilotArt.Affine(first.matrix)*anchor.Matrix.inverse,null);
                    else fresh.Add((group,id,signature));
                }
                if(!nodes.TryGetValue("torso",out var torso)){unknown.Add(frameId+": нет туловища из игрового банка");continue;}
                var bodyInverse=restTorso*torso.Matrix.inverse;
                foreach(var (group,id,signature) in fresh)
                {
                    if(!mainShape.TryGetValue(group.Socket,out var main) || group.Indices.All(i=>frame.parts[i].shape!=main))
                    {unknown.Add($"{frameId}/{group.Socket}: нет основного рисунка {main}, часть пропущена");continue;}
                    var first=Describe(frame,group).First;var placement=bodyInverse*PonyRigPilotArt.Affine(first.matrix);
                    if(!bySignature.TryGetValue(signature,out var symbol))
                    {
                        symbol=new OwnSymbol {Socket=group.Socket,Name=id+"~"+(bySignature.Count+1),Canonical=placement};
                        var origin=PonyRigPilotArt.Affine(first.matrix).inverse;
                        foreach(var part in group.Indices.Select(i=>frame.parts[i]).Where(p=>PonyAppearance.IsVisible(p)).OrderBy(p=>p.drawOrder))
                            symbol.Parts.Add((part,origin*PonyRigPilotArt.Affine(part.matrix)));
                        bySignature.Add(signature,symbol);own.Add(symbol.Socket+"|"+symbol.Name,symbol);
                    }
                    // The node is the first part's placement; the symbol's standing registration is its first frame.
                    nodes[group.Socket]=(symbol.Name,bodyInverse.inverse*placement,symbol.Canonical);
                }
                var deltas=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);var symbols=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var pair in nodes)
                {
                    Matrix4x4 standing;
                    if(pair.Value.Standing is Matrix4x4 owned)standing=owned;
                    else if(followers.TryGetValue(pair.Key,out var follower))
                    {
                        if(pair.Value.Symbol!=follower.Symbol){skipped.Add($"{frameId}/{pair.Key}: ведомый рисунок {pair.Value.Symbol} вместо {follower.Symbol}");continue;}
                        standing=follower.Standing;
                    }
                    else if(canonical.TryGetValue(pair.Key+"|"+pair.Value.Symbol,out var known))standing=known.Matrix;
                    else {skipped.Add($"{frameId}/{pair.Key}: у рисунка {pair.Value.Symbol} нет регистрации");continue;}
                    symbols[pair.Key]=pair.Value.Symbol;deltas[pair.Key]=bodyInverse*pair.Value.Matrix*standing.inverse;
                }
                record(frameId,deltas,symbols,torso.Matrix);count++;
            }
            if(unknown.Count>0)Debug.LogWarning("Атлас поз: в дополнительных клипах пропущены слои или кадры, "+unknown.Count+": "+string.Join("; ",unknown));
            if(own.Count>0)Debug.LogWarning("Атлас поз: рисунков, которых нет в игровом банке, "+own.Count+" (рисуются из базовых PNG игры, в костюме — без костюма): "+string.Join(", ",own.Keys));
            skipped.AddRange(unknown);
            return count;
        }
        // An own symbol's look: the game's base shape PNGs with the slot tint.
        private static JArray OwnLook(OwnSymbol symbol,PonyAppearanceKey appearance,PonyPointPreviewAssets assets,Dictionary<string,int> images)
        {
            var draws=new JArray();
            foreach(var (part,local) in symbol.Parts)
            {
                string file=Path.GetFullPath("Assets/Remains/Resources/Original/Pony/"+part.shape+".png");
                if(!File.Exists(file)){Debug.LogWarning($"Атлас поз: нет PNG фигуры {part.shape} для рисунка {symbol.Socket}|{symbol.Name}; часть не показана.");continue;}
                if(!images.TryGetValue(file,out int image)){image=assets.Images.Count;images.Add(file,image);assets.Images.Add("data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(file)));}
                var tint=PonyAppearance.Tint(part.slot,appearance);var b=part.bounds;
                draws.Add(new JObject {["image"]=image,["rect"]=new JArray(b[0]/1000f,-b[3]/1000f,(b[2]-b[0])/1000f,(b[3]-b[1])/1000f),["local"]=PonyPointPreview.Matrix(local),
                    ["tint"]=new JArray(tint.r*part.color[0],tint.g*part.color[1],tint.b*part.color[2],part.color[3])});
            }
            return draws;
        }
        private static (string Id,string Signature,PonyPart First) Describe(PonyFrame frame,PonyRigPilotArt.Group group)
        {
            var parts=group.Indices.Select(i=>frame.parts[i]).OrderBy(p=>PonySymbolLayout.LocalPath(p.path),StringComparer.Ordinal)
                .ThenBy(p=>p.shape,StringComparer.Ordinal).ThenBy(p=>p.hairStyle).ThenBy(p=>p.eyeStyle).ToArray();
            string id=group.Socket+":"+string.Join(",",parts.Select(p=>p.shape).Distinct(StringComparer.Ordinal).OrderBy(v=>int.Parse(v)));
            return (id,string.Join("|",parts.Select(p=>PonySymbolLayout.LocalPath(p.path)+":"+p.shape+":"+p.hairStyle+":"+p.eyeStyle)),parts[0]);
        }
        private static PonyRigNode Node(PonyAnimationArtwork art,string socket)=>art.Frame.Nodes.SingleOrDefault(n=>n.socket==socket)??throw new InvalidOperationException("Атлас поз: нет узла "+socket);
        private static float[] Six(Matrix4x4 m)=>new[]{m.m00,m.m10,m.m01,m.m11,m.m03,m.m13};
    }
}
