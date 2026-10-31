using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyPointPreview
    {
        public static JArray Matrix(Matrix4x4 m)=>new(m.m00,m.m10,m.m01,m.m11,m.m03,m.m13);
        public static JArray Points(Vector2[][] points)=>new(points.Select(p=>new JArray(p.Select(v=>new JArray(v.x,v.y)))));
        public static void Write(JObject report)
        {
            var images=new JArray();var looks=new JArray();var cache=new Dictionary<string,int>(StringComparer.Ordinal);
            using(var preview=new PonyAnimationPreview())
            foreach(string outfit in PonyPointStudy.Outfits)
            {
                var art=PonyAnimationArtwork.Prepare(outfit);var draws=new JArray();
                var identity=art.Frame.Nodes.Select(_=>Matrix4x4.identity).ToArray();
                for(int p=0;p<art.Parts.Length;p++)
                {
                    var part=art.Parts[p];if(!part.visible || PonyMagicRenderRoles.Contains(part))continue;
                    var tint=PonyAppearance.Tint(part.slot,art.Appearance);
                    string key=part.mesh.GetInstanceID()+"/"+part.material.GetInstanceID()+"/"+tint.ToString("G9");
                    var bounds=part.mesh.bounds;float pad=.003f;
                    var local=new Rect(bounds.min.x-pad,bounds.min.y-pad,bounds.size.x+2*pad,bounds.size.y+2*pad);
                    if(!cache.TryGetValue(key,out int image))
                    {
                        image=images.Count;cache.Add(key,image);
                        int width=Mathf.Clamp(Mathf.CeilToInt(local.width*550),16,1024),height=Mathf.Clamp(Mathf.CeilToInt(local.height*550),16,1024);
                        preview.Render(art,identity,local,width,height,onlyPart:p,transparent:true);
                        string file=Path.Combine(PonyPointStudy.Output,"part-"+image.ToString("D3")+".png");preview.WritePng(file,straightAlpha:true);
                        images.Add(Embed(file));
                    }
                    int node=art.Frame.PartNodes[p];
                    draws.Add(new JObject { ["image"]=image,["node"]=node,["rect"]=new JArray(local.x,local.y,local.width,local.height) });
                }
                looks.Add(new JObject { ["label"]=outfit.Length==0?"Без костюма":"Антирад",["draws"]=draws,
                    ["nodes"]=new JArray(art.Frame.Nodes.Select(n=>new JObject { ["socket"]=n.socket,["matrix"]=Matrix(n.transform.Matrix) })) });
            }
            var parts=new JArray(PonyPointControls.Parts.Select(p=>new JObject { ["socket"]=p.Socket,["label"]=p.Label,["labels"]=new JArray(p.Labels) }));
            var proof=new JArray();
            foreach(var item in (JArray)report["nativeImages"])
                proof.Add(new JObject { ["label"]=item["id"],["images"]=new JArray(Embed(Path.Combine(PonyPointStudy.Output,(string)item["original"])),Embed(Path.Combine(PonyPointStudy.Output,(string)item["rebuilt"]))) });
            var data=new JObject { ["parts"]=parts,["rest"]=Points(PonyPointControls.Parts.Select(p=>p.Rest).ToArray()),["looks"]=looks,["images"]=images,
                ["presets"]=report["presets"],["presetLabels"]=new JArray(PonyPointControls.PresetLabels),["proof"]=proof,
                ["world"]=new JArray(PonyPointStudy.World.x,PonyPointStudy.World.y,PonyPointStudy.World.width,PonyPointStudy.World.height),
                ["metrics"]=new JObject { ["frames"]=report["frames"],["nodes"]=report["nodes"],["matrixError"]=report["maxMatrixError"],["pixelError"]=report["nativeImageMaxChannelError"] } };
            string template=File.ReadAllText("Assets/Remains/Editor/PonyAnimation/PonyPointPreview.html");
            File.WriteAllText(Path.Combine(PonyPointStudy.Output,"affine-points.html"),template.Replace("@DATA@",data.ToString(Formatting.None)));
        }
        private static string Embed(string path)=>"data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(path));
    }
}
