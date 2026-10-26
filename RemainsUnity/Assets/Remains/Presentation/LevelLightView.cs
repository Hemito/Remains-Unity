using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // What the shaders read of a level light: two textures over the camera area with a margin (_RemainsRadiance for
    // actors and props, _RemainsBackgroundRadiance for walls with actor shadows) and the level's bounce light.
    // Rebuilt only when the area moves, the direct light changes, the projected shadows change or a flash shows;
    // the work is proportional to the area, not to the level.
    public sealed class LevelLightView : IDisposable
    {
        public const float Margin=8;           // m of light beyond the camera view on each side
        public const float FlashReach=12;      // m, as the prototype's muzzle flash
        private const float FlashGlowThreshold=-.15f;   // every visible pixel of a flash mask glows
        private const string ComputeResource="Presentation/LevelLightComposite",SceneShaderResource="Presentation/LightScene";
        private const int ShadowMaskPass=4;
        private readonly LevelLight light;
        private readonly LightShaderContext shader;
        private ComputeShader compute;
        private int subtractKernel,addFlashKernel;
        private RenderTexture front,back,mask,flash;
        private Material maskMaterial;
        private Mesh quad;
        private CommandBuffer commands;
        private readonly MaterialPropertyBlock block=new();
        private RectInt area;
        private bool hasArea,hasSignature,flashShown,disposed;
        private int signature;
        private readonly HashSet<string> composed=new(StringComparer.Ordinal),warned=new(StringComparer.Ordinal);
        public RectInt Area=>area;
        public Rect WorldArea=>hasArea?new Rect(light.Scene.WorldRect.x+area.x*light.Scene.Texel,light.Scene.WorldRect.y+area.y*light.Scene.Texel,area.width*light.Scene.Texel,area.height*light.Scene.Texel):default;
        public int Compositions {get;private set;}

        public LevelLightView(LevelLight light,LightShaderContext shader)
        {
            this.light=light??throw new ArgumentNullException(nameof(light));
            this.shader=shader??throw new ArgumentNullException(nameof(shader));
            try
            {
                compute=GameAssets.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource);
                subtractKernel=compute.FindKernel("SubtractField");addFlashKernel=compute.FindKernel("AddFlash");
                maskMaterial=new Material(GameAssets.Load<Shader>(SceneShaderResource)??throw new InvalidOperationException("Нет шейдера "+SceneShaderResource)) {name="Маска тени над камерой",hideFlags=HideFlags.HideAndDontSave};
                quad=LightSpriteShapes.UnitQuad("Квадрат маски тени");
                commands=new CommandBuffer {name="Свет над камерой: маска тени"};
                int reach=Mathf.CeilToInt(2*FlashReach*light.Scene.Resolution)+64;
                flash=Target(reach,reach,"Свет над камерой: вспышка");
            }
            catch {Dispose();throw;}
        }

        // view: the camera's visible world rectangle. Composes when something it shows changed; publishes the globals.
        public void Present(Rect view,IReadOnlyList<SpriteEmission> flashes,IReadOnlyList<ProjectedLightShadow> masks,IReadOnlyList<string> flashIds)
        {
            if(disposed) throw new ObjectDisposedException(nameof(LevelLightView));
            int flashCount=flashes?.Count??0;
            if(flashCount>0 && (flashIds==null || flashIds.Count!=flashCount)) throw new ArgumentException("Количество ID источников не совпадает с количеством вспышек.");
            bool moved=Place(view);
            var hash=new HashCode();hash.Add(area);hash.Add(light.Direct.Revision);
            if(masks!=null) foreach(var m in masks)
            {hash.Add(m.SourceId);hash.Add(m.Mask!=null?m.Mask.GetInstanceID():0);hash.Add(m.WorldBounds);hash.Add(m.Opacity);hash.Add(m.Revision);}
            int next=hash.ToHashCode();
            if(!moved && flashCount==0 && !flashShown && hasSignature && next==signature) return;
            Remains.Diagnostics.FrameHitchLog.Gpu("Свет: камера (тени и вспышки)");
            using var section=Remains.Diagnostics.FrameHitchLog.Section("Свет: камера (тени и вспышки)");
            var sum=light.Direct.Sum;
            Graphics.CopyTexture(sum,0,0,area.x,area.y,area.width,area.height,front,0,0,0,0);
            Graphics.CopyTexture(sum,0,0,area.x,area.y,area.width,area.height,back,0,0,0,0);
            composed.Clear();
            if(masks!=null) foreach(var m in masks)
            {
                if(composed.Contains(m.SourceId) || IsFlash(m.SourceId,flashIds)) continue;
                composed.Add(m.SourceId);
                if(!light.Direct.TryGetField(m.SourceId,out var field,out var texels))
                {
                    if(light.Sources.TryGet(m.SourceId,out _) && warned.Add(m.SourceId))
                        Debug.LogWarning($"Свет над камерой: прямой свет {m.SourceId} ещё не построен; тень от него пропущена до подготовки поля.");
                    continue;
                }
                DrawMask(masks,m.SourceId);
                Apply(subtractKernel,field,texels);
            }
            for(int i=0;i<flashCount;i++)
            {
                if(!TryFlashSource(flashes[i],flashIds[i],out var source)) continue;
                light.Direct.BuildTransient(source,flash);
                DrawMask(masks,flashIds[i]);
                Apply(addFlashKernel,flash,source.Texels);
            }
            flashShown=flashCount>0;signature=next;hasSignature=true;Compositions++;
            Publish();
        }
        private static bool IsFlash(string id,IReadOnlyList<string> flashIds)
        {
            if(flashIds!=null) for(int i=0;i<flashIds.Count;i++) if(flashIds[i]==id) return true;
            return false;
        }

        // The area follows the camera: it moves (snapped to texels) only when the view leaves its inner part.
        private bool Place(Rect view)
        {
            var scene=light.Scene;float texel=scene.Texel;
            var wanted=Rect.MinMaxRect(view.xMin-Margin,view.yMin-Margin,view.xMax+Margin,view.yMax+Margin);
            int w=Mathf.Min(scene.Width,Mathf.CeilToInt(wanted.width/texel)+2),h=Mathf.Min(scene.Height,Mathf.CeilToInt(wanted.height/texel)+2);
            if(front==null || front.width<w || front.height<h) Allocate(Math.Max(w,front?.width??0),Math.Max(h,front?.height??0));
            w=front.width;h=front.height;   // the area fills the textures: the published rectangle has no stale border
            if(hasArea && area.width>=w && area.height>=h)
            {
                var inner=WorldArea;inner.xMin+=Margin*.5f;inner.yMin+=Margin*.5f;inner.xMax-=Margin*.5f;inner.yMax-=Margin*.5f;
                bool atLeft=area.xMin==0,atBottom=area.yMin==0,atRight=area.xMax==scene.Width,atTop=area.yMax==scene.Height;
                if((view.xMin>=inner.xMin || atLeft) && (view.yMin>=inner.yMin || atBottom) && (view.xMax<=inner.xMax || atRight) && (view.yMax<=inner.yMax || atTop)) return false;
            }
            int x=Mathf.FloorToInt((view.center.x-scene.WorldRect.x)/texel)-w/2,y=Mathf.FloorToInt((view.center.y-scene.WorldRect.y)/texel)-h/2;
            area=new RectInt(Mathf.Clamp(x,0,scene.Width-w),Mathf.Clamp(y,0,scene.Height-h),w,h);
            hasArea=true;
            return true;
        }
        private void Allocate(int width,int height)
        {
            LightTextureBudget.Release(front);LightTextureBudget.Release(back);LightTextureBudget.Release(mask);front=back=mask=null;
            front=Target(width,height,"Свет над камерой: актёры");back=Target(width,height,"Свет над камерой: фон с тенями");
            mask=Target(width,height,"Свет над камерой: маска тени");
            front.filterMode=back.filterMode=FilterMode.Bilinear;
            hasArea=false;hasSignature=false;
        }

        // A flash: the bright pixels of its sprite emit like a screen; reach FlashReach.
        private bool TryFlashSource(SpriteEmission emission,string id,out LevelLightSource source)
        {
            source=null;
            if(emission.Sprite==null || !float.IsFinite(emission.Gain) || emission.Gain<=0)
            {Debug.LogWarning($"Свет над камерой: некорректная вспышка {id}: спрайт {emission.Sprite}, сила {emission.Gain}.");return false;}
            var scene=light.Scene;
            var glow=LightSpriteShapes.MeasureGlow(emission.Sprite,FlashGlowThreshold);
            if(glow.Points.Length==0) return false;
            var bounds=emission.Sprite.bounds;var points=new Vector4[glow.Points.Length];
            for(int i=0;i<points.Length;i++)
            {
                var p=glow.Points[i];
                Vector2 world=emission.LocalToWorld.MultiplyPoint3x4(new Vector3(bounds.min.x+p.x*bounds.size.x,bounds.min.y+p.y*bounds.size.y,0));
                points[i]=new Vector4(world.x,world.y,p.z,0);
            }
            var scale=emission.LocalToWorld.lossyScale;
            double formArea=glow.AreaFraction*bounds.size.x*bounds.size.y*Mathf.Abs(scale.x*scale.y);
            Vector2 a=emission.LocalToWorld.MultiplyPoint3x4(bounds.min),b=emission.LocalToWorld.MultiplyPoint3x4(bounds.max);
            var form=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
            var texels=scene.Texels(Rect.MinMaxRect(form.xMin-FlashReach,form.yMin-FlashReach,form.xMax+FlashReach,form.yMax+FlashReach));
            if(texels.width<=0 || texels.height<=0 || texels.width>flash.width || texels.height>flash.height) return false;
            source=new LevelLightSource(id,LevelLightSourceKind.Screen,LevelLightSources.Normalise(points,id),glow.Color*emission.Gain,
                (float)LightRules.EmitterWidth(formArea,scene.Texel),FlashReach,form,texels,true);
            return true;
        }

        private void DrawMask(IReadOnlyList<ProjectedLightShadow> masks,string id)
        {
            var world=WorldArea;
            commands.Clear();commands.SetRenderTarget(mask);commands.ClearRenderTarget(false,true,Color.clear);
            commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(world.xMin,world.xMin+mask.width*light.Scene.Texel,world.yMin,world.yMin+mask.height*light.Scene.Texel,-1,1),false));
            if(masks!=null) foreach(var shadow in masks)
            {
                if(shadow.SourceId!=id) continue;
                if(shadow.Mask==null || !float.IsFinite(shadow.Opacity) || shadow.Opacity<0 || shadow.Opacity>1 || shadow.WorldBounds.width<=0 || shadow.WorldBounds.height<=0)
                    throw new ArgumentException($"Некорректная маска тени источника {id}.");
                block.Clear();block.SetTexture("_MainTex",shadow.Mask);block.SetVector("_SpriteUvRect",new Vector4(0,0,1,1));block.SetFloat("_OccluderOpacity",shadow.Opacity);
                var bounds=shadow.WorldBounds;
                commands.DrawMesh(quad,Matrix4x4.TRS(new Vector3(bounds.x,bounds.y,0),Quaternion.identity,new Vector3(bounds.width,bounds.height,1)),maskMaterial,0,ShadowMaskPass,block);
            }
            Graphics.ExecuteCommandBuffer(commands);
        }
        private void Apply(int kernel,Texture field,RectInt texels)
        {
            int x0=Math.Max(area.xMin,texels.xMin),y0=Math.Max(area.yMin,texels.yMin),x1=Math.Min(area.xMax,texels.xMax),y1=Math.Min(area.yMax,texels.yMax);
            if(x1<=x0 || y1<=y0) return;
            compute.SetTexture(kernel,"_Front",front);compute.SetTexture(kernel,"_Back",back);
            compute.SetTexture(kernel,"_FieldIn",field);compute.SetTexture(kernel,"_Mask",mask);
            compute.SetInts("_AreaOrigin",area.x,area.y);compute.SetInts("_FieldOrigin",texels.x,texels.y);compute.SetInts("_FieldSize",texels.width,texels.height);
            compute.SetInts("_Clip",x0-area.x,y0-area.y,x1-area.x,y1-area.y);
            compute.Dispatch(kernel,(x1-x0+7)/8,(y1-y0+7)/8,1);
        }

        private void Publish()
        {
            if(!shader.IsActive) return;
            var scene=light.Scene;var world=WorldArea;
            Shader.SetGlobalTexture("_RemainsRadiance",front);
            Shader.SetGlobalTexture("_RemainsBackgroundRadiance",back);
            Shader.SetGlobalVector("_RemainsRadianceRect",new Vector4(world.x,world.y,world.width,world.height));
            Shader.SetGlobalFloat("_RemainsRadianceTexel",scene.Texel);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);
            Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",1);
            var bounce=light.Bounce;
            Shader.SetGlobalTexture("_RemainsIndirect",bounce.Indirect);
            Shader.SetGlobalVector("_RemainsIndirectRect",new Vector4(scene.WorldRect.x,scene.WorldRect.y,bounce.Width*BounceLight.Divisor*scene.Texel,bounce.Height*BounceLight.Divisor*scene.Texel));
            Shader.SetGlobalFloat("_RemainsIndirectEnabled",1);
        }

        private static RenderTexture Target(int width,int height,string name)
        {
            var texture=new RenderTexture(width,height,0,GraphicsFormat.R16G16B16A16_SFloat) {name=name,enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            try
            {
                LightTextureBudget.Track(texture,LightTextureBudget.Size(width,height,8));
                if(!texture.Create()) throw new InvalidOperationException("Не создана текстура света "+name);
                return texture;
            }
            catch {LightTextureBudget.Release(texture);throw;}
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            if(shader.IsActive) {Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",0);Shader.SetGlobalFloat("_RemainsIndirectEnabled",0);}
            LightTextureBudget.Release(front);LightTextureBudget.Release(back);LightTextureBudget.Release(mask);LightTextureBudget.Release(flash);
            front=back=mask=flash=null;
            commands?.Release();commands=null;
            if(maskMaterial!=null) UnityEngine.Object.DestroyImmediate(maskMaterial);
            if(quad!=null) UnityEngine.Object.DestroyImmediate(quad);
        }
    }
}
