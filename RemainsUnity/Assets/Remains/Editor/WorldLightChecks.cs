using System;
using System.IO;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class WorldLightChecks
    {
        private static string Output=>Path.GetFullPath("../.reference/world-light-20261106");
        [Serializable] private sealed class Report
        {public bool success;public int stableBuilds,movingBuilds,doorBuilds;public float maskError;public long cachedBytes;
            public float[] rangeDistances,rayRange,polarRange;}
        public static void RunBatch()
        {try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            Directory.CreateDirectory(Output);var report=new Report();long before=LightTextureBudget.Bytes;
            CheckRange(report);Check(report);Require(LightTextureBudget.Bytes==before,"Утечка текстур: "+(LightTextureBudget.Bytes-before));
            CheckFireSources();CheckAffine();report.success=true;File.WriteAllText(Path.Combine(Output,"checks.json"),JsonUtility.ToJson(report,true));
            Debug.Log("WorldLightChecks PASS: затухание по дальности, цвет, маски, fade без transport, движение своего источника, дверь, expiry, пустой кадр, Dispose.");
        }
        private static void CheckRange(Report report)
        {
            const int pixelsPerMetre=10, sourceX=10, sourceY=19;
            const float width=.4f;
            var point=new Vector2((sourceX+.5f)/pixelsPerMetre,(sourceY+.5f)/pixelsPerMetre);
            using var scene=new LevelLightScene(new float[120,40],Vector2.zero,pixelsPerMetre,Array.Empty<LevelLightScene.ObjectSprite>());
            foreach(float reach in new[]{.8f,8f})
            {
                var source=new LevelLightSource("range",LevelLightSourceKind.Lamp,new[]{new Vector4(point.x,point.y,1,0)},
                    Color.white,width,reach,new Rect(point-Vector2.one*.01f,Vector2.one*.02f),new RectInt(0,0,120,40),true);
                var sources=new LevelLightSources(new[]{source},120,40);
                using var rays=new DirectLight(scene,sources,8*1024*1024);
                using var polar=new PolarDirectLight(scene,sources,8*1024*1024);
                rays.BuildAll();polar.BuildAll();
                var rayPixels=Read(rays.Sum);var polarPixels=Read(polar.Sum);
                int last=Mathf.RoundToInt(reach*pixelsPerMetre)+2;
                var distances=new float[last+1];var raySamples=new float[last+1];var polarSamples=new float[last+1];
                float reference=Mathf.Min(1,reach/4);
                float referenceWindow=1-Mathf.Pow(reference/reach,2);
                for(int offset=0;offset<=last;offset++)
                {
                    float distance=offset/(float)pixelsPerMetre;
                    float window=Mathf.Max(0,1-Mathf.Pow(distance/reach,2))/referenceWindow;
                    float expected=2*Mathf.Atan(width/(2*Mathf.Max(distance,.5f/pixelsPerMetre)))*window*window;
                    int sample=sourceY*rays.Sum.width+sourceX+offset;
                    float ray=rayPixels[sample].r,pol=polarPixels[sample].r;
                    float tolerance=Mathf.Max(.00003f,expected*.0015f);
                    Require(Mathf.Abs(ray-expected)<tolerance && Mathf.Abs(pol-expected)<tolerance,
                        $"Дальность {reach} м, отсчёт {distance} м: ожидается {expected}, лучи {ray}, полярный {pol}.");
                    distances[offset]=distance;raySamples[offset]=ray;polarSamples[offset]=pol;
                }
                report.rangeDistances=distances;report.rayRange=raySamples;report.polarRange=polarSamples;
            }
        }
        private static WorldLightEmission Point(string id,Vector2 at,Color color,float gain=1)
            =>new(id,new[]{new Vector4(at.x,at.y,1,0)},new Rect(at-Vector2.one*.02f,Vector2.one*.04f),color,.3f,4,gain);
        private static void Check(Report report)
        {
            var scene=new LevelLightScene(new float[120,80],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            using var light=new LevelLight(scene,new LevelLightSources(Array.Empty<LevelLightSource>(),120,80),.2f);light.BuildAll();
            var context=new LightShaderContext();context.Activate();using var view=new LevelLightView(light,context);
            var noFlashes=Array.Empty<SpriteEmission>();var noIds=Array.Empty<string>();var noMasks=Array.Empty<ProjectedLightShadow>();
            var red=Point("fire:test",new Vector2(3,3),new Color(8,1,.1f));var blue=Point("moving:test",new Vector2(7,3),new Color(.1f,1,8));
            void Draw(WorldLightEmission[] emit,ProjectedLightShadow[] masks=null)=>view.Present(scene.WorldRect,noFlashes,masks??noMasks,noIds,emit);
            Draw(Array.Empty<WorldLightEmission>());Require(Max(Read("_RemainsRadiance"))<.001f,"Пустой мир сам светится.");
            long fill=light.Fill.Revision;
            Draw(new[]{red});var baseline=Read("_RemainsRadiance");var lit=At(baseline,view.WorldArea,Shader.GetGlobalTexture("_RemainsRadiance").width,new Vector2(4,3));Require(lit.r>.2f && lit.r>lit.g,"Нет тёплого света рядом с огнём.");
            int builds=view.WorldLightBuilds;for(int i=0;i<8;i++)Draw(new[]{red});Require(view.WorldLightBuilds==builds,"Неподвижный огонь пересчитывает transport.");
            Require(light.Fill.Revision==fill,"Огонь изменил заполнение/отражения.");
            var fade=new WorldLightEmission(red.Id,red.Samples,red.Bounds,red.Radiance,red.Width,red.Reach,.5f);Draw(new[]{fade});Require(view.WorldLightBuilds==builds,"Fade пересобирает поле.");
            Require(Difference(baseline,Read("_RemainsRadiance"))>.1f,"Fade не меняет яркость.");report.stableBuilds=builds;
            Draw(new[]{blue});var blueOnly=Read("_RemainsRadiance");
            Draw(new[]{red,blue},new[]{new ProjectedLightShadow(red.Id,Texture2D.whiteTexture,scene.WorldRect,1)});
            report.maskError=Difference(blueOnly,Read("_RemainsBackgroundRadiance"));Require(report.maskError<.002f,"Маска огня съела другой цвет: "+report.maskError);
            Draw(new[]{red,blue});int prior=view.WorldLightBuilds;
            var moved=Point(blue.Id,new Vector2(7.2f,3),blue.Radiance);Draw(new[]{red,moved});Require(view.WorldLightBuilds==prior+1,"Движение пересчитало чужой источник.");report.movingBuilds=view.WorldLightBuilds-prior;
            Draw(new[]{red});float beforeDoor=At(Read("_RemainsRadiance"),view.WorldArea,Shader.GetGlobalTexture("_RemainsRadiance").width,new Vector2(6,3)).r;Require(beforeDoor>.1f,"Нет света перед тестом двери.");
            prior=view.WorldLightBuilds;light.SetDoors(new[]{new LevelLightScene.Door("near",new Rect(5,0,.3f,8),1,true)});Finish(light);Draw(new[]{red});
            Require(view.WorldLightBuilds==prior+1,"Геометрия не инвалидировала поле огня.");report.doorBuilds=view.WorldLightBuilds-prior;
            Require(At(Read("_RemainsRadiance"),view.WorldArea,Shader.GetGlobalTexture("_RemainsRadiance").width,new Vector2(6,3)).r<.001f,"Свет проходит через закрытую дверь.");
            prior=view.WorldLightBuilds;light.SetDoors(new[]{new LevelLightScene.Door("near",new Rect(5,0,.3f,8),1,true),new LevelLightScene.Door("far",new Rect(11,6,.2f,1),1,true)});Finish(light);Draw(new[]{red});
            Require(view.WorldLightBuilds==prior,"Дальняя дверь пересчитала огонь.");
            report.cachedBytes=view.WorldLightBytes;Save("warm-source.png",Shader.GetGlobalTexture("_RemainsRadiance"));
            Draw(Array.Empty<WorldLightEmission>());Require(view.WorldLightBytes==0 && Max(Read("_RemainsRadiance"))<.001f,"Свет/кэш остался после expiry.");
            // Dynamic sources never enter the fill solver. Doors may legitimately change its revision.
            Require(fill<=light.Fill.Revision,"Ревизия заполнения пошла назад.");context.Release();
        }
        private static void CheckFireSources()
        {
            var sources=new FireLightSources();var fire=new Remains.Domain.FireState{id="check",level="factory:0"};
            sources.Begin();sources.Fire(fire,new[]{new Remains.Domain.FirePatch(new Vector2(2,1),.3f),new Remains.Domain.FirePatch(new Vector2(3,1),.3f)},1);sources.End();
            var old=sources.Emissions[0];Require(old.Samples.Length==2 && Mathf.Abs(old.Samples.Sum(p=>p.z)-1)<.001f,"Не те точки канонического огня.");
            sources.Begin();sources.Fire(fire,new[]{new Remains.Domain.FirePatch(new Vector2(2,1),.3f),new Remains.Domain.FirePatch(new Vector2(3,1),.3f)},.4f);sources.End();Require(old.GeometrySignature()==sources.Emissions[0].GeometrySignature(),"Fade поменял геометрию огня.");
            sources.Begin();sources.Bottle("flight",new Vector2(3,2));sources.End();var a=sources.Emissions[0].GeometrySignature();sources.Begin();sources.Bottle("flight",new Vector2(3.2f,2));sources.End();Require(a!=sources.Emissions[0].GeometrySignature(),"Бутылка потеряла движение света.");
            sources.Begin();sources.End();Require(sources.Emissions.Count==0,"Погасший огонь остался в списке.");
        }
        private static void CheckAffine()
        {
            var bounds=new Bounds(Vector3.zero,new Vector3(2,2,0));var rotation=Matrix4x4.Rotate(Quaternion.Euler(0,0,45));var world=LightEmissionGeometry.WorldBounds(rotation,bounds);
            Require(Mathf.Abs(world.width-2*Mathf.Sqrt(2))<.0001f && Mathf.Abs(world.height-2*Mathf.Sqrt(2))<.0001f,"Границы повёрнутой вспышки потеряли два угла.");
            var shear=Matrix4x4.identity;shear.m00=-2;shear.m01=.5f;shear.m11=.5f;
            Require(Mathf.Abs(LightEmissionGeometry.AreaScale(shear)-1)<.0001f,"Площадь вспышки не учитывает shear/отражение.");
        }
        private static void Finish(LevelLight light){for(int i=0;light.HasWork && i<1000;i++)light.Step(2,2);Require(!light.HasWork,"Очередь света не завершилась.");}
        private static Color[] Read(string name)=>Read(Shader.GetGlobalTexture(name));
        private static Color[] Read(Texture t){var r=AsyncGPUReadback.Request(t,0,TextureFormat.RGBAFloat);r.WaitForCompletion();Require(!r.hasError,"Ошибка GPU readback");return r.GetData<Color>().ToArray();}
        private static float Difference(Color[] a,Color[] b){Require(a.Length==b.Length,"Разные размеры полей");float max=0;for(int i=0;i<a.Length;i++)max=Mathf.Max(max,Mathf.Abs(a[i].r-b[i].r),Mathf.Abs(a[i].g-b[i].g),Mathf.Abs(a[i].b-b[i].b));return max;}
        private static float Max(Color[] a)=>a.Max(p=>Mathf.Max(p.r,p.g,p.b));
        private static Color At(Color[] a,Rect area,int width,Vector2 p){int height=a.Length/width,x=Mathf.Clamp((int)((p.x-area.x)/area.width*width),0,width-1),y=Mathf.Clamp((int)((p.y-area.y)/area.height*height),0,height-1);return a[y*width+x];}
        private static void Save(string file,Texture source){var t=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);try{t.SetPixels(Read(source));t.Apply();File.WriteAllBytes(Path.Combine(Output,file),t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}}
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException("Подвижный свет: "+message);}
    }
}
