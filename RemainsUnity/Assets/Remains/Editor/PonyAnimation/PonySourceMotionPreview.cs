using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonySourceMotionPreview
    {
        public static JObject Export(PonyPointPreviewAssets assets)
        {
            var reference=PonyAnimationArtwork.Prepare();var frames=new JArray();var sequences=new JArray();
            float maximum=0;var down=new JArray();var up=new JArray();int idle=-1;
            foreach(var setting in new[]{("crouchLower","Лечь"),("crouchRise","Встать"),("idle","Стойка")})
            {
                var clip=PonyAnimationData.Library.clips.Single(c=>c.id==setting.Item1);
                int count=setting.Item1=="idle"?1:clip.frames.Length;
                for(int f=0;f<count;f++)
                {
                    var bare=PonyAnimationArtwork.Prepare(sourceClip:setting.Item1,sourceFrame:f);var points=new Vector2[PonyPointControls.Parts.Length][];
                    for(int p=0;p<points.Length;p++)
                    {
                        string socket=PonyPointControls.Parts[p].Socket;
                        var rest=reference.Frame.Nodes.Single(n=>n.socket==socket).transform.Matrix;
                        var actual=bare.Frame.Nodes.Single(n=>n.socket==socket).transform.Matrix;
                        var delta=actual*rest.inverse;points[p]=PonyAffinePoints.Map(delta,PonyPointControls.Parts[p].Rest);
                        var rebuilt=PonyAffinePoints.Solve(PonyPointControls.Parts[p].Rest,points[p])*rest;
                        for(int i=0;i<16;i++)maximum=Mathf.Max(maximum,Mathf.Abs(rebuilt[i]-actual[i]));
                    }
                    var looks=new JArray();
                    foreach(string outfit in PonyPointStudy.Outfits)
                    {
                        var art=outfit.Length==0?bare:PonyAnimationArtwork.Prepare(outfit,setting.Item1,f);
                        for(int n=0;n<art.Frame.Nodes.Length;n++)
                        {
                            var other=bare.Frame.Nodes.Single(b=>b.socket==art.Frame.Nodes[n].socket).transform.Matrix;
                            var value=art.Frame.Nodes[n].transform.Matrix;
                            for(int i=0;i<16;i++)if(Mathf.Abs(other[i]-value[i])>.00001f)throw new InvalidOperationException("Исходное движение расходится между костюмами: "+setting.Item1+"/"+f);
                        }
                        foreach(var part in art.Parts.Where(p=>p.visible && !Remains.Presentation.PonyMagicRenderRoles.Contains(p)))
                        {
                            int index=Array.IndexOf(art.Parts,part);var bounds=PonyRigDrawPlan.TransformBounds(part.mesh.bounds,art.Frame.Nodes[art.Frame.PartNodes[index]].transform.Matrix);
                            if(bounds.min.x<PonyPointStudy.World.xMin || bounds.max.x>PonyPointStudy.World.xMax || bounds.min.y<PonyPointStudy.World.yMin || bounds.max.y>PonyPointStudy.World.yMax)
                                throw new InvalidOperationException($"Исходный кадр не помещается в просмотр: {setting.Item1}/{f}/{outfit}/{part.sourcePath}.");
                        }
                        looks.Add(assets.Look(art));
                    }
                    int at=frames.Count;frames.Add(new JObject { ["id"]=setting.Item1+"/"+f,["clip"]=setting.Item1,["frame"]=f,
                        ["points"]=PonyPointPreview.Points(points),["looks"]=looks });
                    if(setting.Item1=="crouchLower")down.Add(at);else if(setting.Item1=="crouchRise")up.Add(at);else idle=at;
                }
            }
            if(maximum>.00001f)throw new InvalidOperationException("Исходные преобразования изменились при подготовке опор: "+maximum);
            sequences.Add(new JObject { ["id"]="down",["label"]="Из анимации: лечь",["frames"]=down,["loop"]=false });
            sequences.Add(new JObject { ["id"]="up",["label"]="Из анимации: встать",["frames"]=up,["loop"]=false });
            var cycle=new JArray();foreach(var index in down)cycle.Add(index.DeepClone());for(int i=0;i<10;i++)cycle.Add(down.Last.DeepClone());
            foreach(var index in up)cycle.Add(index.DeepClone());for(int i=0;i<10;i++)cycle.Add(idle);
            sequences.Add(new JObject { ["id"]="cycle",["label"]="Из анимации: лечь и встать",["frames"]=cycle,["loop"]=true });
            var result=new JObject { ["frames"]=frames,["clips"]=sequences,["fps"]=30,["maxMatrixError"]=maximum,["sourceHash"]=PonyRenderAsset.SourceHash() };
            File.WriteAllText(Path.Combine(PonyPointStudy.Output,"source-motion-report.json"),new JObject {
                ["frames"]=frames.Count,["downFrames"]=down.Count,["upFrames"]=up.Count,["cycleFrames"]=cycle.Count,["maxMatrixError"]=maximum,
                ["ids"]=new JArray(frames.Select(f=>f["id"])) }.ToString());
            return result;
        }
    }
}
