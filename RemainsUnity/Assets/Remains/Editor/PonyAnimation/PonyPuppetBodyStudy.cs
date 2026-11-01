using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyPuppetBodyStudy
    {
        public static (string Id,string Label,PonyPuppetControls.Input Input)[] Cases()
        {
            var result=PonyPuppetStudy.Cases.Select(c=>(c.Id,c.Id=="knee"?"Согнуть ногу":c.Label,PonyPuppetBodyControls.Prepare(c.Input))).ToList();
            var tilt=PonyPuppetBodyControls.Prepare(PonyPuppetStudy.Cases[2].Input);tilt.BodyAngle=20;tilt.Foot=PonyPuppetBodyControls.Frame(tilt).MultiplyPoint3x4(tilt.Foot);
            result.Add(("tilt","Наклон корпуса",tilt));return result.ToArray();
        }
        public static JObject Definition()
        {
            var value=PonyPuppetStudy.Definition();value["cases"]=new JArray(Cases().Select(c=>new JObject { ["id"]=c.Id,["label"]=c.Label,
                ["input"]=PonyPuppetStudy.InputJson(c.Input),["points"]=PonyPointPreview.Points(PonyPuppetBodyControls.Solve(c.Input).Points) }));return value;
        }
        public static void RunBatch()
        {
            int exit=0;
            try
            {
                var dataset=new PonyLegShapeDataset();var fixtures=new JArray();var cases=Cases();
                JObject Fixture(string id,PonyPuppetControls.Input input)
                {
                    var control=PonyPuppetBodyControls.Solve(input);var result=PonyLegShapePose.Evaluate(dataset.Model,input);
                    return new JObject { ["id"]=id,["input"]=PonyPuppetStudy.InputJson(input),["points"]=PonyPointPreview.Points(result.Points),["controlPoints"]=PonyPointPreview.Points(control.Points),
                        ["variant"]=result.UsedStrength>0?result.Prediction.Variant:dataset.DefaultVariant,["usedStrength"]=result.UsedStrength,["indices"]=new JArray(result.Prediction.Indices) };
                }
                foreach(var item in cases)
                {
                    var fixture=Fixture(item.Id,item.Input);fixtures.Add(fixture);var solved=PonyLegShapePose.Evaluate(dataset.Model,item.Input);
                    foreach(string outfit in PonyPointStudy.Outfits)
                    {
                        var art=dataset.Artwork((string)fixture["variant"],outfit);var matrices=PonyPointControls.Matrices(art,solved.Points);
                        for(int n=0;n<matrices.Length;n++)if(!PonyPointControls.Parts.Any(p=>p.Socket==art.Frame.Nodes[n].socket))matrices[n]=PonyPuppetBodyControls.Frame(item.Input)*matrices[n];
                        using var preview=new PonyAnimationPreview();preview.Render(art,matrices,PonyPointStudy.World,560,560);preview.WritePng(Path.Combine(PonyPointStudy.Output,"local-"+item.Id+(outfit.Length==0?"-bare":"-antirad")+".png"));
                    }
                }
                var baseline=cases.Single(c=>c.Id=="back").Input;var kneeCases=new JArray();float lengthError=0,invarianceError=0;
                for(int i=0;i<24;i++)
                {
                    float angle=i*Mathf.PI/12;var target=PonyPointControls.Parts[0].Rest[0]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(i%2==0?.1f:2);
                    var input=PonyPuppetBodyControls.MoveKnee(baseline,target);var pose=PonyPuppetBodyControls.Solve(input);
                    for(int p=0;p<2;p++)lengthError=Mathf.Max(lengthError,Mathf.Abs(Vector2.Distance(pose.Points[p][0],pose.Points[p][1])-Vector2.Distance(PonyPointControls.Parts[p].Rest[0],PonyPointControls.Parts[p].Rest[1])));
                    var fixture=Fixture("knee-"+i,input);fixture["target"]=PonyJointStudy.Point(target);kneeCases.Add(fixture);
                }
                var reference=PonyLegShapePose.Evaluate(dataset.Model,baseline);var bodyCases=new JArray();
                foreach(float angle in new[]{-30f,-15f,0f,15f,30f})
                {
                    var input=PonyPuppetBodyControls.Prepare(baseline);input.Body=new Vector2(.13f,.08f);input.BodyAngle=angle;var frame=PonyPuppetBodyControls.Frame(input);input.Foot=frame.MultiplyPoint3x4(baseline.Foot);
                    var pose=PonyLegShapePose.Evaluate(dataset.Model,input);var local=PonyPuppetBodyControls.Map(frame.inverse,pose.Points);
                    for(int p=0;p<2;p++)for(int i=0;i<3;i++)invarianceError=Mathf.Max(invarianceError,Vector2.Distance(reference.Points[p][i],local[p][i]));
                    bodyCases.Add(Fixture("body-"+angle,input));
                }
                if(lengthError>.00001f || invarianceError>.00002f)throw new InvalidOperationException($"Контроль куклы: длина={lengthError}, независимость от переноса/наклона={invarianceError}.");
                var report=new JObject { ["fixtures"]=fixtures,["kneeCases"]=kneeCases,["bodyCases"]=bodyCases,["maximumLengthError"]=lengthError,["bodyInvarianceError"]=invarianceError };
                File.WriteAllText(Path.Combine(PonyPointStudy.Output,"puppet-local-report.json"),report.ToString());
                PonyPointPreview.Write(JObject.Parse(File.ReadAllText(Path.Combine(PonyPointStudy.Output,"report.json"))),puppet:true,model:dataset,fixtures:fixtures,bodyRelative:true);
                Debug.Log($"Кукла относительно корпуса: PASS; длина={lengthError:G6}, независимость от наклона={invarianceError:G6}.");
            }
            catch(Exception error){Debug.LogException(error);File.WriteAllText(Path.Combine(PonyPointStudy.Output,"puppet-local-failure.txt"),error.ToString());exit=1;}
            EditorApplication.Exit(exit);
        }
    }
}
