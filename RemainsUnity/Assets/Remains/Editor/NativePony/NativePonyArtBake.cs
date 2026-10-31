using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // The old artwork is only an import source. The resulting PNGs and skinning data are native Unity assets.
    public static class NativePonyArtBake
    {
        public static readonly string[] Groups={"hindFar","frontFar","tail","torso","neck","head","mane","hindNear","frontNear","pipbuckNear"};
        public static string Group(string socket)=>socket switch
        {
            "hindUpperFar" or "hindLowerFar"=>"hindFar",
            "frontUpperFar" or "frontMiddleFar" or "frontLowerFar" or "pipbuckFar"=>"frontFar",
            "hindUpperNear" or "hindLowerNear"=>"hindNear",
            "frontUpperNear" or "frontMiddleNear" or "frontLowerNear"=>"frontNear",
            "tail" or "torso" or "neck" or "head" or "mane" or "pipbuckNear"=>socket,
            _=>throw new InvalidOperationException("Новый риг: неизвестное гнездо исходного рисунка "+socket)
        };
        public static Rect[] Rectangles(PonyAnimationArtwork[] variants,float pixelsPerUnit)
        {
            var result=new Rect[Groups.Length];
            for(int g=0;g<Groups.Length;g++)
            {
                Bounds box=default;bool any=false;
                foreach(var art in variants)for(int p=0;p<art.Parts.Length;p++)
                {
                    var part=art.Parts[p];var node=art.Frame.Nodes[art.Frame.PartNodes[p]];
                    if(!part.visible || Group(node.socket)!=Groups[g] || PonyMagicRenderRoles.Contains(part))continue;
                    var bounds=PonyRigDrawPlan.TransformBounds(part.mesh.bounds,node.transform.Matrix);
                    if(!any){box=bounds;any=true;}else box.Encapsulate(bounds);
                }
                if(!any)throw new InvalidOperationException("Новый риг: нет графики слоя "+Groups[g]);
                float x=Mathf.Floor(box.min.x*pixelsPerUnit-2)/pixelsPerUnit,y=Mathf.Floor(box.min.y*pixelsPerUnit-2)/pixelsPerUnit;
                result[g]=new Rect(x,y,Mathf.Ceil((box.max.x-x)*pixelsPerUnit+2)/pixelsPerUnit,Mathf.Ceil((box.max.y-y)*pixelsPerUnit+2)/pixelsPerUnit);
            }
            return result;
        }
        public static void Bake(PonyAnimationArtwork art,string group,Rect rect,float pixelsPerUnit,string path)
        {
            int width=Mathf.RoundToInt(rect.width*pixelsPerUnit),height=Mathf.RoundToInt(rect.height*pixelsPerUnit);
            var target=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            var command=new CommandBuffer {name="Новый риг: подготовка слоя "+group};float oldLight=Shader.GetGlobalFloat("_WorldLightEnabled");
            try
            {
                target.Create();command.SetRenderTarget(target);command.ClearRenderTarget(false,true,Color.clear);
                command.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(rect.xMin,rect.xMax,rect.yMin,rect.yMax,-10,10),false));
                command.SetGlobalFloat("_WorldLightEnabled",0);var properties=new MaterialPropertyBlock();
                for(int p=0;p<art.Parts.Length;p++)
                {
                    var part=art.Parts[p];var node=art.Frame.Nodes[art.Frame.PartNodes[p]];
                    if(!part.visible || Group(node.socket)!=group || PonyMagicRenderRoles.Contains(part))continue;
                    properties.Clear();properties.SetTexture("_MainTex",part.material.mainTexture);properties.SetColor("_Color",PonyAppearance.Tint(part.slot,art.Appearance));
                    properties.SetFloat("_Emission",0);properties.SetFloat("_ArtworkVisibilityEnabled",0);
                    command.DrawMesh(part.mesh,node.transform.Matrix,part.material,0,0,properties);
                }
                command.SetGlobalFloat("_WorldLightEnabled",oldLight);Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                // WorldLit renders premultiplied alpha; PNG sprite import expects straight RGB.
                var pixels=image.GetPixels();
                for(int i=0;i<pixels.Length;i++)if(pixels[i].a>0 && pixels[i].a<.9999f)
                {
                    float alpha=pixels[i].a;var linear=pixels[i].linear;linear.r/=alpha;linear.g/=alpha;linear.b/=alpha;
                    pixels[i]=linear.gamma;pixels[i].a=alpha;
                }
                image.SetPixels(pixels);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {Shader.SetGlobalFloat("_WorldLightEnabled",oldLight);RenderTexture.active=previous;command.Release();target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
        public static void Grid(string group,Rect bounds,out Vector2[] vertices,out int[] triangles)
        {
            bool rigid=group is "torso" or "head" or "mane" or "pipbuckNear";
            bool limb=group.StartsWith("front",StringComparison.Ordinal) || group.StartsWith("hind",StringComparison.Ordinal);
            int columns=rigid?1:Mathf.Clamp(Mathf.CeilToInt(bounds.width/(limb?.03f:.045f)),3,32),rows=rigid?1:Mathf.Clamp(Mathf.CeilToInt(bounds.height/(limb?.02f:.045f)),4,64);
            vertices=new Vector2[(columns+1)*(rows+1)];var indices=new List<int>();
            for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)vertices[y*(columns+1)+x]=new Vector2(Mathf.Lerp(bounds.xMin,bounds.xMax,(float)x/columns),Mathf.Lerp(bounds.yMin,bounds.yMax,(float)y/rows));
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
            {int a=y*(columns+1)+x,b=a+1,c=a+columns+1,d=c+1;indices.AddRange(new[]{a,c,b,b,c,d});}
            triangles=indices.ToArray();
        }
    }
}
