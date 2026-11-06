using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // One offline render of selected real factory views. No gameplay actors, flashes or mask ambiguity.
    public static class LightSamplingFactoryStudy
    {
        private const int Width=960,Height=576;
        private static string Output=>Path.GetFullPath("../.reference/light-steps-20261106");
        [Serializable] private sealed class Row
        {public string id;public Rect area;public int sources,samples;public double nativeMapAndRenderFenceMs;public float resolveOrderMaxDifference,nativeOutputMaxDifference;}
        [Serializable] private sealed class Report
        {
            public bool success;public int seed=104;public Row[] rooms;
            public string method="Тот же seed104, настоящие RGB/дальности/все1–8точек излучателей и поглощение10см. Без актёров/оружия/вспышек. A:настоящий LevelLightView. B:RAW-world-sum bilinear в выходной кадр, затем resolve. C:та же production HLSL/Irradiance/5см-полярная карта, target на выходной пиксель. Fill во всех случаях из одного готового мирового поля0,08. NativeMapAndRenderFenceMs — один холодный offline rebuild видимых источников, не цена кадра/FPS.";
        }
        public static void RunBatch()
        {try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        private static void Run()
        {
            Directory.CreateDirectory(Output);ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var world=NewGameWorld.Create(104);FactoryContentSources.Prepare(world,"factory:0");
            var authoring=RoomAuthoring.Load("factory:0",world);DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));var layout=authoring.CreateLayout(false);
            GameObject visual=null,cameraObject=null;ComputeShader resample=null;
            try
            {
                visual=AuthoredRoomVisual.Create("factory:0",layout,world);var view=visual.GetComponent<FactoryLevelView>();var factory=visual.GetComponent<FactoryLevelRoot>();view.Synchronize(layout,world,1);
                view.Focus(new Vector2(layout.spawnX,layout.spawnY));var light=view.Light;Require(light.Direct is PolarDirectLight && light.Fill!=null,"Не выбран текущий основной свет.");
                resample=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Remains/Editor/LightGridStudy/LightSamplingResample.compute"));
                cameraObject=new GameObject("Editor: лесенки в настоящем заводе");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=(float)Width/Height;camera.orthographicSize=3.6f;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
                var rows=new List<Row>();var figures=new List<string>();float cell=(float)factory.Prepared.Grid.Level.Cell;
                foreach(var (x,y,id) in new[]{(1,1,"hall"),(1,9,"office"),(0,0,"entry")})
                {
                    var room=factory.Prepared.Grid.Rooms.Single(p=>p.Cell.X==x && p.Cell.Y==y);
                    var bounds=new Rect(room.Column*cell,(factory.Prepared.Grid.Level.Height-room.Row-room.Template.Height)*cell,room.Template.Width*cell,room.Template.Height*cell);
                    var area=new Rect(bounds.center-new Vector2(6,3.6f)+new Vector2(-bounds.width*.12f,-bounds.height*.08f),new Vector2(12,7.2f));
                    camera.transform.position=new Vector3(area.center.x,area.center.y,-10);view.Focus(area.center);view.SetViewArea(area);view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
                    var currentTexture=Shader.GetGlobalTexture("_RemainsRadiance");var r=Shader.GetGlobalVector("_RemainsRadianceRect");var currentRect=new Rect(r.x,r.y,r.z,r.w);
                    RenderTexture current=Target("A actual"),fill=Target("Тот же fill"),native=Target("C native raw");
                    try
                    {
                        Sample(resample,currentTexture,currentRect,area,current);Sample(resample,light.Fill.Texture,light.Scene.WorldRect,area,fill);Clear(native);
                        using var b=new FactoryWispComposite(area,Width,Height);b.Begin(light.Direct.Sum,light.Scene.WorldRect,fill,1);b.Resolve();
                        var indices=new List<int>();light.Sources.Overlapping(light.Scene.Texels(area),indices);
                        var row=new Row{id=id,area=area};var watch=Stopwatch.StartNew();
                        foreach(int index in indices)
                        {
                            var source=light.Sources.All[index];if(!source.Enabled)continue;
                            using var sampler=new LightSamplingStudy.Sampler(light.Scene,source,area);sampler.Native(native,add:true);row.sources++;row.samples+=source.Samples.Length;
                        }
                        Read(native);row.nativeMapAndRenderFenceMs=watch.Elapsed.TotalMilliseconds;
                        using var c=new FactoryWispComposite(area,Width,Height);c.Begin(native,area,fill,1);c.Resolve();
                        row.resolveOrderMaxDifference=Difference(Read(current),Read(b.Front));row.nativeOutputMaxDifference=Difference(Read(b.Front),Read(c.Front));rows.Add(row);
                        // Warm deferred artwork uploads before the three views; no physical state changes between them.
                        camera.Render();camera.Render();Capture(camera,id+"-a-current.png");
                        b.Publish();Capture(camera,id+"-b-upsample-before-resolve.png");c.Publish();Capture(camera,id+"-c-native.png");
                        foreach(var variant in new[]{("a-current","A. Основной игровой путь"),("b-upsample-before-resolve","B. RAW-upsample перед resolve"),("c-native","C. Прямой свет на выходной пиксель")})
                            figures.Add("<figure><figcaption>"+id+" — "+variant.Item2+"</figcaption><img src='"+id+"-"+variant.Item1+".png'></figure>");
                    }
                    finally{foreach(var t in new[]{current,fill,native})Object.DestroyImmediate(t);}
                }
                File.WriteAllText(Path.Combine(Output,"factory-report.json"),JsonUtility.ToJson(new Report{success=true,rooms=rows.ToArray()},true));
                File.WriteAllText(Path.Combine(Output,"factory.html"),"<!doctype html><meta charset='utf-8'><title>Лесенки света: завод</title><style>body{background:#22272e;color:#eee;font:17px system-ui;margin:24px}main{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}figure{margin:0}img{width:100%}figcaption{min-height:3em}a{color:#9cf}</style><h1>Настоящий завод: меняется только выборка direct</h1><p>Seed104, три комнаты, zoom80пикс/м, без актёров/вспышек. Геометрия10см, полярные углы5см, те же RGB/дальность/точки/fill0,08. Основной exe не менялся.</p><p><a href='factory-report.json'>Отчёт и ограничения</a> · <a href='index.html'>Контрольный угол</a></p><main>"+string.Join("",figures)+"</main>");
                Debug.Log("Лесенки на заводе: PASS — 3комнаты, настоящие источники/преграды, без actor masks; "+Output);
            }
            finally{foreach(var value in new Object[]{cameraObject,visual,resample})if(value!=null)Object.DestroyImmediate(value);FactoryContentSources.Clear();}
        }
        private static RenderTexture Target(string name){var t=new RenderTexture(Width,Height,0,GraphicsFormat.R16G16B16A16_SFloat){name=name,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};Require(t.Create(),"Нет texture "+name);return t;}
        private static void Clear(RenderTexture t){var previous=RenderTexture.active;try{RenderTexture.active=t;GL.Clear(false,true,Color.clear);}finally{RenderTexture.active=previous;}}
        private static void Sample(ComputeShader shader,Texture input,Rect source,Rect output,RenderTexture target)
        {int kernel=shader.FindKernel("Resample");shader.SetTexture(kernel,"_Input",input);shader.SetTexture(kernel,"_Output",target);shader.SetInts("_Size",Width,Height);shader.SetVector("_InputRect",new Vector4(source.x,source.y,source.width,source.height));shader.SetVector("_OutputRect",new Vector4(output.x,output.y,output.width,output.height));shader.Dispatch(kernel,(Width+7)/8,(Height+7)/8,1);}
        private static Color[] Read(Texture t){var r=AsyncGPUReadback.Request(t,0,TextureFormat.RGBAFloat);r.WaitForCompletion();Require(!r.hasError,"Readback failed "+t.name);return r.GetData<Color>().ToArray();}
        private static float Difference(Color[] a,Color[] b){float value=0;for(int i=0;i<a.Length;i++)value=Mathf.Max(value,Mathf.Abs(a[i].r-b[i].r),Mathf.Abs(a[i].g-b[i].g),Mathf.Abs(a[i].b-b[i].b));return value;}
        private static void Capture(Camera camera,string file)=>Remains.Runtime.NetworkFrameCapture.Save(camera,Path.Combine(Output,file),width:Width,height:Height);
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException("Свет завода: "+message);}
    }
}
