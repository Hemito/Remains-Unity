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
    public static class PonyPointStudy
    {
        public static string Output=>Path.GetFullPath("../.reference/affine-points");
        public static readonly Rect World=new(-1.08f,-.25f,2.3f,2.3f);
        public static readonly string[] Outfits={"","armor.antirad"};
        public static void RunBatch()
        {
            int exit=0;Directory.CreateDirectory(Output);
            try
            {
                CheckMath();PonyAnimationData.Load();PonyRigDrawPlan.Load();
                var bank=GameAssets.Load<PonyRigMotionAsset>("Original/Pony/rig-motion");
                var source=new[]{new Vector2(.07f,.11f),new Vector2(.39f,-.02f),new Vector2(-.06f,.47f)};
                int frames=0,nodes=0;double maximum=0;
                foreach(var clip in bank.Clips)foreach(var frame in clip.frames)
                {
                    frames++;foreach(var node in frame.nodes)
                    {
                        var original=node.transform.Matrix;var rebuilt=PonyAffinePoints.Solve(source,PonyAffinePoints.Map(original,source));nodes++;
                        for(int i=0;i<16;i++)maximum=Math.Max(maximum,Math.Abs(original[i]-rebuilt[i]));
                    }
                }
                Require(maximum<.00001,"Матрицы исходных движений изменились: "+maximum);
                var images=new JArray();int maxChannel=0;long differing=0,total=0;
                using(var preview=new PonyAnimationPreview())
                foreach(var item in new[]{("idle",0),("walk",4),("walk",12),("crouchLower",7),("climb",7)})
                foreach(string outfit in Outfits)
                {
                    var art=PonyAnimationArtwork.Prepare(outfit,item.Item1,item.Item2);
                    var matrices=art.Frame.Nodes.Select(n=>n.transform.Matrix).ToArray();
                    var rebuilt=matrices.Select(m=>PonyAffinePoints.Solve(source,PonyAffinePoints.Map(m,source))).ToArray();
                    string name=item.Item1+"-"+item.Item2+(outfit.Length==0?"-bare":"-antirad");
                    preview.Render(art,matrices,World,560,560);var before=Read(preview.Texture);preview.WritePng(Path.Combine(Output,name+"-original.png"));
                    preview.Render(art,rebuilt,World,560,560);var after=Read(preview.Texture);preview.WritePng(Path.Combine(Output,name+"-rebuilt.png"));
                    for(int p=0;p<before.Length;p++)
                    {
                        int error=Math.Max(Math.Abs(before[p].r-after[p].r),Math.Max(Math.Abs(before[p].g-after[p].g),Math.Abs(before[p].b-after[p].b)));
                        maxChannel=Math.Max(maxChannel,error);if(error>2)differing++;total++;
                    }
                    images.Add(new JObject { ["id"]=name,["original"]=name+"-original.png",["rebuilt"]=name+"-rebuilt.png" });
                }
                Require(differing/(double)total<.0001,"Восстановление опор заметно изменило изображение.");
                var presets=new JArray();
                using(var preview=new PonyAnimationPreview())
                foreach(string id in PonyPointControls.PresetIds)
                {
                    var points=PonyPointControls.Preset(id);var matrices=new JArray();
                    foreach(string outfit in Outfits)
                    {
                        var art=PonyAnimationArtwork.Prepare(outfit);var posed=PonyPointControls.Matrices(art,points);
                        preview.Render(art,posed,World,560,560);preview.WritePng(Path.Combine(Output,id+(outfit.Length==0?"-bare":"-antirad")+".png"));
                        // Wardrobe reads exactly the same actor-space edit, regardless of the underlying local mesh.
                        for(int p=0;p<PonyPointControls.Parts.Length;p++)
                        {
                            var delta=PonyAffinePoints.Solve(PonyPointControls.Parts[p].Rest,points[p]);
                            var check=PonyAffinePoints.Map(delta,PonyPointControls.Parts[p].Rest);
                            Require(check.Zip(points[p],Vector2.Distance).Max()<.00001,"Опоры не попали в заданные положения.");
                        }
                    }
                    foreach(int p in Enumerable.Range(0,points.Length))matrices.Add(PonyPointPreview.Matrix(PonyAffinePoints.Solve(PonyPointControls.Parts[p].Rest,points[p])));
                    presets.Add(new JObject { ["id"]=id,["points"]=PonyPointPreview.Points(points),["deltas"]=matrices });
                }
                var report=new JObject { ["schema"]=1,["clips"]=bank.Clips.Length,["frames"]=frames,["nodes"]=nodes,["maxMatrixError"]=maximum,
                    ["nativeImagePixels"]=total,["nativeImageMaxChannelError"]=maxChannel,["nativeImagePixelsOver2"]=differing,
                    ["nativeImages"]=images,["presets"]=presets,["scope"]="Independent affine handles; optional knee coupling is an authoring constraint, not inferred anatomy." };
                File.WriteAllText(Path.Combine(Output,"report.json"),report.ToString());
                PonyPointPreview.Write(report);
                Debug.Log($"Три опоры: PASS; поз={bank.Clips.Length}, кадров={frames}, матриц={nodes}, max error={maximum:G5}; пикселей с ошибкой >2={differing}/{total}; {Output}");
            }
            catch(Exception problem){Debug.LogException(problem);File.WriteAllText(Path.Combine(Output,"failure.txt"),problem.ToString());exit=1;}
            EditorApplication.Exit(exit);
        }
        private static void CheckMath()
        {
            var source=new[]{new Vector2(-.2f,.7f),new Vector2(-.3f,.2f),new Vector2(.1f,.5f)};
            var matrix=Matrix4x4.identity;matrix.m00=-1.2f;matrix.m01=.4f;matrix.m10=.2f;matrix.m11=.8f;matrix.m03=.17f;
            var solved=PonyAffinePoints.Solve(source,PonyAffinePoints.Map(matrix,source));
            for(int i=0;i<16;i++)Require(Mathf.Abs(matrix[i]-solved[i])<.000001,"Не восстановлено отражение/растяжение/скос.");
            Require(!PonyAffinePoints.TrySolve(new[]{Vector2.zero,Vector2.one,Vector2.one*2},source,out _,out _),"Не отклонены коллинеарные исходные опоры.");
            Require(!PonyAffinePoints.AcceptEdit(Matrix4x4.Scale(new Vector3(-1,1,1)),out _),"Разрешён выворот при редактировании.");
            Require(!PonyAffinePoints.AcceptEdit(Matrix4x4.Scale(new Vector3(.05f,1,1)),out _),"Разрешено схлопывание при редактировании.");
        }
        private static Color32[] Read(RenderTexture target)
        {
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image.GetPixels32();}
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException("Проверка трёх опор: "+reason);}
    }
}
