using System;
using System.IO;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Own render target only: previewing never creates objects in the user's scene.
    public sealed class PonyAnimationPreview : IDisposable
    {
        private RenderTexture target;
        public RenderTexture Texture=>target;
        public void Render(PonyAnimationArtwork art,Matrix4x4[] matrices,Rect world,int width,int height,float magic=0)
        {
            if(art==null || matrices==null || matrices.Length!=art.Frame.Nodes.Length || world.width<=0 || world.height<=0)
                throw new InvalidOperationException("Предпросмотр анимации: некорректные рисунок, матрицы или камера.");
            width=Mathf.Clamp(width,64,2048);height=Mathf.Clamp(height,64,2048);
            if(target==null || target.width!=width || target.height!=height)
            {
                Dispose();target=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB)
                    {name="Предпросмотр анимации пони",hideFlags=HideFlags.HideAndDontSave};target.Create();
            }
            float oldLight=Shader.GetGlobalFloat("_WorldLightEnabled");
            var command=new CommandBuffer {name="Редактор анимации пони"};
            try
            {
                command.SetRenderTarget(target);command.ClearRenderTarget(false,true,new Color(.17f,.19f,.22f,1));
                command.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(world.xMin,world.xMax,world.yMin,world.yMax,-10,10),false));
                command.SetGlobalFloat("_WorldLightEnabled",0);
                var block=new MaterialPropertyBlock();
                for(int i=0;i<art.Parts.Length;i++)
                {
                    var part=art.Parts[i];if(!part.visible)continue;
                    var color=PonyAppearance.Tint(part.slot,art.Appearance);if(PonyMagicRenderRoles.Contains(part))color.a*=magic;
                    block.Clear();block.SetColor("_Color",color);block.SetTexture("_MainTex",part.material.mainTexture);
                    block.SetFloat("_Emission",part.slot=="magic"?1:0);block.SetFloat("_ArtworkVisibilityEnabled",0);
                    command.DrawMesh(part.mesh,matrices[art.Frame.PartNodes[i]],part.material,0,0,block);
                }
                command.SetGlobalFloat("_WorldLightEnabled",oldLight);Graphics.ExecuteCommandBuffer(command);
            }
            finally {command.Release();Shader.SetGlobalFloat("_WorldLightEnabled",oldLight);}
        }
        public void WritePng(string path)
        {
            if(target==null)throw new InvalidOperationException("Сначала подготовьте предпросмотр.");
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        public void Dispose(){if(target!=null){target.Release();Object.DestroyImmediate(target);target=null;}}
    }
}
