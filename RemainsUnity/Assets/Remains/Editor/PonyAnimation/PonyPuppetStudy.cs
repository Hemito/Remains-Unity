using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyPuppetStudy
    {
        public static readonly (string Id,string Label,PonyPuppetControls.Input Input)[] Cases={
            ("rest","Исходная стойка",new()),
            ("lift","Поднять копыто",new(){Foot=new Vector2(-.13f,.19f)}),
            ("back","Отвести копыто",new(){Foot=new Vector2(-.47f,.08f)}),
            ("body","Опустить корпус",new(){Body=new Vector2(0,-.1f)}),
            ("knee","Поправить колено",new(){Foot=new Vector2(-.25f,.12f),KneeOffset=new Vector2(.025f,-.035f)}),
            ("width","Изменить толщину",new(){Foot=new Vector2(-.13f,.19f),Width=.85f})
        };
        public static JObject Definition()
        {
            var cases=new JArray();foreach(var item in Cases)
            {
                var result=PonyPuppetControls.Solve(item.Input);
                cases.Add(new JObject { ["id"]=item.Id,["label"]=item.Label,["input"]=InputJson(item.Input),
                    ["points"]=PonyPointPreview.Points(result.Points),["limited"]=result.ReachLimited,["jointLimited"]=result.JointLimited });
            }
            var fine=FineFixture();
            return new JObject { ["cases"]=cases,["bodyHandle"]=PonyJointStudy.Point(PonyPuppetBodyControls.Pivot),["maxDragAngleStep"]=PonyPuppetLimits.MaximumDragAngleStep,["fineCase"]=new JObject {
                ["input"]=InputJson(Cases[1].Input),["correction"]=PonyPointPreview.Points(fine),
                ["points"]=PonyPointPreview.Points(PonyPuppetControls.ApplyFine(PonyPuppetControls.Solve(Cases[1].Input),fine)) } };
        }
        private static Vector2[][] FineFixture()=>new[]{new[]{Vector2.zero,Vector2.zero,new Vector2(.012f,-.008f)},new[]{Vector2.zero,Vector2.zero,new Vector2(-.015f,0)}};
        public static JObject InputJson(PonyPuppetControls.Input value)=>new()
        {
            ["body"]=PonyJointStudy.Point(value.Body),["foot"]=PonyJointStudy.Point(value.Foot),["kneeOffset"]=PonyJointStudy.Point(value.KneeOffset),
            ["bodyRelative"]=value.BodyRelative,["bodyAngle"]=value.BodyAngle,
            ["width"]=value.Width,["attachment"]=value.Attachment,["limits"]=new JObject {
                ["upperMin"]=value.Limits.UpperMin,["upperMax"]=value.Limits.UpperMax,["kneeMin"]=value.Limits.KneeMin,["kneeMax"]=value.Limits.KneeMax,
                ["footCone"]=value.Limits.FootCone,["minLength"]=value.Limits.MinLength,["maxLength"]=value.Limits.MaxLength }
        };
        public static void RunBatch()
        {
            int exit=0;Directory.CreateDirectory(PonyPointStudy.Output);
            try
            {
                float maximum=0;int checkedCases=0;
                foreach(var item in Cases)
                {
                    var solved=PonyPuppetControls.Solve(item.Input);
                    if(Vector2.Distance(solved.Points[0][1],solved.Points[1][0])>1e-6)throw new InvalidOperationException("Рассоединились опоры колена: "+item.Id);
                    if(!solved.ReachLimited && Vector2.Distance(solved.Foot,item.Input.Foot)>1e-6)throw new InvalidOperationException("Копыто не достигло цели: "+item.Id);
                    if(item.Id=="rest")for(int p=0;p<2;p++)for(int i=0;i<3;i++)maximum=Mathf.Max(maximum,Vector2.Distance(solved.Points[p][i],PonyPointControls.Parts[p].Rest[i]));
                    foreach(string outfit in PonyPointStudy.Outfits)
                    {
                        var art=PonyAnimationArtwork.Prepare(outfit);var matrices=PonyPuppetControls.Matrices(art,item.Input);
                        using var preview=new PonyAnimationPreview();preview.Render(art,matrices,PonyPointStudy.World,560,560);
                        preview.WritePng(Path.Combine(PonyPointStudy.Output,"puppet-"+item.Id+(outfit.Length==0?"-bare":"-antirad")+".png"));
                    }
                    checkedCases++;
                }
                if(maximum>.00001)throw new InvalidOperationException("Кукольное управление изменило исходную стойку: "+maximum);
                var outside=PonyPuppetControls.Solve(new(){Foot=new Vector2(10,10)});
                if(!outside.ReachLimited)throw new InvalidOperationException("Не определена недостижимая цель копыта.");
                foreach(string outfit in PonyPointStudy.Outfits)
                {
                    var art=PonyAnimationArtwork.Prepare(outfit);var matrices=PonyPuppetControls.Matrices(art,Cases[1].Input,FineFixture());
                    using var preview=new PonyAnimationPreview();preview.Render(art,matrices,PonyPointStudy.World,560,560);
                    preview.WritePng(Path.Combine(PonyPointStudy.Output,"puppet-fine"+(outfit.Length==0?"-bare":"-antirad")+".png"));
                }
                var limitCases=new JArray();
                for(int i=0;i<72;i++)
                {
                    float angle=i*Mathf.PI/36;var input=new PonyPuppetControls.Input {Foot=PonyPointControls.Parts[0].Rest[0]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*.9f};
                    var result=PonyPuppetControls.Solve(input);
                    if(!PonyPuppetLimits.Check(result.Points,input.Limits,out var reason))throw new InvalidOperationException("Обход цели вокруг таза: "+i+": "+reason);
                    limitCases.Add(new JObject { ["input"]=InputJson(input),["points"]=PonyPointPreview.Points(result.Points),["limited"]=result.ReachLimited });
                }
                var reverse=PonyPuppetControls.Solve(new(){KneeOffset=new Vector2(0,-1)});
                if(!reverse.JointLimited || !PonyPuppetLimits.Check(reverse.Points,new(),out _))throw new InvalidOperationException("Не ограничен обратный сгиб колена.");
                var badPoints=PonyPointControls.Parts.Select(p=>(Vector2[])p.Rest.Clone()).ToArray();badPoints[1][1]=badPoints[1][0]+new Vector2(.3f,-.1f);
                if(PonyPuppetLimits.Check(badPoints,new(),out _))throw new InvalidOperationException("Ручная правка обошла предел сгиба.");
                var report=Definition();report["restPointError"]=maximum;report["checks"]=checkedCases;report["farTargetLimited"]=outside.ReachLimited;
                report["limitCases"]=limitCases;report["reverseKneeLimited"]=true;report["badFineRejected"]=true;
                File.WriteAllText(Path.Combine(PonyPointStudy.Output,"puppet-limits-report.json"),report.ToString());
                var original=JObject.Parse(File.ReadAllText(Path.Combine(PonyPointStudy.Output,"report.json")));
                PonyPointPreview.Write(original,puppet:true);
                Debug.Log($"Кукольное управление: PASS; поз={checkedCases}, ошибка стойки={maximum:G6}; {PonyPointStudy.Output}");
            }
            catch(Exception problem){Debug.LogException(problem);File.WriteAllText(Path.Combine(PonyPointStudy.Output,"puppet-failure.txt"),problem.ToString());exit=1;}
            EditorApplication.Exit(exit);
        }
    }
}
