using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // PT-20261105-01: isolate output sampling from geometry, polar bins and nonlinear resolve.
    // Editor-only. Never builds/replaces a Player, changes production settings or loads user saves.
    public static class LightSamplingStudy
    {
        private const int Width=960,Height=480;
        private static readonly Rect World=new(0,0,12,6);
        private static readonly Vector2 Source=new(1.2f,4.2f);
        private const float Reach=12,EmitterWidth=.35f,Power=12;
        private static string Output=>Path.GetFullPath("../.reference/light-steps-20261106");
        [Serializable] private sealed class Edge
        {public string variant;public int columns,longestIntegerPlateau;public double fitRmsePixels,maximumFitErrorPixels;}
        [Serializable] private sealed class Result
        {
            public bool success;
            public string device,api,method;
            public float productionAgreement,resolveOrderMaxDifference,outputSamplingMaxDifference;
            public double coarseResolvedRmse,rawUpsampleRmse,nativePolarRmse;
            public Edge[] edges;
            public int width=Width,height=Height;
            public long view10cmBytes=120L*60*8,viewNativeBytes=Width*Height*8L;
            public string limitation="Контролируемый угол, один источник, без актёров и отражений; exact означает тот же ray-march7,5см без полярных bins, не аналитическую физику. Время diagnostic Editor с fence не равно GPU timestamps/FPS. Основной exe не изменяется.";
        }
        internal sealed class Sampler:IDisposable
        {
            private readonly LevelLightScene scene;
            private readonly ComputeShader shader;
            private readonly ComputeBuffer points,distances;
            private readonly RenderTexture map;
            private readonly int build,field,window;
            private readonly int angles,rows;
            private readonly Rect outputRect;
            internal Sampler(LevelLightScene scene,LevelLightSource source=null,Rect? area=null)
            {
                this.scene=scene;outputRect=area??World;source??=SourceDefinition(scene);
                var asset=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Remains/Editor/LightGridStudy/LightSamplingStudy.compute");
                if(asset==null)throw new InvalidOperationException("Нет диагностического compute-шейдера света.");
                shader=Object.Instantiate(asset);build=shader.FindKernel("BuildMaps");field=shader.FindKernel("Field");window=shader.FindKernel("Window");
                float step=.75f/scene.Resolution;angles=(int)Math.Ceiling(2*Math.PI*source.Reach/.05f);rows=(int)Math.Ceiling(source.Reach/step)+1;
                points=new ComputeBuffer(source.Samples.Length,16);points.SetData(source.Samples);
                distances=new ComputeBuffer(rows,4);var data=new float[rows];float at=step*.5f;
                for(int i=0;i<rows;i++,at+=step)data[i]=at;distances.SetData(data);
                map=new RenderTexture(angles,rows,0,GraphicsFormat.R32_SFloat){dimension=TextureDimension.Tex2DArray,volumeDepth=source.Samples.Length,enableRandomWrite=true,filterMode=FilterMode.Point};
                Require(map.Create(),"Не создана карта углов.");
                shader.SetInts("_SceneSize",scene.Width,scene.Height);shader.SetVector("_SceneRect",Vector(scene.WorldRect));shader.SetFloat("_PPU",scene.Resolution);
                shader.SetFloat("_MarchStep",step);shader.SetInt("_SampleOffset",0);shader.SetInt("_SampleCount",source.Samples.Length);shader.SetInt("_Angles",angles);shader.SetInt("_RadialSamples",rows);
                shader.SetFloat("_Radius",source.Reach);shader.SetFloat("_ProjectedWidth",source.Width);shader.SetVector("_Radiance",source.Radiance);
                shader.SetVector("_SourceBounds",new Vector4(source.Bounds.xMin,source.Bounds.yMin,source.Bounds.xMax,source.Bounds.yMax));
                shader.SetInts("_FieldOrigin",source.Texels.x,source.Texels.y);shader.SetInts("_FieldSize",source.Texels.width,source.Texels.height);
                foreach(int kernel in new[]{build,field,window}){shader.SetTexture(kernel,"_Scene",scene.Texture);shader.SetBuffer(kernel,"_Samples",points);}
                shader.SetTexture(build,"_MapOut",map);shader.SetBuffer(build,"_MarchDistances",distances);
                foreach(int kernel in new[]{field,window}){shader.SetTexture(kernel,"_Map",map);shader.SetBuffer(kernel,"_MarchDistances",distances);}
                shader.Dispatch(build,(angles+63)/64,source.Samples.Length,1);
            }
            internal void Coarse(RenderTexture target)
            {shader.SetTexture(field,"_Field",target);shader.Dispatch(field,(target.width+7)/8,(target.height+7)/8,1);}
            internal void Native(RenderTexture target,bool exact=false,bool add=false)
            {
                shader.SetInts("_OutputSize",target.width,target.height);shader.SetVector("_OutputRect",Vector(outputRect));shader.SetInt("_WindowExact",exact?1:0);shader.SetInt("_WindowAdd",add?1:0);
                shader.SetTexture(window,"_Window",target);shader.Dispatch(window,(target.width+7)/8,(target.height+7)/8,1);
            }
            public void Dispose(){points?.Dispose();distances?.Dispose();foreach(var value in new Object[]{map,shader})if(value!=null)Object.DestroyImmediate(value);}
        }
        public static void RunBatch()
        {try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var result=new Result{device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                method="Одинаковый угол, поглощение, источник, production HLSL и полярная карта5см. A:resolve10см→bilinear. B:bilinear RAW10см→resolve output. C:production polar на каждый outputpixel, прежняя геометрия10см. D:то же, геометрия2,5см. E:ray-march без polar при прежней геометрии10см. F:без fill. Во всех comparisons resolve использует один FactoryWispComposite и gain1/fill0,08."};
            using var scene=Scene(10);using var sampler=new Sampler(scene);
            var owned=new List<RenderTexture>();
            RenderTexture Make(int w,int h,string name){var target=Target(w,h,name);owned.Add(target);return target;}
            var raw=Make(120,60,"Сырой direct10см");sampler.Coarse(raw);
            var native=Make(Width,Height,"Direct на выходной пиксель");sampler.Native(native);
            var exact=Make(Width,Height,"Луч на выходной пиксель");sampler.Native(exact,true);
            var fine=Make(Width,Height,"Точная геометрия");
            using(var fineScene=Scene(40))using(var fineSampler=new Sampler(fineScene))fineSampler.Native(fine);
            var white=Make(Width,Height,"Заполнение1");Clear(white,Color.white);var smallWhite=Make(120,60,"Заполнение1 coarse");Clear(smallWhite,Color.white);
            var zero=Make(Width,Height,"Без заполнения");Clear(zero,Color.black);
            using var production=new PolarDirectLight(scene,new LevelLightSources(new[]{SourceDefinition(scene)},scene.Width,scene.Height));production.BuildAll();
            result.productionAgreement=Difference(Read(raw),Read(production.Sum));
            Require(result.productionAgreement<.002f,"Диагностическая coarse-реализация отличается от production: "+result.productionAgreement);
            using var low=new FactoryWispComposite(World,120,60);low.Begin(raw,World,smallWhite,1);low.Resolve();
            var a=Make(Width,Height,"A current");Graphics.Blit(low.Front,a);
            using var b=new FactoryWispComposite(World,Width,Height);b.Begin(raw,World,white,1);b.Resolve();
            using var c=new FactoryWispComposite(World,Width,Height);c.Begin(native,World,white,1);c.Resolve();
            using var d=new FactoryWispComposite(World,Width,Height);d.Begin(fine,World,white,1);d.Resolve();
            using var e=new FactoryWispComposite(World,Width,Height);e.Begin(exact,World,white,1);e.Resolve();
            using var noFill=new FactoryWispComposite(World,Width,Height);noFill.Begin(native,World,zero,1);noFill.Resolve();
            var rawUpsampled=Make(Width,Height,"RAW coarse увеличенный");Graphics.Blit(raw,rawUpsampled);
            try
            {
                var ap=Read(a);var bp=Read(b.Front);var cp=Read(c.Front);var ep=Read(e.Front);
                result.resolveOrderMaxDifference=Difference(ap,bp);result.outputSamplingMaxDifference=Difference(bp,cp);
                result.coarseResolvedRmse=Rmse(ap,ep);result.rawUpsampleRmse=Rmse(bp,ep);result.nativePolarRmse=Rmse(cp,ep);
                result.edges=new[]{MeasureEdge("coarse10cm",Read(rawUpsampled)),MeasureEdge("native-polar",Read(native)),MeasureEdge("native-ray",Read(exact)),MeasureEdge("fine-geometry",Read(fine))};
                Save("a-current.png",a);Save("b-upsample-before-resolve.png",b.Front);Save("c-native-polar.png",c.Front);Save("d-fine-geometry.png",d.Front);Save("e-native-ray.png",e.Front);Save("f-native-no-fill.png",noFill.Front);
                Save("raw-coarse-visibility.png",rawUpsampled,true);Save("raw-native-visibility.png",native,true);Save("raw-ray-visibility.png",exact,true);
                result.success=true;File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(result,true));WritePage();
                Debug.Log("Лесенки света: PASS — production parity="+result.productionAgreement+", resolve order delta="+result.resolveOrderMaxDifference+", sampling delta="+result.outputSamplingMaxDifference+"; "+Output);
            }
            finally{foreach(var target in owned)Object.DestroyImmediate(target);}
        }
        internal static LevelLightScene Scene(int resolution)
        {
            var walls=new float[12*resolution,6*resolution];
            // Cell-aligned axis-aligned silhouette: increasing geometry resolution does not change its shape.
            for(int y=0;y<walls.GetLength(1);y++)for(int x=0;x<walls.GetLength(0);x++)
            {float wx=(x+.5f)/resolution,wy=(y+.5f)/resolution;if(wx>=3.8f && wx<4.4f && wy>=3.5f)walls[x,y]=1;}
            return new LevelLightScene(walls,Vector2.zero,resolution,Array.Empty<LevelLightScene.ObjectSprite>());
        }
        internal static LevelLightSource SourceDefinition(LevelLightScene scene)=>new("study",LevelLightSourceKind.Lamp,
            new[]{new Vector4(Source.x,Source.y,1,0)},new Color(Power,Power,Power),EmitterWidth,Reach,new Rect(Source-Vector2.one*.01f,Vector2.one*.02f),new RectInt(0,0,scene.Width,scene.Height),true);
        private static float Unshadowed(int x,int y)
        {var world=new Vector2((x+.5f)/Width*World.width,(y+.5f)/Height*World.height);float distance=Vector2.Distance(world,Source);return Power*2*Mathf.Atan(EmitterWidth/(2*Mathf.Max(distance,.05f)))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Reach*.9f,Reach,distance)));}
        private static Edge MeasureEdge(string name,Color[] data)
        {
            var crossings=new List<(double X,double Y)>();int longest=0,run=0,previous=int.MinValue;
            for(int x=Width/2;x<Width-Width/16;x++)
            {
                double cross=double.NaN;
                for(int y=1;y<Height*3/4;y++)
                {float a=data[(y-1)*Width+x].r/Mathf.Max(1e-5f,Unshadowed(x,y-1)),b=data[y*Width+x].r/Mathf.Max(1e-5f,Unshadowed(x,y));if(a>=.5f && b<.5f){cross=y-1+(a-.5f)/(a-b);break;}}
                if(!double.IsFinite(cross))continue;crossings.Add((x,cross));int row=(int)Math.Round(cross);
                run=row==previous?run+1:1;previous=row;longest=Math.Max(longest,run);
            }
            Require(crossings.Count>100,"Не найдена длинная граница тени: "+name);
            double mx=crossings.Average(p=>p.X),my=crossings.Average(p=>p.Y),slope=crossings.Sum(p=>(p.X-mx)*(p.Y-my))/crossings.Sum(p=>(p.X-mx)*(p.X-mx));
            var errors=crossings.Select(p=>Math.Abs(p.Y-my-slope*(p.X-mx))).ToArray();
            return new Edge{variant=name,columns=crossings.Count,longestIntegerPlateau=longest,fitRmsePixels=Math.Sqrt(errors.Average(v=>v*v)),maximumFitErrorPixels=errors.Max()};
        }
        private static RenderTexture Target(int w,int h,string name)
        {var t=new RenderTexture(w,h,0,GraphicsFormat.R16G16B16A16_SFloat){name=name,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};Require(t.Create(),"Не создана текстура "+name);return t;}
        private static void Clear(RenderTexture t,Color color){var old=RenderTexture.active;try{RenderTexture.active=t;GL.Clear(false,true,color);}finally{RenderTexture.active=old;}}
        private static Color[] Read(Texture texture){var request=AsyncGPUReadback.Request(texture,0,TextureFormat.RGBAFloat);request.WaitForCompletion();Require(!request.hasError,"Ошибка readback "+texture.name);return request.GetData<Color>().ToArray();}
        private static float Difference(Color[] a,Color[] b){Require(a.Length==b.Length,"Разные размеры полей.");float value=0;for(int i=0;i<a.Length;i++)value=Mathf.Max(value,Mathf.Abs(a[i].r-b[i].r));return value;}
        private static double Rmse(Color[] a,Color[] b){double sum=0;int count=0;for(int y=0;y<Height*3/4;y++)for(int x=Width/2;x<Width-Width/16;x++){double e=a[y*Width+x].r-b[y*Width+x].r;sum+=e*e;count++;}return Math.Sqrt(sum/count);}
        private static void Save(string name,Texture source,bool visibility=false)
        {
            var data=Read(source);for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {int i=y*Width+x;float v=visibility?data[i].r/Mathf.Max(1e-5f,Unshadowed(x,y)):data[i].r;v=Mathf.LinearToGammaSpace(Mathf.Clamp01(v));if(x/80f>=3.8f && x/80f<4.4f && y/80f>=3.5f)data[i]=new Color(.25f,.27f,.3f,1);else data[i]=new Color(v,v,v,1);}
            var image=new Texture2D(Width,Height,TextureFormat.RGBA32,false,true);try{image.SetPixels(data);image.Apply();File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
        }
        private static void WritePage()
        {
            var rows=new[]{("a-current.png","A. Игровой порядок: resolve на10см, затем увеличение"),("b-upsample-before-resolve.png","B. Та же сетка: сначала увеличение RAW, затем resolve"),("c-native-polar.png","C. Полярный direct на выходной пиксель, геометрия10см"),("d-fine-geometry.png","D. Тот же вывод, геометрия2,5см"),("e-native-ray.png","E. Луч на выходной пиксель, геометрия10см"),("f-native-no-fill.png","F. ВариантC без заполнения"),("raw-coarse-visibility.png","Видимость: поле10см"),("raw-native-visibility.png","Видимость: native polar"),("raw-ray-visibility.png","Видимость: native ray")};
            string body=string.Join("",rows.Select(r=>"<figure><figcaption>"+r.Item2+"</figcaption><img src='"+r.Item1+"'></figure>"));
            File.WriteAllText(Path.Combine(Output,"index.html"),"<!doctype html><meta charset='utf-8'><title>Ступени света — диагностика</title><style>body{background:#22272e;color:#eee;font:17px system-ui;margin:24px}main{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}figure{margin:0}img{width:100%}figcaption{min-height:3em}a{color:#9cf}</style><h1>Лесенки: разделение причин</h1><p>Один источник и один прямой угол. Все формулы — из текущего игрового compute. Основной exe не меняется. Заполнение0,08, кромеF. Масштаб80пикс/м.</p><p><a href='report.json'>Численные данные и ограничения</a></p><main>"+body+"</main>");
        }
        private static Vector4 Vector(Rect r)=>new(r.x,r.y,r.width,r.height);
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException("Диагностика ступеней: "+message);}
    }
}
