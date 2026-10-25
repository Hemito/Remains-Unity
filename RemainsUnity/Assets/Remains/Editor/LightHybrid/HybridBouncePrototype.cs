using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Remains.Content;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Remains.Editor
{
    // Decision prototype only. Existing transport kernels run on a conservative whole-level grid;
    // only the new first-bounce source and its constant reinjection are different from the reference.
    public sealed class HybridBouncePrototype : IDisposable
    {
        public const string ComputePath="Assets/Remains/Editor/LightHybrid/HybridBounce.compute";
        private RoomRadianceField field;
        private RenderTexture coarse,seed;
        private ComputeShader prepare;
        private bool disposed;
        public Texture Result=>field==null?null:Get<RenderTexture>(field,"fluence");
        public Texture CoarseScene=>coarse;
        public Texture FirstBounceSeed=>seed;
        public int Width {get;}
        public int Height {get;}
        public int Divisor {get;}
        public int CascadeLevels {get;}
        public long Bytes {get;private set;}
        // Submission + a blocking 1-pixel GPU fence. Not hardware timestamp time.
        public double FenceMilliseconds {get;private set;}
        public const int Passes=8;

        [Serializable] public sealed class IsolatedSeedLeak
        {
            public bool found;
            public string orientation,detail;
            public int sourceX,sourceY,sideASamples,sideBSamples;
            public Vector2 world,sideAWorld,sideBWorld;
            public Color source,sideA,sideB;
            public double fenceMilliseconds;
        }

        // Destructive diagnostic: call only after all regular captures using Result, immediately before Dispose.
        // With one illuminated cell on an otherwise zero-emission wall, a single transport pass cannot include
        // another wall reflection. Nonzero fluence on both sides exposes the non-directional first-bounce seed
        // (and the existing cascade interpolation), rather than unrelated scene sources or repeated bounce.
        public IsolatedSeedLeak EvaluateIsolatedSeedLeak()
        {
            if(disposed || field==null) throw new ObjectDisposedException(nameof(HybridBouncePrototype));
            var absorption=Read(coarse);var seeds=Read(seed);
            var result=new IsolatedSeedLeak {detail="Не найден освещённый однотексельный барьер с открытыми texel по обе стороны."};
            float best=1e-5f;bool vertical=false;int sx=-1,sy=-1;
            for(int y=2;y<Height-2;y++)for(int x=2;x<Width-2;x++)
            {
                int i=y*Width+x;float strength=Maximum(seeds[i]);
                if(absorption[i].a<.99f || strength<=best) continue;
                bool wallVertical=absorption[i-1].a<.99f && absorption[i+1].a<.99f &&
                    absorption[i-Width].a>=.99f && absorption[i+Width].a>=.99f;
                bool wallHorizontal=absorption[i-Width].a<.99f && absorption[i+Width].a<.99f &&
                    absorption[i-1].a>=.99f && absorption[i+1].a>=.99f;
                if(!wallVertical && !wallHorizontal) continue;
                sx=x;sy=y;vertical=wallVertical;best=strength;
            }
            if(sx<0) return result;
            var bounce=Get<RenderTexture>(field,"bounceA");
            var watch=Stopwatch.StartNew();
            Clear(bounce);Clear(Get<RenderTexture>(field,"bounceB"));
            Graphics.CopyTexture(seed,0,0,sx,sy,1,1,bounce,0,0,sx,sy);
            field.RenderReferenceForChecks();
            Fence((RenderTexture)Result);watch.Stop();
            var output=Read((RenderTexture)Result);var rect=Get<Vector4>(field,"rect");
            result.found=true;result.sourceX=sx;result.sourceY=sy;result.world=World(sx,sy);
            result.source=seeds[sy*Width+sx];result.orientation=vertical?"vertical":"horizontal";
            result.fenceMilliseconds=watch.Elapsed.TotalMilliseconds;
            result.detail="Единственный ненулевой источник отражения на реальном coarse-барьере; один исходный transport pass, без повторных отражений. Значения сторон — максимумы открытых texel вдоль полосы длиной пять texel. Это выявляет двустороннюю изотропную инъекцию и интерполяцию каскадов, не измеряет вклад прямого света.";
            for(int side=-1;side<=1;side+=2)for(int offset=-2;offset<=2;offset++)
            {
                int x=sx+(vertical?side:offset),y=sy+(vertical?offset:side),index=y*Width+x;
                if(absorption[index].a>=.99f) continue;
                if(side<0)
                {
                    result.sideASamples++;
                    if(Maximum(output[index])>Maximum(result.sideA)) {result.sideA=output[index];result.sideAWorld=World(x,y);}
                }
                else
                {
                    result.sideBSamples++;
                    if(Maximum(output[index])>Maximum(result.sideB)) {result.sideB=output[index];result.sideBWorld=World(x,y);}
                }
            }
            return result;
            Vector2 World(int x,int y)=>new(rect.x+(x+.5f)*rect.z/Width,rect.y+(y+.5f)*rect.w/Height);
        }

        private static float Maximum(Color color)=>Mathf.Max(color.r,Mathf.Max(color.g,color.b));
        private static Color[] Read(RenderTexture target)
        {
            var old=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply(false);return image.GetPixels();}
            finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
        }
        private static void Clear(RenderTexture target)
        {
            var old=RenderTexture.active;
            try {RenderTexture.active=target;GL.Clear(false,true,Color.clear);}
            finally {RenderTexture.active=old;}
        }

        public HybridBouncePrototype(Texture fineScene,Texture fineDirect,Rect worldRect,int fineWidth,int fineHeight,int divisor,RoomLightingTable table)
        {
            if(fineScene==null || fineDirect==null || table==null || fineWidth<=0 || fineHeight<=0 || worldRect.width<=0 || worldRect.height<=0 || divisor is not (2 or 4))
                throw new ArgumentException("Гибридное отражение: нужны две текстуры, геометрия уровня и делитель 2/4.");
            if(fineScene.width!=fineWidth || fineScene.height!=fineHeight || fineDirect.width!=fineWidth || fineDirect.height!=fineHeight)
                throw new ArgumentException("Гибридное отражение: размеры входных текстур не совпали с сеткой уровня.");
            Width=(fineWidth+divisor-1)/divisor;Height=(fineHeight+divisor-1)/divisor;Divisor=divisor;
            int levels=1;while((Mathf.Pow(4,levels)-1)/3<Mathf.Sqrt((float)Width*Width+(float)Height*Height)) levels++;
            CascadeLevels=levels;
            try
            {
                prepare=AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath)??throw new InvalidOperationException("Нет Editor-шейдера гибридных отражений: "+ComputePath);
                coarse=Target("Гибрид: консервативное поглощение");seed=Target("Гибрид: постоянный первый отскок");
                var go=new GameObject("Диагностика: отражение всего завода 1/"+divisor) {hideFlags=HideFlags.HideAndDontSave};
                field=go.AddComponent<RoomRadianceField>();
                Set(field,"compute",GameAssets.Load<ComputeShader>("Presentation/RadianceCascades")??throw new InvalidOperationException("Нет исходных каскадов света."));
                Set(field,"width",Width);Set(field,"height",Height);Set(field,"levels",levels);
                float texel=worldRect.width/Width;
                if(Mathf.Abs(texel-worldRect.height/Height)>.0001f)
                    throw new InvalidOperationException("Гибридное отражение: coarse-сетка не имеет квадратных texel; исходные размеры должны делиться на делитель.");
                Set(field,"texel",texel);Set(field,"rect",new Vector4(worldRect.x,worldRect.y,worldRect.width,worldRect.height));
                Set(field,"albedo",(float)table.RadianceAlbedo);Set(field,"edgeTexels",(float)table.ShadowEdge/texel);
                Set(field,"rimTexels",(float)table.ObjectRimWidth/texel);Set(field,"objectGradient",table.ObjectLightMode=="gradient");
                Set(field,"scene",coarse);Set(field,"baseScene",coarse);Set(field,"staticScene",coarse);
                Set(field,"sourceTable",table);
                var allocation=(IEnumerator)typeof(RoomRadianceField).GetMethod("AllocateSteps",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(field,null);
                try {while(allocation.MoveNext()) {}}finally {(allocation as IDisposable)?.Dispose();}

                int down=prepare.FindKernel("DownsampleAndSeed"),inject=prepare.FindKernel("InjectSeed");
                prepare.SetInts("_FineSize",fineWidth,fineHeight);prepare.SetInts("_CoarseSize",Width,Height);
                prepare.SetInt("_Divisor",divisor);prepare.SetFloat("_WallGain",(float)table.RadianceAlbedo*.5f);
                prepare.SetTexture(down,"_FineScene",fineScene);prepare.SetTexture(down,"_FineDirect",fineDirect);
                prepare.SetTexture(down,"_CoarseScene",coarse);prepare.SetTexture(down,"_FirstBounce",seed);
                var watch=Stopwatch.StartNew();
                prepare.Dispatch(down,(Width+7)/8,(Height+7)/8,1);
                Graphics.CopyTexture(seed,Get<RenderTexture>(field,"bounceA"));
                for(int i=0;i<Passes;i++)
                {
                    field.RenderReferenceForChecks();
                    if(i==Passes-1) break;
                    // The original Bounce kernel writes the next indirect generation. Direct incident light is
                    // steady, so add its first wall reflection back before each subsequent transport pass.
                    prepare.SetTexture(inject,"_FirstBounce",seed);prepare.SetTexture(inject,"_BounceNext",Get<RenderTexture>(field,"bounceA"));
                    prepare.Dispatch(inject,(Width+7)/8,(Height+7)/8,1);
                }
                Fence((RenderTexture)Result);watch.Stop();FenceMilliseconds=watch.Elapsed.TotalMilliseconds;
                var owned=new HashSet<RenderTexture> {coarse,seed};
                foreach(var member in typeof(RoomRadianceField).GetFields(BindingFlags.Instance|BindingFlags.NonPublic))
                    if(member.FieldType==typeof(RenderTexture) && member.GetValue(field) is RenderTexture target) owned.Add(target);
                foreach(var target in owned)
                    Bytes+=LightTextureBudget.Size(target.width,target.height,target.graphicsFormat==GraphicsFormat.R32G32B32A32_SFloat?16:target.graphicsFormat==GraphicsFormat.R32_SFloat?4:8);
            }
            catch {Dispose();throw;}
        }

        private RenderTexture Target(string name)
        {
            var target=new RenderTexture(Width,Height,0,GraphicsFormat.R16G16B16A16_SFloat)
                {name=name,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            try
            {
                LightTextureBudget.Track(target,checked((long)Width*Height*8));
                if(!target.Create()) throw new InvalidOperationException("Не создан буфер прототипа: "+name);
                return target;
            }
            catch {LightTextureBudget.Release(target);throw;}
        }
        private static T Get<T>(object obj,string name)=>(T)typeof(RoomRadianceField).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
        private static void Set(object obj,string name,object value)=>typeof(RoomRadianceField).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
        private static void Fence(RenderTexture target)
        {
            var old=RenderTexture.active;var pixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=target;pixel.ReadPixels(new Rect(0,0,1,1),0,0);pixel.Apply(false);}
            finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixel);}
        }
        public void Dispose()
        {
            if(disposed) return;disposed=true;
            if(field!=null)
            {
                // coarse is owned by this helper, not by the borrowed field's static-scene recycler.
                Set(field,"scene",null);Set(field,"baseScene",null);Set(field,"staticScene",null);
                field.ReleaseResources();UnityEngine.Object.DestroyImmediate(field.gameObject);field=null;
            }
            LightTextureBudget.Release(coarse);LightTextureBudget.Release(seed);coarse=seed=null;
        }
    }
}
