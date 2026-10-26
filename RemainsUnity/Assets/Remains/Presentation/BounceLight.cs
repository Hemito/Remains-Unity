using System;
using System.Collections;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Remains.Presentation
{
    // Bounce light of a level on a half grid (20 cm texels at 10/m): walls reflect the direct light several times
    // (BounceLight.compute; each face only to its own side). The whole level is computed once at load. An event
    // (a door, a recomposed region of direct light) is computed in a window around it twice — with the inputs before
    // and after the change — and only the difference is added to the level's light: the window's clipping is the
    // same in both runs and cancels, except for far light that passes through the changed place.
    public sealed class BounceLight : IDisposable
    {
        public const int Divisor=2;
        public const int Passes=8;
        public const int Margin=64;          // coarse texels (12.8 m) of context around a change
        public const int MaxWindow=512;      // coarse texels; a larger change is split into chunks
        private const string ComputeResource="Presentation/BounceLight";
        private readonly LevelLightScene scene;
        private readonly DirectLight direct;
        private ComputeShader compute;
        private int downsampleKernel,seedKernel,jumpKernel,distanceKernel,seedFacesKernel,cascadeKernel,fluenceKernel,bounceFacesKernel,accumulateKernel;
        private RenderTexture coarseScene,coarseDirect,indirect;
        private Transport window;
        private readonly List<RectInt> changes=new();     // coarse rectangles not yet taken into windows
        private readonly Queue<RectInt> pending=new();     // chunks of the changes, one window each
        // Windows use the cascade levels of the whole level and start on its top probe grid, so a window places
        // its probes and rays exactly where the whole-level computation does; only rays leaving it differ.
        private readonly int levels,alignment;
        private IEnumerator work;
        private bool built,disposed;
        public int Width {get;}
        public int Height {get;}
        public Texture Indirect=>indirect;
        public bool HasWork=>work!=null || pending.Count>0 || changes.Count>0;
        public int Windows {get;private set;}
        public int Dispatches {get;private set;}
        public long Bytes=>LightTextureBudget.Size(Width,Height,2+8+16)+(window?.Bytes??0);

        public BounceLight(LevelLightScene scene,DirectLight direct,float albedo)
        {
            if(!(albedo>=0 && albedo<1)) throw new ArgumentOutOfRangeException(nameof(albedo));
            this.albedo=albedo;
            this.scene=scene??throw new ArgumentNullException(nameof(scene));
            this.direct=direct??throw new ArgumentNullException(nameof(direct));
            Width=(scene.Width+Divisor-1)/Divisor;Height=(scene.Height+Divisor-1)/Divisor;
            levels=Transport.Levels(Width,Height);alignment=1<<(levels-1);
            try
            {
                compute=GameAssets.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource);
                downsampleKernel=compute.FindKernel("Downsample");seedKernel=compute.FindKernel("Seed");jumpKernel=compute.FindKernel("Jump");
                distanceKernel=compute.FindKernel("Distance");seedFacesKernel=compute.FindKernel("SeedFaces");cascadeKernel=compute.FindKernel("Cascade");
                fluenceKernel=compute.FindKernel("Fluence");bounceFacesKernel=compute.FindKernel("BounceFaces");accumulateKernel=compute.FindKernel("Accumulate");
                coarseScene=Target(Width,Height,GraphicsFormat.R16_SFloat,"Отражения: поглощение 1/2");
                coarseDirect=Target(Width,Height,GraphicsFormat.R16G16B16A16_SFloat,"Отражения: прямой свет 1/2");
                indirect=Target(Width,Height,GraphicsFormat.R32G32B32A32_SFloat,"Отражения уровня");
                indirect.filterMode=FilterMode.Bilinear;
            }
            catch {Dispose();throw;}
        }

        // The whole level from zero, one transport stage per MoveNext; the whole-level work buffers exist only meanwhile.
        public IEnumerator BuildAllSteps()
        {
            ThrowIfDisposed();
            pending.Clear();changes.Clear();work=null;
            Downsample(new RectInt(0,0,Width,Height));
            var whole=new Transport(this,Width,Height,levels,"уровень");
            try
            {
                var run=whole.Run(Vector2Int.zero,new Vector2Int(Width,Height),keepAsOld:false);
                while(run.MoveNext()) yield return null;
                ClearIndirect();
                whole.Accumulate(indirect,Vector2Int.zero,new Vector2Int(Width,Height),hasOld:false);
            }
            finally {whole.Dispose();}
            built=true;
        }
        public void BuildAll()
        {
            var steps=BuildAllSteps();
            try {while(steps.MoveNext()) {}} finally {(steps as IDisposable)?.Dispose();}
        }

        // A changed rectangle of the level (fine texels): absorption (a door) or direct light (a recomposed region).
        public void Changed(RectInt fine)
        {
            ThrowIfDisposed();
            if(fine.width<=0 || fine.height<=0) return;
            int x0=fine.xMin/Divisor,y0=fine.yMin/Divisor,x1=(fine.xMax+Divisor-1)/Divisor,y1=(fine.yMax+Divisor-1)/Divisor;
            changes.Add(new RectInt(x0,y0,Math.Min(Width,x1)-x0,Math.Min(Height,y1)-y0));
        }
        // The changes of one event lie close together: their union is split into chunks whose aligned window with
        // the margin fits MaxWindow; chunks that touch no change are skipped.
        private void TakeChanges()
        {
            int x0=int.MaxValue,y0=int.MaxValue,x1=0,y1=0;
            foreach(var c in changes) {x0=Math.Min(x0,c.xMin);y0=Math.Min(y0,c.yMin);x1=Math.Max(x1,c.xMax);y1=Math.Max(y1,c.yMax);}
            int chunk=MaxWindow-2*Margin-2*alignment;
            for(int y=y0;y<y1;y+=chunk)
                for(int x=x0;x<x1;x+=chunk)
                {
                    var part=new RectInt(x,y,Math.Min(chunk,x1-x),Math.Min(chunk,y1-y));
                    foreach(var c in changes) if(c.Overlaps(part)) {pending.Enqueue(part);break;}
                }
            changes.Clear();
        }

        // Advances queued windows by at most `stages` transport stages (one stage ≈ one pass over the window).
        // Waits while direct light still has work: inputs are read when they are final.
        public bool Step(int stages)
        {
            ThrowIfDisposed();
            if(!built) throw new InvalidOperationException("Отражения: шаг события до полной сборки уровня.");
            for(int i=0;i<stages;i++)
            {
                if(work==null)
                {
                    if(direct.HasWork) break;
                    if(pending.Count==0 && changes.Count>0) TakeChanges();
                    if(pending.Count==0) break;
                    work=Process(pending.Dequeue());
                }
                if(!work.MoveNext()) {(work as IDisposable)?.Dispose();work=null;}
            }
            return HasWork;
        }

        private IEnumerator Process(RectInt change)
        {
            window??=new Transport(this,MaxWindow,MaxWindow,levels,"окно события");
            int x0=Math.Max(0,(change.xMin-Margin)/alignment*alignment),y0=Math.Max(0,(change.yMin-Margin)/alignment*alignment);
            int x1=Math.Min(Width,(change.xMax+Margin+alignment-1)/alignment*alignment),y1=Math.Min(Height,(change.yMax+Margin+alignment-1)/alignment*alignment);
            var origin=new Vector2Int(x0,y0);var size=new Vector2Int(x1-x0,y1-y0);
            var before=window.Run(origin,size,keepAsOld:true);
            while(before.MoveNext()) yield return null;
            Downsample(change);
            var after=window.Run(origin,size,keepAsOld:false);
            while(after.MoveNext()) yield return null;
            window.Accumulate(indirect,origin,size,hasOld:true);
            Windows++;
        }

        private void Downsample(RectInt coarse)
        {
            compute.SetTexture(downsampleKernel,"_FineScene",scene.Texture);compute.SetTexture(downsampleKernel,"_FineDirect",direct.Sum);
            compute.SetTexture(downsampleKernel,"_CoarseSceneOut",coarseScene);compute.SetTexture(downsampleKernel,"_CoarseDirectOut",coarseDirect);
            compute.SetInts("_FineSize",scene.Width,scene.Height);compute.SetInt("_Divisor",Divisor);
            compute.SetInts("_Clip",coarse.xMin,coarse.yMin,coarse.xMax,coarse.yMax);
            Dispatch(downsampleKernel,coarse.width,coarse.height);
        }
        private void ClearIndirect()
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=indirect;GL.Clear(false,true,Color.clear);}
            finally {RenderTexture.active=previous;}
        }
        private void Dispatch(int kernel,int width,int height)
        {
            if(width<=0 || height<=0) return;
            compute.Dispatch(kernel,(width+7)/8,(height+7)/8,1);Dispatches++;
        }

        // Work buffers of one window size: transport of the coarse inputs copied in, eight reflection generations.
        private sealed class Transport : IDisposable
        {
            private readonly BounceLight owner;
            private readonly int capacityWidth,capacityHeight,levels;
            private readonly List<RenderTexture> owned=new();
            private readonly RenderTexture scene,direct,seedsA,seedsB,distance,cascadeA,cascadeB,fluence,old;
            private readonly RenderTexture[] facesA=new RenderTexture[4],facesB=new RenderTexture[4];
            public long Bytes {get;private set;}

            public Transport(BounceLight owner,int width,int height,int levels,string purpose)
            {
                this.owner=owner;capacityWidth=width;capacityHeight=height;this.levels=levels;
                try
                {
                    scene=Make(width,height,GraphicsFormat.R16_SFloat,"поглощение");
                    direct=Make(width,height,GraphicsFormat.R16G16B16A16_SFloat,"прямой свет");
                    seedsA=Make(width,height,GraphicsFormat.R32G32_SFloat,"стена A");seedsB=Make(width,height,GraphicsFormat.R32G32_SFloat,"стена B");
                    distance=Make(width,height,GraphicsFormat.R32_SFloat,"расстояние до стены");
                    int cw=2*width+(2<<levels),ch=2*height+(2<<levels);
                    cascadeA=Make(cw,ch,GraphicsFormat.R16G16B16A16_SFloat,"каскад A");cascadeB=Make(cw,ch,GraphicsFormat.R16G16B16A16_SFloat,"каскад B");
                    fluence=Make(width,height,GraphicsFormat.R16G16B16A16_SFloat,"результат");
                    old=Make(width,height,GraphicsFormat.R16G16B16A16_SFloat,"результат до события");
                    for(int i=0;i<4;i++)
                    {
                        facesA[i]=Make(width,height,GraphicsFormat.B10G11R11_UFloatPack32,"грани A"+i);
                        facesB[i]=Make(width,height,GraphicsFormat.B10G11R11_UFloatPack32,"грани B"+i);
                    }
                }
                catch {Dispose();throw;}

                RenderTexture Make(int w,int h,GraphicsFormat format,string name)
                {
                    int bytes=format==GraphicsFormat.R16_SFloat?2:format==GraphicsFormat.R32_SFloat || format==GraphicsFormat.B10G11R11_UFloatPack32?4:8;
                    var texture=Target(w,h,format,$"Отражения ({purpose}): {name}");
                    owned.Add(texture);Bytes+=LightTextureBudget.Size(w,h,bytes);return texture;
                }
            }
            public static int Levels(int width,int height)
            {
                float diagonal=Mathf.Sqrt((float)width*width+(float)height*height);int levels=1;
                while((Mathf.Pow(4,levels)-1)/3<diagonal) levels++;
                return levels;
            }

            // One window of the level; the result stays in `fluence` (or `old` when keepAsOld).
            public IEnumerator Run(Vector2Int origin,Vector2Int size,bool keepAsOld)
            {
                if(size.x>capacityWidth || size.y>capacityHeight) throw new ArgumentException($"Окно отражений {size} больше буферов {capacityWidth}×{capacityHeight}.");
                var c=owner.compute;int w=size.x,h=size.y;
                Graphics.CopyTexture(owner.coarseScene,0,0,origin.x,origin.y,w,h,scene,0,0,0,0);
                Graphics.CopyTexture(owner.coarseDirect,0,0,origin.x,origin.y,w,h,direct,0,0,0,0);
                c.SetInts("_Size",w,h);c.SetFloat("_Albedo",owner.albedo);
                c.SetTexture(owner.seedKernel,"_Scene",scene);c.SetTexture(owner.seedKernel,"_SeedsOut",seedsA);
                owner.Dispatch(owner.seedKernel,w,h);
                var source=seedsA;var target=seedsB;
                for(int step=Mathf.NextPowerOfTwo(Mathf.Max(w,h))/2;step>=1;step/=2)
                {
                    c.SetInt("_Step",step);c.SetTexture(owner.jumpKernel,"_SeedsIn",source);c.SetTexture(owner.jumpKernel,"_SeedsOut",target);
                    owner.Dispatch(owner.jumpKernel,w,h);
                    (source,target)=(target,source);
                }
                c.SetTexture(owner.distanceKernel,"_SeedsFinal",source);c.SetTexture(owner.distanceKernel,"_DistanceOut",distance);
                owner.Dispatch(owner.distanceKernel,w,h);
                Faces(owner.seedFacesKernel,facesA);
                yield return null;
                var faces=facesA;var next=facesB;
                for(int pass=0;pass<Passes;pass++)
                {
                    c.SetInts("_Size",w,h);c.SetInt("_Top",levels-1);
                    RenderTexture upper=cascadeB,current=cascadeA;
                    for(int level=levels-1;level>=0;level--)
                    {
                        c.SetInt("_Level",level);
                        c.SetTexture(owner.cascadeKernel,"_Scene",scene);c.SetTexture(owner.cascadeKernel,"_Distance",distance);
                        c.SetTexture(owner.cascadeKernel,"_Upper",upper);c.SetTexture(owner.cascadeKernel,"_Cascade",current);
                        for(int i=0;i<4;i++) c.SetTexture(owner.cascadeKernel,"_FaceIn"+i,faces[i]);
                        int spacing=1<<level,block=2<<level;
                        owner.Dispatch(owner.cascadeKernel,(w+spacing-1)/spacing*block,(h+spacing-1)/spacing*block);
                        (upper,current)=(current,upper);
                    }
                    var result=pass==Passes-1 && keepAsOld?old:fluence;
                    c.SetTexture(owner.fluenceKernel,"_Cascade0",upper);c.SetTexture(owner.fluenceKernel,"_Fluence",result);
                    owner.Dispatch(owner.fluenceKernel,w,h);
                    if(pass<Passes-1)
                    {
                        c.SetTexture(owner.bounceFacesKernel,"_Cascade0",upper);
                        Faces(owner.bounceFacesKernel,next);
                        (faces,next)=(next,faces);
                    }
                    yield return null;
                }

                void Faces(int kernel,RenderTexture[] output)
                {
                    c.SetTexture(kernel,"_Scene",scene);c.SetTexture(kernel,"_Direct",direct);
                    for(int i=0;i<4;i++) c.SetTexture(kernel,"_FaceOut"+i,output[i]);
                    owner.Dispatch(kernel,w,h);
                }
            }
            public void Accumulate(RenderTexture level,Vector2Int origin,Vector2Int size,bool hasOld)
            {
                var c=owner.compute;
                c.SetInts("_Size",size.x,size.y);c.SetInts("_Origin",origin.x,origin.y);c.SetInt("_HasOld",hasOld?1:0);
                c.SetTexture(owner.accumulateKernel,"_New",fluence);c.SetTexture(owner.accumulateKernel,"_Old",old);
                c.SetTexture(owner.accumulateKernel,"_Indirect",level);
                owner.Dispatch(owner.accumulateKernel,size.x,size.y);
            }
            public void Dispose()
            {
                foreach(var texture in owned) LightTextureBudget.Release(texture);
                owned.Clear();
            }
        }

        private readonly float albedo;
        public float Albedo=>albedo;

        private static RenderTexture Target(int width,int height,GraphicsFormat format,string name)
        {
            var texture=new RenderTexture(width,height,0,format) {name=name,enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            try
            {
                int bytes=format==GraphicsFormat.R16_SFloat?2:format==GraphicsFormat.R32_SFloat || format==GraphicsFormat.B10G11R11_UFloatPack32?4:format==GraphicsFormat.R32G32B32A32_SFloat?16:8;
                LightTextureBudget.Track(texture,LightTextureBudget.Size(width,height,bytes));
                if(!texture.Create()) throw new InvalidOperationException("Не создана текстура отражений "+name);
                return texture;
            }
            catch {LightTextureBudget.Release(texture);throw;}
        }
        private void ThrowIfDisposed() {if(disposed) throw new ObjectDisposedException(nameof(BounceLight));}
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;(work as IDisposable)?.Dispose();work=null;
            window?.Dispose();window=null;
            LightTextureBudget.Release(coarseScene);LightTextureBudget.Release(coarseDirect);LightTextureBudget.Release(indirect);
            coarseScene=coarseDirect=indirect=null;
        }
    }
}
