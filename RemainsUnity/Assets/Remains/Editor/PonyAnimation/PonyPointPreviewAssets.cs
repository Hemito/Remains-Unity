using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Shared, ordered draw assets for manual poses and original animation frames.
    public sealed class PonyPointPreviewAssets:IDisposable
    {
        public readonly JArray Images=new();
        private readonly Dictionary<string,int> cache=new(StringComparer.Ordinal);
        private readonly PonyAnimationPreview preview=new();
        public JObject Look(PonyAnimationArtwork art)
        {
            var draws=new JArray();var identity=art.Frame.Nodes.Select(_=>Matrix4x4.identity).ToArray();
            for(int p=0;p<art.Parts.Length;p++)
            {
                var part=art.Parts[p];if(!part.visible || PonyMagicRenderRoles.Contains(part))continue;
                var tint=PonyAppearance.Tint(part.slot,art.Appearance);
                string key=part.mesh.GetInstanceID()+"/"+part.material.GetInstanceID()+"/"+tint.ToString("G9");
                var bounds=part.mesh.bounds;const float pad=.003f;
                var local=new Rect(bounds.min.x-pad,bounds.min.y-pad,bounds.size.x+2*pad,bounds.size.y+2*pad);
                if(!cache.TryGetValue(key,out int image))
                {
                    image=Images.Count;cache.Add(key,image);
                    int width=Mathf.Clamp(Mathf.CeilToInt(local.width*550),16,1024),height=Mathf.Clamp(Mathf.CeilToInt(local.height*550),16,1024);
                    preview.Render(art,identity,local,width,height,onlyPart:p,transparent:true);
                    string file=Path.Combine(PonyPointStudy.Output,"part-"+image.ToString("D3")+".png");preview.WritePng(file,straightAlpha:true);
                    Images.Add("data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(file)));
                }
                draws.Add(new JObject { ["image"]=image,["node"]=art.Frame.PartNodes[p],["symbol"]=part.symbolId,
                    ["rect"]=new JArray(local.x,local.y,local.width,local.height) });
            }
            return new JObject { ["label"]=art.Outfit.Length==0?"Без костюма":"Антирад",["draws"]=draws,
                ["nodes"]=new JArray(art.Frame.Nodes.Select(n=>new JObject { ["socket"]=n.socket,["matrix"]=PonyPointPreview.Matrix(n.transform.Matrix) })) };
        }
        public void Dispose()=>preview.Dispose();
    }
}
