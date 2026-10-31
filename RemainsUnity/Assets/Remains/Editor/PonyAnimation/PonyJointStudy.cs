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
    public static class PonyJointStudy
    {
        public sealed class Connection
        {
            public string Parent,Child,Label;
            public PonyJointFit.Result Shared,Independent;
            public PonyJointFit.ErrorSummary SharedHoldout,IndependentHoldout;
            public string Classification;
        }
        public static readonly string[] Training={"body_stay","body_trot","body_sit_down","body_sit"};
        public static string Output=>Path.GetFullPath("../.reference/cutout-joints-v1");
        public static void RunBatch()
        {
            int code=0;Directory.CreateDirectory(Output);
            try
            {
                CheckMath();var rig=new GodotRigPilotData(Path.GetFullPath("../.reference/rig-pilot/source"),includeRise:true);
                var connections=Analyze(rig);var report=Report(rig,connections);File.WriteAllText(Path.Combine(Output,"joint-report.json"),report.ToString());
                if(!Environment.GetCommandLineArgs().Contains("-jointReportOnly"))PonyJointStudyPreview.Write(rig,connections,Output);
                Debug.Log("Точки крепления: расчёт завершён; "+string.Join(", ",connections.GroupBy(c=>c.Classification).Select(g=>g.Key+"="+g.Count()))+"; "+Output);
            }
            catch(Exception problem){Debug.LogException(problem);File.WriteAllText(Path.Combine(Output,"failure.txt"),problem.ToString());code=1;}
            EditorApplication.Exit(code);
        }
        private static Connection[] Analyze(GodotRigPilotData rig)
        {
            var doc=PonyAnimationArtwork.CreateDocument();
            try
            {
                var output=new List<Connection>();
                foreach(var child in doc.joints.Where(j=>j.parent>=0 && !string.IsNullOrEmpty(j.socket)))
                {
                    string parent=doc.joints[child.parent].socket;
                    if(parent==null)throw new InvalidDataException("У гнезда нет графического родителя: "+child.id);
                    var connection=new Connection {Parent=parent,Child=child.socket,Label=doc.joints[child.parent].label+" → "+child.label};
                    var train=Samples(rig,parent,child.socket,true).ToArray();var holdout=Samples(rig,parent,child.socket,false).ToArray();
                    connection.Shared=PonyJointFit.FitSharedRest(train,Matrix4x4.identity,Matrix4x4.identity);
                    connection.Independent=PonyJointFit.FitIndependent(train);
                    connection.SharedHoldout=PonyJointFit.Evaluate(connection.Shared,holdout);
                    connection.IndependentHoldout=PonyJointFit.Evaluate(connection.Independent,holdout);
                    connection.Classification=Classify(connection.Shared,connection.SharedHoldout,rig.RootScale/50);
                    if(MaxRelativeDifference(train.Concat(holdout))<.000005f)connection.Classification="rigid-follow-no-unique-pivot";
                    output.Add(connection);
                }
                return output.ToArray();
            }
            finally {UnityEngine.Object.DestroyImmediate(doc);}
        }
        private static IEnumerable<PonyJointFit.Sample> Samples(GodotRigPilotData rig,string parent,string child,bool training,string variant=null)
        {
            int p=rig.Indices[GodotRigPilotData.SocketBones[parent]],c=rig.Indices[GodotRigPilotData.SocketBones[child]];
            foreach(string id in GodotRigPilotData.SourceIds.Concat(new[]{"body_sit_up"}))
            {
                if(Training.Contains(id)!=training)continue;var clip=rig.SourceClips[id];
                for(int f=0;f<clip.Frames.Length;f++)
                {
                    var frame=clip.Frames[f];string key=frame.Libraries[p]+"/"+frame.Libraries[c];
                    if(variant!=null && key!=variant)continue;
                    yield return new PonyJointFit.Sample(frame.Delta[p],frame.Delta[c],id,f);
                }
            }
        }
        private static string Classify(PonyJointFit.Result fit,PonyJointFit.ErrorSummary holdout,float sourcePixel)
        {
            if(!fit.Identifiable)return "not-identifiable";
            double rms=Math.Max(fit.Rms,holdout.Rms)/sourcePixel,max=Math.Max(fit.Max,holdout.Max)/sourcePixel;
            if(rms<=.05 && max<=.15)return "stable-within-source-rounding";
            if(rms<=.2 && max<=.5)return "approximate-pivot";
            return "variable-attachment";
        }
        private static float MaxRelativeDifference(IEnumerable<PonyJointFit.Sample> samples)
        {
            float error=0;foreach(var sample in samples)for(int i=0;i<16;i++)error=Mathf.Max(error,Mathf.Abs(sample.Parent[i]-sample.Child[i]));return error;
        }
        private static JObject Report(GodotRigPilotData rig,Connection[] connections)
        {
            float pixel=rig.RootScale/50;var entries=new JArray();
            foreach(var c in connections)
            {
                var variants=new JArray();var perClip=new JArray();int p=rig.Indices[GodotRigPilotData.SocketBones[c.Parent]],child=rig.Indices[GodotRigPilotData.SocketBones[c.Child]];
                foreach(var source in rig.SourceClips.Values)
                {
                    var samples=source.Frames.Select((f,i)=>new PonyJointFit.Sample(f.Delta[p],f.Delta[child],source.Id,i)).ToArray();
                    if(samples.Length<4){perClip.Add(new JObject { ["clip"]=source.Id,["classification"]="insufficient-motion",["samples"]=samples.Length });continue;}
                    var train=samples.Where((s,i)=>i%2==0).ToArray();var heldout=samples.Where((s,i)=>i%2!=0).ToArray();
                    var local=PonyJointFit.FitSharedRest(train,Matrix4x4.identity,Matrix4x4.identity);var errors=PonyJointFit.Evaluate(local,heldout);
                    perClip.Add(new JObject { ["clip"]=source.Id,["classification"]=MaxRelativeDifference(samples)<.000005f?"rigid-follow-no-unique-pivot":Classify(local,errors,pixel),
                        ["fit"]=FitJson(local,pixel),["holdout"]=ErrorsJson(errors,pixel) });
                }
                var variantKeys=GodotRigPilotData.SourceIds.Concat(new[]{"body_sit_up"}).SelectMany(id=>rig.SourceClips[id].Frames.Select(f=>f.Libraries[p]+"/"+f.Libraries[child])).Distinct(StringComparer.Ordinal).ToArray();
                foreach(string key in variantKeys)
                {
                    var train=Samples(rig,c.Parent,c.Child,true,key).ToArray();var holdout=Samples(rig,c.Parent,c.Child,false,key).ToArray();
                    if(train.Length<2){variants.Add(new JObject { ["libraries"]=key,["classification"]="insufficient-training",["trainingSamples"]=train.Length,["holdoutSamples"]=holdout.Length});continue;}
                    var fit=PonyJointFit.FitSharedRest(train,Matrix4x4.identity,Matrix4x4.identity);
                    variants.Add(new JObject { ["libraries"]=key,["fit"]=FitJson(fit,pixel),["holdout"]=holdout.Length>0?ErrorsJson(PonyJointFit.Evaluate(fit,holdout),pixel):null });
                }
                entries.Add(new JObject { ["parent"]=c.Parent,["child"]=c.Child,["label"]=c.Label,["classification"]=c.Classification,
                    ["sharedRestPointFit"]=FitJson(c.Shared,pixel),["sharedHoldout"]=ErrorsJson(c.SharedHoldout,pixel),
                    ["independentAnchorsFit"]=FitJson(c.Independent,pixel),["independentHoldout"]=ErrorsJson(c.IndependentHoldout,pixel),["libraryVariantFits"]=variants,["perClipEvenOddFits"]=perClip});
            }
            return new JObject { ["schema"]=1,["sourceCommit"]=GodotRigPilotData.SourceCommit,["coordinateSpace"]="reference actor metres; delta matrices relative to body_stay",
                ["sourcePixelInWorldUnits"]=pixel,["trainingClips"]=new JArray(Training),["holdoutClips"]=new JArray(GodotRigPilotData.SourceIds.Concat(new[]{"body_sit_up"}).Except(Training)),
                ["tightThreshold"]=new JObject { ["rmsSourcePixels"]=.05,["maxSourcePixels"]=.15 },
                ["meaning"]="Residual/rank test of a fixed attachment, not an anatomical proof. Library changes also fitted separately; full affine preserved.",
                ["connections"]=entries};
        }
        private static JObject FitJson(PonyJointFit.Result fit,float pixel)=>new()
        {
            ["rank"]=fit.Rank,["unknowns"]=fit.Unknowns,["identifiable"]=fit.Identifiable,["converged"]=fit.NumericallyConverged,
            ["condition"]=double.IsFinite(fit.Condition)?fit.Condition:null,["singularValues"]=new JArray(fit.SingularValues),
            ["parentAnchor"]=Point(fit.ParentAnchor),["childAnchor"]=Point(fit.ChildAnchor),["sharedRestPoint"]=Point(fit.SharedRestPoint),
            ["rms"]=fit.Rms,["max"]=fit.Max,["rmsSourcePixels"]=fit.Rms/pixel,["maxSourcePixels"]=fit.Max/pixel,["samples"]=fit.SampleCount
        };
        private static JObject ErrorsJson(PonyJointFit.ErrorSummary errors,float pixel)=>new()
        {
            ["rms"]=errors.Rms,["max"]=errors.Max,["rmsSourcePixels"]=errors.Rms/pixel,["maxSourcePixels"]=errors.Max/pixel,
            ["samples"]=errors.Samples,["worstClip"]=errors.WorstClip,["worstFrame"]=errors.WorstFrame,
            ["clips"]=JArray.FromObject(errors.Clips)
        };
        public static JArray Point(Vector2 value)=>new(value.x,value.y);
        private static void CheckMath()
        {
            var samples=new List<PonyJointFit.Sample>();var pivot=new Vector2(.22f,.51f);
            for(int f=0;f<17;f++)
            {
                var parent=Matrix4x4.TRS(new Vector3(.02f*f,.01f*f,0),Quaternion.Euler(0,0,f*2),Vector3.one);
                var child=parent*Matrix4x4.Translate(pivot)*Matrix4x4.Rotate(Quaternion.Euler(0,0,f*7))*Matrix4x4.Translate(-pivot);
                samples.Add(new PonyJointFit.Sample(parent,child,"synthetic",f));
            }
            var fit=PonyJointFit.FitSharedRest(samples,Matrix4x4.identity,Matrix4x4.identity);var independent=PonyJointFit.FitIndependent(samples);
            Require(fit.Identifiable && Vector2.Distance(fit.SharedRestPoint,pivot)<.00001f && fit.Max<.000001,"Не восстановлен известный шарнир");
            Require(independent.Identifiable && independent.Max<.000001,"Не восстановлены локальные точки известного шарнира");
            var translations=Enumerable.Range(0,10).Select(f=>new PonyJointFit.Sample(Matrix4x4.Translate(new Vector3(f*.01f,0,0)),Matrix4x4.Translate(new Vector3(f*.01f,0,0)),"translation",f)).ToArray();
            Require(!PonyJointFit.FitSharedRest(translations,Matrix4x4.identity,Matrix4x4.identity).Identifiable,"Перемещение ошибочно определяет уникальный центр");
            var bad=samples.Select((s,i)=>new PonyJointFit.Sample(s.Parent,Matrix4x4.Translate(new Vector3(.03f*Mathf.Sin(i),0,0))*s.Child,s.Clip,s.Frame)).ToArray();
            Require(PonyJointFit.FitSharedRest(bad,Matrix4x4.identity,Matrix4x4.identity).Rms>.001,"Непостоянное крепление ошибочно признано точным");
            Debug.Log("Поиск шарнира: synthetic PASS — известная точка, независимые точки, неидентифицируемое движение, нарушение крепления.");
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Проверка поиска шарнира: "+message);}
    }
}
