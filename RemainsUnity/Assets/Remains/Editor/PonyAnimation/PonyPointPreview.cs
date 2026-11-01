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
        public static void Write(JObject report,bool puppet=false)
        {
            using var assets=new PonyPointPreviewAssets();var looks=new JArray();
            foreach(string outfit in PonyPointStudy.Outfits)
            {
                looks.Add(assets.Look(PonyAnimationArtwork.Prepare(outfit)));
            }
            var parts=new JArray(PonyPointControls.Parts.Select(p=>new JObject { ["socket"]=p.Socket,["label"]=p.Label,["labels"]=new JArray(p.Labels) }));
            var proof=new JArray();
            foreach(var item in (JArray)report["nativeImages"])
                proof.Add(new JObject { ["label"]=item["id"],["images"]=new JArray(Embed(Path.Combine(PonyPointStudy.Output,(string)item["original"])),Embed(Path.Combine(PonyPointStudy.Output,(string)item["rebuilt"]))) });
            var data=new JObject { ["parts"]=parts,["rest"]=Points(PonyPointControls.Parts.Select(p=>p.Rest).ToArray()),["looks"]=looks,["images"]=assets.Images,
                ["presets"]=report["presets"],["presetLabels"]=new JArray(PonyPointControls.PresetLabels),["proof"]=proof,
                ["world"]=new JArray(PonyPointStudy.World.x,PonyPointStudy.World.y,PonyPointStudy.World.width,PonyPointStudy.World.height),
                ["metrics"]=new JObject { ["frames"]=report["frames"],["nodes"]=report["nodes"],["matrixError"]=report["maxMatrixError"],["pixelError"]=report["nativeImageMaxChannelError"] } };
            if(puppet){data["puppet"]=PonyPuppetStudy.Definition();data["source"]=PonySourceMotionPreview.Export(assets);}
            string template=File.ReadAllText("Assets/Remains/Editor/PonyAnimation/"+(puppet?"PonyPuppetPreview.html":"PonyPointPreview.html"));
            if(puppet)template=template.Replace("@SCRIPT@",File.ReadAllText("Assets/Remains/Editor/PonyAnimation/PonyPuppetPreview.js"));
            File.WriteAllText(Path.Combine(PonyPointStudy.Output,puppet?"affine-puppet-motion.html":"affine-points.html"),template.Replace("@DATA@",data.ToString(Formatting.None)));
        }
        private static string Embed(string path)=>"data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(path));
    }
}
