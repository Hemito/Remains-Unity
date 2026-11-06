using System;
using System.IO;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class DetailLightChecks
    {
        private static string Output=>Path.GetFullPath("../.reference/light-detail-20261106");
        [Serializable] private sealed class Report {public bool success;public float oracleError,hdrMaskError,hdrOffError;public int maskFrames,mapBuilds,staticBuilds,lampUpdates,panBuilds;public long retainedBytes,panRetainedBytes;}
        public static void RunBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);var report=new Report();long before=LightTextureBudget.Bytes;
                Check(report);CheckHdr(report);CheckPan(report);Require(LightTextureBudget.Bytes==before,"Утечка текстур после Dispose: "+(LightTextureBudget.Bytes-before));
                report.success=true;File.WriteAllText(Path.Combine(Output,"checks.json"),JsonUtility.ToJson(report,true));
                Debug.Log("DetailLightChecks PASS: native oracle, движущиеся маски без rebuild, OFF/ON, дверь, память.");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void Check(Report report)
        {
            var scene=LightSamplingStudy.Scene(10);var source=LightSamplingStudy.SourceDefinition(scene);
            using var light=new LevelLight(scene,new LevelLightSources(new[]{source},scene.Width,scene.Height),.2f);light.BuildAll();
            var context=new LightShaderContext();context.Activate();using var view=new LevelLightView(light,context){FillGain=0};
            var polar=(PolarDirectLight)light.Direct;var area=scene.WorldRect;
            var none=Array.Empty<ProjectedLightShadow>();var ids=Array.Empty<string>();var flashes=Array.Empty<SpriteEmission>();
            view.Present(area,flashes,none,ids);var actual=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
            var raw=new RenderTexture(960,480,0,GraphicsFormat.R16G16B16A16_SFloat){enableRandomWrite=true,filterMode=FilterMode.Bilinear};Require(raw.Create(),"Нет oracle RT");
            var zero=new Texture2D(1,1,TextureFormat.RGBA32,false,true);zero.SetPixel(0,0,Color.black);zero.Apply();
            try
            {
                using var sampler=new LightSamplingStudy.Sampler(scene);sampler.Native(raw);
                using var expected=new FactoryWispComposite(area,960,480);expected.Begin(raw,area,zero,1);expected.Resolve();
                report.oracleError=Difference(actual,Read(expected.Front));Require(report.oracleError<.004f,"Отличие от native oracle: "+report.oracleError);
                Save("runtime-native.png",Shader.GetGlobalTexture("_RemainsRadiance"));
                int maps=polar.DetailMapBuilds,builds=view.DetailBuilds;
                for(int i=0;i<20;i++)
                {
                    var m=new ProjectedLightShadow(source.Id,Texture2D.whiteTexture,new Rect(6+i*.02f,1,2,1),.8f,i);
                    view.Present(area,flashes,new[]{m},ids);
                    Require(Difference(actual,Read(Shader.GetGlobalTexture("_RemainsRadiance")))==0,"Маска затемнила актёра.");
                    Require(Difference(actual,Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance")))>.01f,"Нет маски на стене.");report.maskFrames++;
                }
                Require(maps==polar.DetailMapBuilds && builds==view.DetailBuilds,"Движение маски пересобирает статический свет/карты.");
                light.SetDark(new[]{source.Id});Finish(light);view.Present(area,flashes,none,ids);
                Require(Read(Shader.GetGlobalTexture("_RemainsRadiance")).Max(p=>p.maxColorComponent)<.001f,"Выключенная лампа светит.");
                Require(view.DetailBuilds==builds && view.DetailSourceUpdates==1,"OFF пересобирает всё окно.");
                light.SetDark(ids);Finish(light);view.Present(area,flashes,none,ids);
                Require(Difference(actual,Read(Shader.GetGlobalTexture("_RemainsRadiance")))<.004f,"ON не восстановил свет.");
                light.SetDoors(new[]{new LevelLightScene.Door("detail-door",new Rect(2.5f,0,.3f,6),1,true)});Finish(light);view.Present(area,flashes,none,ids);
                Require(Difference(actual,Read(Shader.GetGlobalTexture("_RemainsRadiance")))>.1f,"Дверь не меняет подробный свет.");
                Require(view.DetailBuilds==builds+1 && polar.DetailMapBuilds==maps+1,"Изменение геометрии не обновило карту ровно один раз.");
                report.staticBuilds=view.DetailBuilds;report.mapBuilds=polar.DetailMapBuilds;report.lampUpdates=view.DetailSourceUpdates;report.retainedBytes=LightTextureBudget.Bytes;
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(zero);context.Release();}
        }
        private static void Finish(LevelLight light){for(int i=0;light.HasWork && i<1000;i++)light.Step(2,2);Require(!light.HasWork,"Очередь не завершилась.");}
        private static void CheckHdr(Report report)
        {
            using var scene=new LevelLightScene(new float[120,60],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            LevelLightSource Source(string id,float power)=>new(id,LevelLightSourceKind.Lamp,new[]{new Vector4(3,3,1,0)},new Color(power,power,power),.4f,12,new Rect(2.9f,2.9f,.2f,.2f),new RectInt(0,0,120,60),true);
            using var both=new LevelLight(new LevelLightScene(new float[120,60],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>()),new LevelLightSources(new[]{Source("bright",5000),Source("weak",.1f)},120,60),.2f);
            using var weak=new LevelLight(scene,new LevelLightSources(new[]{Source("weak",.1f)},120,60),.2f);both.BuildAll();weak.BuildAll();
            var context=new LightShaderContext();context.Activate();using var reference=new LevelLightView(weak,context){FillGain=0};using var actual=new LevelLightView(both,context){FillGain=0};
            var noIds=Array.Empty<string>();var noFlashes=Array.Empty<SpriteEmission>();var noMasks=Array.Empty<ProjectedLightShadow>();
            reference.Present(scene.WorldRect,noFlashes,noMasks,noIds);var expected=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
            actual.Present(scene.WorldRect,noFlashes,new[]{new ProjectedLightShadow("bright",Texture2D.whiteTexture,scene.WorldRect,1)},noIds);
            report.hdrMaskError=Difference(expected,Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance")));Require(report.hdrMaskError<.001f,"Маска яркой лампы съела слабого соседа: "+report.hdrMaskError);
            both.SetDark(new[]{"bright"});Finish(both);actual.Present(scene.WorldRect,noFlashes,noMasks,noIds);
            report.hdrOffError=Difference(expected,Read(Shader.GetGlobalTexture("_RemainsRadiance")));Require(report.hdrOffError<.001f,"OFF яркой лампы съел слабого соседа: "+report.hdrOffError);context.Release();
        }
        private static void CheckPan(Report report)
        {
            var scene=new LevelLightScene(new float[400,200],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            var source=new LevelLightSource("pan",LevelLightSourceKind.Lamp,new[]{new Vector4(6,5,1,0)},Color.white,.3f,10,new Rect(5.9f,4.9f,.2f,.2f),new RectInt(0,0,160,150),true);
            using var light=new LevelLight(scene,new LevelLightSources(new[]{source},400,200),.2f);light.BuildAll();
            var context=new LightShaderContext();context.Activate();using var view=new LevelLightView(light,context);
            var area=new Rect(3,3,6,4);var noMasks=Array.Empty<ProjectedLightShadow>();var ids=Array.Empty<string>();var flashes=Array.Empty<SpriteEmission>();
            view.Present(area,flashes,noMasks,ids);long bytes=LightTextureBudget.Bytes;
            for(int i=1;i<12;i++){area.x=3+i*.6f;view.Present(area,flashes,noMasks,ids);Require(LightTextureBudget.Bytes==bytes,"Память растёт при переносе подробного окна.");}
            var polar=(PolarDirectLight)light.Direct;Require(polar.DetailMapBuilds==1,"Перенос камеры перестраивает неизменную карту.");
            int builds=view.DetailBuilds;light.SetDoors(new[]{new LevelLightScene.Door("far",new Rect(35,15,.2f,2),1,true)});Finish(light);view.Present(area,flashes,noMasks,ids);
            Require(view.DetailBuilds==builds,"Дальняя дверь пересобирает подробное окно.");report.panBuilds=builds;report.panRetainedBytes=bytes;context.Release();
        }
        private static Color[] Read(Texture t){var r=AsyncGPUReadback.Request(t,0,TextureFormat.RGBAFloat);r.WaitForCompletion();Require(!r.hasError,"Ошибка чтения "+t.name);return r.GetData<Color>().ToArray();}
        private static float Difference(Color[] a,Color[] b){Require(a.Length==b.Length,"Размеры native oracle разные");float result=0;for(int i=0;i<a.Length;i++)result=Mathf.Max(result,Mathf.Abs(a[i].r-b[i].r),Mathf.Abs(a[i].g-b[i].g),Mathf.Abs(a[i].b-b[i].b));return result;}
        private static void Save(string file,Texture source){var t=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);try{t.SetPixels(Read(source));t.Apply();File.WriteAllBytes(Path.Combine(Output,file),t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}}
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException("Подробный свет: "+message);}
    }
}
