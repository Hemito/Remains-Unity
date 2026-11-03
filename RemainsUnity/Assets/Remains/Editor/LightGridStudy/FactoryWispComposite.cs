using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Editor adapter to the production projected masks and LightScene mask-union pass.
    // RGB belongs to each actual source; masks never subtract another wisp or static fill.
    internal sealed class FactoryWispComposite : IDisposable
    {
        private readonly ComputeShader compute;
        private readonly Material maskMaterial;
        private readonly Mesh quad;
        private readonly CommandBuffer commands;
        private readonly MaterialPropertyBlock block=new();
        private readonly RenderTexture frontRaw,backRaw,mask;
        private readonly int begin,add,resolve;
        private readonly Rect world;
        private readonly int width,height;
        internal RenderTexture Front {get;private set;}
        internal RenderTexture Back {get;private set;}
        internal long GpuBytes=>(long)width*height*8*5;
        internal FactoryWispComposite(Rect world,int width,int height)
        {
            this.world=world;this.width=width;this.height=height;
            try
            {
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Remains/Editor/LightGridStudy/FactoryWispComposite.compute");
                if(shader==null)throw new InvalidOperationException("Нет FactoryWispComposite.compute.");
                compute=Object.Instantiate(shader);begin=compute.FindKernel("Begin");add=compute.FindKernel("AddWisp");resolve=compute.FindKernel("Resolve");
                maskMaterial=new Material(GameAssets.Load<Shader>("Presentation/LightScene")??throw new InvalidOperationException("Нет штатного LightScene для масок виспов."));
                quad=LightSpriteShapes.UnitQuad("Editor: объединение штатных масок виспов");commands=new CommandBuffer{name="Editor: штатные маски виспа"};
                frontRaw=Target("Виспы: direct актёрам");backRaw=Target("Виспы: direct стенам");mask=Target("Виспы: объединение масок");
                Front=Target("Виспы: свет актёрам");Back=Target("Виспы: свет задней стене");
                compute.SetInts("_Size",width,height);compute.SetVector("_WorldRect",new Vector4(world.x,world.y,world.width,world.height));
                foreach(int kernel in new[]{begin,add,resolve})
                {compute.SetTexture(kernel,"_FrontRaw",frontRaw);compute.SetTexture(kernel,"_BackRaw",backRaw);}
                compute.SetTexture(resolve,"_Front",Front);compute.SetTexture(resolve,"_Back",Back);compute.SetTexture(add,"_Mask",mask);
            }
            catch{Dispose();throw;}
        }
        internal void Begin(Texture staticDirect,Rect staticRect,Texture reconstructedFill,float staticGain=.2f)
        {
            compute.SetTexture(begin,"_StaticDirect",staticDirect);compute.SetVector("_StaticRect",new Vector4(staticRect.x,staticRect.y,staticRect.width,staticRect.height));
            compute.SetFloat("_StaticGain",staticGain);compute.SetTexture(resolve,"_Fill",reconstructedFill);Dispatch(begin);
        }
        internal void Add(string id,Texture direct,IReadOnlyList<ProjectedLightShadow> shadows)
        {
            commands.Clear();commands.SetRenderTarget(mask);commands.ClearRenderTarget(false,true,Color.clear);
            commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(world.xMin,world.xMax,world.yMin,world.yMax,-1,1),false));
            foreach(var shadow in shadows)
            {
                if(shadow.SourceId!=id)continue;
                if(shadow.Mask==null || !float.IsFinite(shadow.Opacity) || shadow.Opacity<0 || shadow.Opacity>1)
                    throw new InvalidOperationException("Некорректная штатная маска виспа "+id);
                block.Clear();block.SetTexture("_MainTex",shadow.Mask);block.SetVector("_SpriteUvRect",new Vector4(0,0,1,1));block.SetFloat("_OccluderOpacity",shadow.Opacity);
                var b=shadow.WorldBounds;
                commands.DrawMesh(quad,Matrix4x4.TRS(new Vector3(b.x,b.y,0),Quaternion.identity,new Vector3(b.width,b.height,1)),maskMaterial,0,4,block);
            }
            Graphics.ExecuteCommandBuffer(commands);compute.SetTexture(add,"_Wisp",direct);Dispatch(add);
        }
        internal void Resolve(){Dispatch(resolve);}
        internal void Publish()
        {
            Shader.SetGlobalTexture("_RemainsRadiance",Front);Shader.SetGlobalTexture("_RemainsBackgroundRadiance",Back);
            Shader.SetGlobalVector("_RemainsRadianceRect",new Vector4(world.x,world.y,world.width,world.height));
            Shader.SetGlobalFloat("_RemainsRadianceTexel",world.width/width);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",1);Shader.SetGlobalFloat("_RemainsIndirectEnabled",0);
        }
        private void Dispatch(int kernel)=>compute.Dispatch(kernel,(width+7)/8,(height+7)/8,1);
        private RenderTexture Target(string name)
        {
            var t=new RenderTexture(width,height,0,GraphicsFormat.R16G16B16A16_SFloat){name=name,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            if(!t.Create()){Object.DestroyImmediate(t);throw new InvalidOperationException("Не создана текстура: "+name);}return t;
        }
        public void Dispose()
        {
            commands?.Dispose();
            foreach(var obj in new Object[]{Front,Back,frontRaw,backRaw,mask,compute,maskMaterial,quad})if(obj!=null)Object.DestroyImmediate(obj);
        }
    }
}
