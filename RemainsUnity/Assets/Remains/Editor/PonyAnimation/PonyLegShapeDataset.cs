using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public sealed class PonyLegShapeDataset
    {
        public sealed class Variant
        {
            public string Id,Clip;public int Frame;
            public Dictionary<string,Matrix4x4> Canonical=new(StringComparer.Ordinal);
        }
        public sealed class Row
        {
            public string Clip;public int Frame;
            public PonyLegShapeModel.Sample Sample;public Vector2[][] Points;
        }
        public readonly PonyLegShapeModel Model=new();
        public readonly Dictionary<string,Variant> Variants=new(StringComparer.Ordinal);
        public readonly List<Row> Rows=new();
        public readonly List<string> Skipped=new();
        public int RawFrames;public float MaximumRegistrationError,MaximumDecodeError;
        public string DefaultVariant;
        public PonyLegShapeDataset()
        {
            PonyAnimationData.Load();var library=PonyAnimationData.Library;var layout=new PonySymbolLayout(library);
            var wear=StateJson.FromJson<WearableSourceBank>(GameAssets.Load<TextAsset>("Original/Pony/Wearables/pip").text);PonyAuthoredBuildPlans.Extend(wear,"pip");
            var reference=PonyAnimationArtwork.Prepare();var restWear=wear.clips.Single(c=>c.id=="idle").frames[0];
            var outerRest=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
            foreach(var part in PonyPointControls.Parts)
            {
                var node=reference.Frame.Nodes.Single(n=>n.socket==part.Socket);
                outerRest.Add(part.Socket,PonyRigPilotArt.Affine(restWear.instances.Single(i=>i.basePath==node.top).matrix));
            }
            var restTorso=reference.Frame.Nodes.Single(n=>n.socket=="torso").transform.Matrix;
            var canonicalBySymbol=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);var unique=new Dictionary<string,PonyLegShapeModel.Sample>(StringComparer.Ordinal);
            foreach(var clip in library.clips)
            {
                var ownWear=wear.clips.Single(c=>c.id==clip.id);
                for(int f=0;f<clip.frames.Length;f++)
                {
                    RawFrames++;var art=PonyAnimationArtwork.Prepare(sourceClip:clip.id,sourceFrame:f);
                    var bodyInverse=restTorso*art.Frame.Nodes.Single(n=>n.socket=="torso").transform.Matrix.inverse;
                    var points=new Vector2[2][];var symbols=new string[2];var canonical=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
                    for(int p=0;p<2;p++)
                    {
                        string socket=PonyPointControls.Parts[p].Socket;var node=art.Frame.Nodes.Single(n=>n.socket==socket);
                        var symbol=layout.Get(clip.id,f,node.top);symbols[p]=symbol.Id;
                        var instance=ownWear.frames[f].instances.SingleOrDefault(i=>i.basePath==node.top)??throw new InvalidOperationException($"Нет регистрации символа ноги: {clip.id}/{f}/{socket}.");
                        var value=outerRest[socket]*PonyRigPilotArt.Affine(instance.matrix).inverse*symbol.Matrix;
                        if(canonicalBySymbol.TryGetValue(symbol.Id,out var saved))
                        {
                            for(int i=0;i<16;i++)MaximumRegistrationError=Mathf.Max(MaximumRegistrationError,Mathf.Abs(saved[i]-value[i]));
                            value=saved;
                        }
                        else canonicalBySymbol.Add(symbol.Id,value);
                        canonical.Add(socket,value);
                        points[p]=PonyAffinePoints.Map(bodyInverse*node.transform.Matrix*value.inverse,PonyPointControls.Parts[p].Rest);
                    }
                    if(MaximumRegistrationError>.00005f)throw new InvalidOperationException("Варианты ноги не имеют стабильной регистрации: "+MaximumRegistrationError);
                    string variant=string.Join("|",symbols);
                    if(!Variants.ContainsKey(variant))Variants.Add(variant,new Variant {Id=variant,Clip=clip.id,Frame=f,Canonical=canonical});
                    if(clip.id=="idle" && f==0)DefaultVariant=variant;
                    float[] features,shape;
                    try {var target=PonyLegShapeModel.Driver(points);features=PonyLegShapeModel.Features(target);shape=PonyLegShapeModel.Encode(points,target);var restored=PonyLegShapeModel.Decode(shape,target);
                        for(int p=0;p<2;p++)for(int i=0;i<3;i++)MaximumDecodeError=Mathf.Max(MaximumDecodeError,Vector2.Distance(restored[p][i],points[p][i]));}
                    catch(InvalidOperationException error){Skipped.Add(clip.id+"/"+f+": "+error.Message);continue;}
                    string key=variant+"/"+string.Join(",",features.Concat(shape).Select(v=>Math.Round(v,4).ToString("F4",CultureInfo.InvariantCulture)));
                    if(!unique.TryGetValue(key,out var sample))
                    {
                        sample=new PonyLegShapeModel.Sample {Id=clip.id+"/"+f,Variant=variant,Features=features,Shape=shape,Mirrored=shape[7]<=0 || shape[9]<=0};
                        unique.Add(key,sample);Model.Samples.Add(sample);
                    }
                    sample.Sources.Add((clip.id+"/"+f,Family(clip.id)));Rows.Add(new Row {Clip=clip.id,Frame=f,Sample=sample,Points=points});
                }
            }
            if(Skipped.Count>0)Debug.LogWarning("Примеры формы ноги: пропущены вырожденные конфигурации, "+Skipped.Count+"; "+string.Join("; ",Skipped));
            if(MaximumDecodeError>.00002f)throw new InvalidOperationException("Потеряна форма исходного образца: "+MaximumDecodeError);
        }
        public PonyAnimationArtwork Artwork(string variant,string outfit)
        {
            if(!Variants.TryGetValue(variant,out var value))throw new InvalidOperationException("Нет рисунка варианта ноги "+variant);
            return PonyAnimationArtwork.ReplaceNodes(PonyAnimationArtwork.Prepare(outfit),PonyAnimationArtwork.Prepare(outfit,value.Clip,value.Frame),value.Canonical);
        }
        public JObject Export(PonyPointPreviewAssets assets)
        {
            var variants=new JObject();foreach(var pair in Variants)
                variants[pair.Key]=new JObject { ["looks"]=new JArray(PonyPointStudy.Outfits.Select(outfit=>assets.Look(Artwork(pair.Key,outfit)))) };
            return new JObject { ["version"]=1,["sourceHash"]=PonyRenderAsset.SourceHash(),["defaultVariant"]=DefaultVariant,["rawFrames"]=RawFrames,["skipped"]=new JArray(Skipped),["variants"]=variants,
                ["weights"]=new JArray(PonyLegShapeModel.FeatureWeights),["neighbours"]=PonyLegShapeModel.Neighbours,
                ["samples"]=new JArray(Model.Samples.Select(s=>new JObject { ["id"]=s.Id,["variant"]=s.Variant,["features"]=new JArray(s.Features),["shape"]=new JArray(s.Shape),["mirrored"]=s.Mirrored,
                    ["sources"]=new JArray(s.Sources.Select(p=>new JObject { ["id"]=p.Id,["family"]=p.Family })) })) };
        }
        public JArray ReferencePoses(PonyPointPreviewAssets assets)
        {
            var result=new JArray();var rest=PonyAnimationArtwork.Prepare().Frame.Nodes.Single(n=>n.socket=="torso").transform.Matrix;
            foreach(var row in Rows.Where(r=>r.Clip=="crouchLower" || r.Clip=="crouchRise" || r.Clip=="idle" && r.Frame==0))
            {
                var looks=new JArray();
                foreach(string outfit in PonyPointStudy.Outfits)
                {
                    var art=PonyAnimationArtwork.Prepare(outfit,row.Clip,row.Frame);var frame=rest*art.Frame.Nodes.Single(n=>n.socket=="torso").transform.Matrix.inverse;
                    var look=assets.Look(art);
                    for(int n=0;n<art.Frame.Nodes.Length;n++)look["nodes"][n]["matrix"]=PonyPointPreview.Matrix(frame*art.Frame.Nodes[n].transform.Matrix);
                    looks.Add(look);
                }
                result.Add(new JObject { ["id"]=row.Clip+"/"+row.Frame,["label"]=(row.Clip=="idle"?"Стойка":row.Clip=="crouchLower"?"Лечь":"Встать")+" · "+(row.Frame+1),
                    ["points"]=PonyPointPreview.Points(row.Points),["looks"]=looks });
            }
            return result;
        }
        public static string Family(string id)=>id=="idle" || id.StartsWith("crouch",StringComparison.Ordinal)?"rest":id.Contains("walk") || id.Contains("run")?"gait":"other";
    }
}
