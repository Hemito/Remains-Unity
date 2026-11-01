using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyLegShapeStudy
    {
        public static void RunBatch()
        {
            int exit=0;Directory.CreateDirectory(PonyPointStudy.Output);
            try
            {
                var dataset=new PonyLegShapeDataset();var fixtures=new JArray();
                foreach(var item in PonyPuppetStudy.Cases)
                {
                    var result=PonyLegShapePose.Evaluate(dataset.Model,item.Input);
                    string variant=result.UsedStrength>0?result.Prediction.Variant:dataset.DefaultVariant;
                    if(result.LimitedReason!=null)Debug.LogWarning($"Форма ноги {item.Id}: сила уменьшена до {result.UsedStrength}; {result.LimitedReason}");
                    fixtures.Add(new JObject { ["id"]=item.Id,["input"]=PonyPuppetStudy.InputJson(item.Input),["points"]=PonyPointPreview.Points(result.Points),
                        ["variant"]=variant,["usedStrength"]=result.UsedStrength,["reason"]=result.LimitedReason,["distance"]=result.Prediction.Distance,
                        ["indices"]=new JArray(result.Prediction.Indices),["weights"]=new JArray(result.Prediction.Weights) });
                    foreach(string outfit in PonyPointStudy.Outfits)
                    {
                        var art=dataset.Artwork(variant,outfit);var matrices=PonyPointControls.Matrices(art,result.Points);
                        for(int n=0;n<matrices.Length;n++)if(!PonyPointControls.Parts.Any(p=>p.Socket==art.Frame.Nodes[n].socket))
                            matrices[n]=Matrix4x4.Translate(item.Input.Body)*matrices[n];
                        using var preview=new PonyAnimationPreview();preview.Render(art,matrices,PonyPointStudy.World,560,560);
                        preview.WritePng(Path.Combine(PonyPointStudy.Output,"learned-"+item.Id+(outfit.Length==0?"-bare":"-antirad")+".png"));
                    }
                }
                double predicted=0,procedural=0;int count=0,variantMatches=0;
                foreach(var row in dataset.Rows.Where(r=>!r.Sample.Mirrored))
                {
                    // Hold out the entire clip, including duplicate examples in other clips.
                    bool Include(PonyLegShapeModel.Sample sample)=>sample.Sources.All(s=>!s.Id.StartsWith(row.Clip+"/",StringComparison.Ordinal));
                    if(!dataset.Model.Samples.Any(s=>!s.Mirrored && Include(s)))continue;
                    var target=PonyLegShapeModel.Driver(row.Points);var prediction=dataset.Model.Predict(target,include:Include);
                    var baseline=new[]{PonyPuppetControls.Segment(PonyPointControls.Parts[0].Rest,target.Hip,target.Knee,1),PonyPuppetControls.Segment(PonyPointControls.Parts[1].Rest,target.Knee,target.Foot,1)};
                    baseline[0][2]=Vector2.Lerp(baseline[0][2],PonyPointControls.Parts[0].Rest[2],.35f);
                    for(int p=0;p<2;p++)for(int i=0;i<3;i++){predicted+=(prediction.Points[p][i]-row.Points[p][i]).sqrMagnitude;procedural+=(baseline[p][i]-row.Points[p][i]).sqrMagnitude;}
                    count++;if(prediction.Variant==row.Sample.Variant)variantMatches++;
                }
                var probes=new JArray();
                for(int i=0;i<36;i++)
                {
                    float angle=i*Mathf.PI/18;var input=new PonyPuppetControls.Input {Foot=PonyPointControls.Parts[0].Rest[0]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.65f};
                    var result=PonyLegShapePose.Evaluate(dataset.Model,input);
                    probes.Add(new JObject { ["input"]=PonyPuppetStudy.InputJson(input),["points"]=PonyPointPreview.Points(result.Points),["usedStrength"]=result.UsedStrength,
                        ["variant"]=result.UsedStrength>0?result.Prediction.Variant:dataset.DefaultVariant });
                }
                var ambiguous=new JArray();
                for(int i=0;i<dataset.Model.Samples.Count;i++)for(int j=i+1;j<dataset.Model.Samples.Count;j++)
                {
                    var a=dataset.Model.Samples[i];var b=dataset.Model.Samples[j];if(a.Variant==b.Variant)continue;
                    double distance=Math.Sqrt(PonyLegShapeModel.Distance(a.Features,b.Features));
                    if(distance<.03)ambiguous.Add(new JObject {["first"]=a.Id,["second"]=b.Id,["distance"]=distance});
                }
                var report=new JObject { ["rawFrames"]=dataset.RawFrames,["uniqueExamples"]=dataset.Model.Samples.Count,["variants"]=dataset.Variants.Count,
                    ["skipped"]=new JArray(dataset.Skipped),["mirroredExamples"]=dataset.Model.Samples.Count(s=>s.Mirrored),
                    ["registrationError"]=dataset.MaximumRegistrationError,["decodeError"]=dataset.MaximumDecodeError,["fixtures"]=fixtures,["probes"]=probes,["ambiguousVariants"]=ambiguous,
                    ["holdout"]=new JObject { ["frames"]=count,["predictedRmsMetres"]=Math.Sqrt(predicted/Math.Max(1,count*6)),["proceduralRmsMetres"]=Math.Sqrt(procedural/Math.Max(1,count*6)),["variantMatches"]=variantMatches } };
                File.WriteAllText(Path.Combine(PonyPointStudy.Output,"leg-shape-report.json"),report.ToString());
                var original=JObject.Parse(File.ReadAllText(Path.Combine(PonyPointStudy.Output,"report.json")));
                PonyPointPreview.Write(original,puppet:true,model:dataset,fixtures:fixtures);
                Debug.Log($"Примеры формы ноги: PASS; кадров={dataset.RawFrames}, примеров={dataset.Model.Samples.Count}, вариантов={dataset.Variants.Count}; holdout={Math.Sqrt(predicted/Math.Max(1,count*6)):G4} м.");
            }
            catch(Exception error){Debug.LogException(error);File.WriteAllText(Path.Combine(PonyPointStudy.Output,"leg-shape-failure.txt"),error.ToString());exit=1;}
            EditorApplication.Exit(exit);
        }
    }
}
