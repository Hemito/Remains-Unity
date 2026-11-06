using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // What the shaders read of a level light: two textures over the camera area with a margin (_RemainsRadiance for
    // actors and props, _RemainsBackgroundRadiance for walls with actor shadows). The accepted polar mode
    // resolves the raw direct light against the cached world fill after per-source shadows and flashes.
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
        private readonly PolarDirectLight polar;
        private readonly PolarLightWindow detailed;
        private readonly WorldLightFields worldFields;
        public int WorldLightBuilds=>worldFields?.Builds??0;
        public long WorldLightBytes=>worldFields?.Bytes??0;
        public const int DetailScale=8; // 1.25cm output texels; scene/transport stay at 10cm.
        private int Scale=>polar==null?1:DetailScale;
        private float ViewMargin=>polar==null?Margin:2;
        public int DetailBuilds=>detailed?.Builds??0;
        public int DetailSourceUpdates=>detailed?.SourceUpdates??0;
        private ComputeShader compute;
        private int subtractKernel,addFlashKernel,resolveKernel,copyKernel,copyDetailedKernel,resolveDetailedKernel,worldLightKernel;
        private RenderTexture front,back,mask,flash,staticFront,staticBack;
        private Material maskMaterial;
        private Mesh quad;
        private CommandBuffer commands;
        private readonly MaterialPropertyBlock block=new();
        private RectInt area;
        private bool hasArea,hasSignature,flashShown,hasFlashBaseline,disposed;
        private int signature;
        private float fillGain=.08f;
        private readonly HashSet<string> composed=new(StringComparer.Ordinal),warned=new(StringComparer.Ordinal);
        public RectInt Area=>area;
        public Rect WorldArea=>hasArea?new Rect(light.Scene.WorldRect.x+area.x*light.Scene.Texel,light.Scene.WorldRect.y+area.y*light.Scene.Texel,area.width*light.Scene.Texel,area.height*light.Scene.Texel):default;
        public int Compositions {get;private set;}
        public float FillGain
        {
            get=>fillGain;
            set
            {
                if(!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value),"Свет над камерой: коэффициент заполнения должен быть конечным.");
                float next=Mathf.Clamp01(value);
                if(fillGain==next)return;
                fillGain=next;hasSignature=false;
            }
        }

        public LevelLightView(LevelLight light,LightShaderContext shader)
        {
            this.light=light??throw new ArgumentNullException(nameof(light));
            this.shader=shader??throw new ArgumentNullException(nameof(shader));
            polar=light.Direct as PolarDirectLight;
            if(polar!=null){detailed=new PolarLightWindow(polar);worldFields=new WorldLightFields(light,Scale);}
            try
            {
                compute=GameAssets.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource);
                subtractKernel=compute.FindKernel("SubtractField");addFlashKernel=compute.FindKernel("AddFlash");resolveKernel=compute.FindKernel("ResolveGridFill");
                copyKernel=compute.FindKernel("CopyDirect");copyDetailedKernel=compute.FindKernel("CopyDetailed");resolveDetailedKernel=compute.FindKernel("ResolveDetailed");worldLightKernel=compute.FindKernel("AddWorldLight");
                maskMaterial=new Material(GameAssets.Load<Shader>(SceneShaderResource)??throw new InvalidOperationException("Нет шейдера "+SceneShaderResource)) {name="Маска тени над камерой",hideFlags=HideFlags.HideAndDontSave};
                quad=LightSpriteShapes.UnitQuad("Квадрат маски тени");
                commands=new CommandBuffer {name="Свет над камерой: маска тени"};
                int reach=Mathf.CeilToInt(2*FlashReach*light.Scene.Resolution)+64;
                flash=Target(reach,reach,"Свет над камерой: вспышка");
            }
            catch {Dispose();throw;}
        }

        // view: the camera's visible world rectangle. Composes when something it shows changed; publishes the globals.
        public void Present(Rect view,IReadOnlyList<SpriteEmission> flashes,IReadOnlyList<ProjectedLightShadow> masks,IReadOnlyList<string> flashIds,IReadOnlyList<WorldLightEmission> worldLights=null)
        {
            if(disposed) throw new ObjectDisposedException(nameof(LevelLightView));
            int flashCount=flashes?.Count??0;
            if(worldLights?.Count>0 && worldFields==null)throw new InvalidOperationException("Динамическое освещение окружения требует полярный backend.");
            if(flashCount>0 && (flashIds==null || flashIds.Count!=flashCount)) throw new ArgumentException("Количество ID источников не совпадает с количеством вспышек.");
            // An event is published atomically: never combine a new direct sum with the preceding fill state.
            // Keep the already resolved camera buffers until the queued direct/fill stages have all finished.
            if(light.UsesGridFill && light.HasPendingPublication)
            {
                // A one-frame flash must not persist for the duration of an unrelated queued door/light event.
                // Its baseline was resolved with the same direct/fill revision before the flash was added.
                if(flashShown && hasFlashBaseline)
                {
                    Graphics.CopyTexture(staticFront,front);Graphics.CopyTexture(staticBack,back);
                    flashShown=false;hasSignature=false;
                }
                if(Compositions>0)Publish();
                return;
            }
            bool moved=Place(view);
            var hash=new HashCode();hash.Add(area);hash.Add(light.Direct.Revision);
            if(light.UsesGridFill){hash.Add(light.Fill.Revision);hash.Add(fillGain);}
            hash.Add(worldFields?.GeometryRevision??0);
            if(masks!=null) foreach(var m in masks)
            {hash.Add(m.SourceId);hash.Add(m.Mask!=null?m.Mask.GetInstanceID():0);hash.Add(m.WorldBounds);hash.Add(m.Opacity);hash.Add(m.Revision);}
            if(worldLights!=null)foreach(var e in worldLights){hash.Add(e.GeometrySignature());hash.Add(e.Gain);}
            int next=hash.ToHashCode();
            if(!moved && flashCount==0 && !flashShown && hasSignature && next==signature){Publish();return;}
            Remains.Diagnostics.FrameHitchLog.Gpu("Свет: камера (тени и вспышки)");
            using var section=Remains.Diagnostics.FrameHitchLog.Section("Свет: камера (тени и вспышки)");
            if(polar!=null)detailed.Prepare(area,WorldArea,front.width,front.height);
            var sum=polar==null?light.Direct.Sum:detailed.Sum;
            // Explicit format conversion: a FP32 source sum cannot use CopyTexture into the FP16 camera buffers.
            int copy=polar==null?copyKernel:copyDetailedKernel;
            compute.SetTexture(copy,"_DirectIn",sum);compute.SetTexture(copy,"_Front",front);compute.SetTexture(copy,"_Back",back);
            compute.SetInts("_AreaOrigin",area.x,area.y);compute.SetInts("_AreaSize",area.width,area.height);compute.SetInts("_OutputSize",front.width,front.height);
            compute.Dispatch(copy,(front.width+7)/8,(front.height+7)/8,1);
            composed.Clear();
            if(masks!=null) foreach(var m in masks)
            {
                if(composed.Contains(m.SourceId) || IsFlash(m.SourceId,flashIds)) continue;
                composed.Add(m.SourceId);
                if(polar!=null)
                {
                    if(!light.Sources.TryGet(m.SourceId,out int index) || !light.Sources.All[index].Enabled)continue;
                    DrawMask(masks,m.SourceId);
                    polar.DrawDetail(index,back,WorldArea,2,mask,MaskBounds(masks,m.SourceId));
                    continue;
                }
                if(!light.Direct.TryGetField(m.SourceId,out var field,out var texels))
                {
                    if(light.Sources.TryGet(m.SourceId,out int sourceIndex) && light.Sources.All[sourceIndex].Enabled && warned.Add(m.SourceId))
                        Debug.LogWarning($"Свет над камерой: прямой свет {m.SourceId} ещё не построен; тень от него пропущена до подготовки поля.");
                    continue;
                }
                DrawMask(masks,m.SourceId);
                Apply(subtractKernel,field,texels);
            }
            if(worldFields!=null)
            {
                worldFields.Begin();
                if(worldLights!=null)foreach(var e in worldLights)
                {
                    if(e.Gain<=0 || !WorldArea.Overlaps(Rect.MinMaxRect(e.Bounds.xMin-e.Reach,e.Bounds.yMin-e.Reach,e.Bounds.xMax+e.Reach,e.Bounds.yMax+e.Reach)))continue;
                    var field=worldFields.Get(e);if(field==null)continue;
                    DrawMask(masks,e.Id);AddWorldLight(field,e.Gain);
                }
                worldFields.End();
            }
            if(light.UsesGridFill && flashCount>0)CaptureFlashBaseline();
            for(int i=0;i<flashCount;i++)
            {
                if(!TryFlashSource(flashes[i],flashIds[i],out var source)) continue;
                DrawMask(masks,flashIds[i]);
                if(polar!=null)polar.DrawDetailTransient(source,front,back,WorldArea,mask);
                else {light.Direct.BuildTransient(source,flash);Apply(addFlashKernel,flash,source.Texels);}
            }
            if(light.UsesGridFill)ResolveGridFill();
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
            int w=Mathf.Min(scene.Width,Mathf.CeilToInt((view.width+2*ViewMargin)/texel)+2),h=Mathf.Min(scene.Height,Mathf.CeilToInt((view.height+2*ViewMargin)/texel)+2);
            if(front==null || front.width<w*Scale || front.height<h*Scale) Allocate(Math.Max(w,front==null?0:front.width/Scale),Math.Max(h,front==null?0:front.height/Scale));
            w=front.width/Scale;h=front.height/Scale;   // the area fills the textures: the published rectangle has no stale border
            if(hasArea && area.width>=w && area.height>=h)
            {
                var inner=WorldArea;inner.xMin+=ViewMargin*.5f;inner.yMin+=ViewMargin*.5f;inner.xMax-=ViewMargin*.5f;inner.yMax-=ViewMargin*.5f;
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
            width*=Scale;height*=Scale;
            LightTextureBudget.Release(front);LightTextureBudget.Release(back);LightTextureBudget.Release(mask);front=back=mask=null;
            LightTextureBudget.Release(staticFront);LightTextureBudget.Release(staticBack);staticFront=staticBack=null;hasFlashBaseline=false;
            front=Target(width,height,"Свет над камерой: актёры");back=Target(width,height,"Свет над камерой: фон с тенями",polar!=null);
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
            double formArea=glow.AreaFraction*bounds.size.x*bounds.size.y*LightEmissionGeometry.AreaScale(emission.LocalToWorld);
            var form=LightEmissionGeometry.WorldBounds(emission.LocalToWorld,bounds);
            var texels=scene.Texels(Rect.MinMaxRect(form.xMin-FlashReach,form.yMin-FlashReach,form.xMax+FlashReach,form.yMax+FlashReach));
            if(texels.width<=0 || texels.height<=0 || texels.width>flash.width || texels.height>flash.height) return false;
            source=new LevelLightSource(id,LevelLightSourceKind.Screen,LevelLightSources.Normalise(points,id),glow.Color*emission.Gain,
                (float)LightRules.EmitterWidth(formArea,scene.Texel),FlashReach,form,texels,true);
            return true;
        }

        private static Rect MaskBounds(IReadOnlyList<ProjectedLightShadow> masks,string id)
        {
            bool found=false;Rect result=default;
            foreach(var m in masks)if(m.SourceId==id)
            {var b=m.WorldBounds;result=found?Rect.MinMaxRect(Mathf.Min(result.xMin,b.xMin),Mathf.Min(result.yMin,b.yMin),Mathf.Max(result.xMax,b.xMax),Mathf.Max(result.yMax,b.yMax)):b;found=true;}
            return result;
        }
        private void DrawMask(IReadOnlyList<ProjectedLightShadow> masks,string id)
        {
            var world=WorldArea;
            commands.Clear();commands.SetRenderTarget(mask);commands.ClearRenderTarget(false,true,Color.clear);
            commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(world.xMin,world.xMax,world.yMin,world.yMax,-1,1),false));
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

        private void AddWorldLight(WorldLightFields.Field field,float gain)
        {
            var world=WorldArea;var b=field.World;float sx=front.width/world.width,sy=front.height/world.height;
            int x0=Mathf.Clamp(Mathf.FloorToInt((b.xMin-world.xMin)*sx),0,front.width),y0=Mathf.Clamp(Mathf.FloorToInt((b.yMin-world.yMin)*sy),0,front.height);
            int x1=Mathf.Clamp(Mathf.CeilToInt((b.xMax-world.xMin)*sx),0,front.width),y1=Mathf.Clamp(Mathf.CeilToInt((b.yMax-world.yMin)*sy),0,front.height);
            if(x1<=x0 || y1<=y0)return;int kernel=worldLightKernel;
            compute.SetTexture(kernel,"_Front",front);compute.SetTexture(kernel,"_Back",back);compute.SetTexture(kernel,"_Mask",mask);compute.SetTexture(kernel,"_FieldIn",field.Texture);
            compute.SetInts("_OutputSize",front.width,front.height);compute.SetInts("_Clip",x0,y0,x1,y1);
            compute.SetVector("_OutputRect",new Vector4(world.x,world.y,world.width,world.height));compute.SetVector("_DynamicRect",new Vector4(b.x,b.y,b.width,b.height));compute.SetFloat("_DynamicGain",gain);
            compute.Dispatch(kernel,(x1-x0+7)/8,(y1-y0+7)/8,1);
        }
        private void CaptureFlashBaseline()
        {
            if(staticFront==null)
            {
                staticFront=Target(front.width,front.height,"Свет над камерой: актёры до вспышки");
                staticBack=Target(back.width,back.height,"Свет над камерой: фон до вспышки",polar!=null);
                staticFront.filterMode=staticBack.filterMode=FilterMode.Bilinear;
            }
            Graphics.CopyTexture(front,staticFront);Graphics.CopyTexture(back,staticBack);
            ResolveGridFill(staticFront,staticBack);hasFlashBaseline=true;
        }

        private void ResolveGridFill()=>ResolveGridFill(front,back);
        private void ResolveGridFill(RenderTexture targetFront,RenderTexture targetBack)
        {
            var fill=light.Fill.Texture;
            if(fill==null || fill.width!=light.Scene.Width || fill.height!=light.Scene.Height)
                throw new InvalidOperationException($"Свет над камерой: заполнение не совпадает с fine-сценой {light.Scene.Width}×{light.Scene.Height}, поле={fill}.");
            int resolve=polar==null?resolveKernel:resolveDetailedKernel;
            compute.SetTexture(resolve,"_Front",targetFront);compute.SetTexture(resolve,"_Back",targetBack);
            var world=WorldArea;var scene=light.Scene.WorldRect;
            compute.SetInts("_OutputSize",targetFront.width,targetFront.height);
            compute.SetVector("_OutputRect",new Vector4(world.x,world.y,world.width,world.height));
            compute.SetVector("_WorldRect",new Vector4(scene.x,scene.y,scene.width,scene.height));
            compute.SetTexture(resolve,"_Fill",fill);compute.SetInts("_AreaOrigin",area.x,area.y);
            compute.SetInts("_AreaSize",area.width,area.height);compute.SetFloat("_FillGain",fillGain);
            compute.Dispatch(resolve,(targetFront.width+7)/8,(targetFront.height+7)/8,1);
        }

        private void Publish()
        {
            if(!shader.IsActive) return;
            var scene=light.Scene;var world=WorldArea;
            Shader.SetGlobalTexture("_RemainsRadiance",front);
            Shader.SetGlobalTexture("_RemainsBackgroundRadiance",back);
            Shader.SetGlobalVector("_RemainsRadianceRect",new Vector4(world.x,world.y,world.width,world.height));
            Shader.SetGlobalFloat("_RemainsRadianceTexel",scene.Texel/Scale);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);
            Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",1);
            if(light.UsesGridFill)Shader.SetGlobalFloat("_RemainsIndirectEnabled",0);
            else
            {
                var bounce=light.Bounce;
                Shader.SetGlobalTexture("_RemainsIndirect",bounce.Indirect);
                Shader.SetGlobalVector("_RemainsIndirectRect",new Vector4(scene.WorldRect.x,scene.WorldRect.y,bounce.Width*BounceLight.Divisor*scene.Texel,bounce.Height*BounceLight.Divisor*scene.Texel));
                Shader.SetGlobalFloat("_RemainsIndirectEnabled",1);
            }
        }

        private static RenderTexture Target(int width,int height,string name,bool fullPrecision=false)
        {
            var texture=new RenderTexture(width,height,0,fullPrecision?GraphicsFormat.R32G32B32A32_SFloat:GraphicsFormat.R16G16B16A16_SFloat) {name=name,enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            try
            {
                LightTextureBudget.Track(texture,LightTextureBudget.Size(width,height,fullPrecision?16:8));
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
            detailed?.Dispose();worldFields?.Dispose();
            LightTextureBudget.Release(front);LightTextureBudget.Release(back);LightTextureBudget.Release(mask);LightTextureBudget.Release(flash);
            LightTextureBudget.Release(staticFront);LightTextureBudget.Release(staticBack);staticFront=staticBack=null;hasFlashBaseline=false;
            front=back=mask=flash=null;
            commands?.Release();commands=null;
            if(maskMaterial!=null) UnityEngine.Object.DestroyImmediate(maskMaterial);
            if(quad!=null) UnityEngine.Object.DestroyImmediate(quad);
        }
    }
}
