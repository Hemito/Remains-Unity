using System;
using System.IO;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class FastLampChecks
    {
        [Serializable] private sealed class Report
        {
            public bool success,cacheMiss,weakNeighbour,repeatedToggles,sequentialOff,busyDoor,batchQueued,fillPreflight,smallSceneQueued;
            public int fastUpdates,cycles;
            public float maximumError,fillError;
            public long sumBytes,additionalSumBytes;
            public string format;
        }
        public static void RunBatch()
        {
            try
            {
                var report=new Report();Check(report);CheckSmall(report);Debug.Log(StationaryPolarChecks.Run());
                report.success=true;var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/lamp-fast-20261104/numeric.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
                Debug.Log("FastLampChecks PASS: слабый сосед, cache miss, повторные/последовательные выключения, занятая дверь, batch, ограничение заполнения и малой сцены.");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static LevelLight Create(int width=800,int height=500)
        {
            var scene=new LevelLightScene(new float[width,height],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            var points=new[]{new Vector2(30,20),new Vector2(30.5f,20),new Vector2(50,20)};
            var colors=new[]{new Color(5000,3000,1500),new Color(.1f,.03f,.06f),new Color(.3f,.7f,.5f)};
            var sources=points.Select((p,i)=>new LevelLightSource("lamp-"+i,LevelLightSourceKind.Lamp,new[]{new Vector4(p.x,p.y,1,0)},colors[i],.4f,4,
                new Rect(p.x-.1f,p.y-.1f,.2f,.2f),scene.Texels(new Rect(p.x-4,p.y-4,8,8)),true)).ToArray();
            return new LevelLight(scene,new LevelLightSources(sources,width,height),.2f,directCacheBytes:60000);
        }
        private static void Check(Report report)
        {
            using var light=Create();light.BuildAll();var polar=(PolarDirectLight)light.Direct;
            report.format=((RenderTexture)polar.Sum).graphicsFormat.ToString();report.sumBytes=polar.SumBytes;report.additionalSumBytes=polar.SumBytes/2;
            int builds=polar.Builds;light.SetDark(new[]{"lamp-0"});
            Require(light.FastLampUpdates==1 && !light.HasWork && polar.Builds==builds+1 && polar.LastBuiltIds.SequenceEqual(new[]{"lamp-0"}),"Холодное выключение должно строить только вытесненное поле лампы.");report.cacheMiss=true;
            var fastOff=Read(polar.Sum);var fillOff=Read(light.Fill.Texture);
            light.BuildAll();var reference=Read(polar.Sum);var referenceFill=Read(light.Fill.Texture);
            report.maximumError=Difference(fastOff,reference);report.fillError=Difference(fillOff,referenceFill);
            Require(report.maximumError<.001f && report.fillError==0,"Вычитание яркой лампы потеряло слабого соседа или исказило заполнение: "+report.maximumError+" / "+report.fillError);
            Require(reference.Any(c=>c.r>0 && c.r<.1f),"Слабый сосед не присутствует в контрольном поле.");report.weakNeighbour=true;
            report.cycles=16;
            for(int cycle=0;cycle<report.cycles;cycle++)
            {
                light.SetDark(Array.Empty<string>());Finish(light);light.SetDark(new[]{"lamp-0"});
                Require(!light.HasWork,"Повторное одиночное выключение осталось в очереди.");
                report.maximumError=Math.Max(report.maximumError,Difference(Read(polar.Sum),reference));
            }
            Require(report.maximumError<.001f,"Повторные переключения накопили ошибку: "+report.maximumError);report.repeatedToggles=true;
            light.SetDark(new[]{"lamp-0","lamp-1"});Require(!light.HasWork,"Второе последовательное одиночное выключение не прошло быстрый путь.");
            var both=Read(polar.Sum);var bothFill=Read(light.Fill.Texture);light.BuildAll();
            Require(Difference(both,Read(polar.Sum))<.001f && Difference(bothFill,Read(light.Fill.Texture))==0,"Последовательные выключения оставили остаточный свет.");report.sequentialOff=true;
            light.SetDark(Array.Empty<string>());Finish(light);int previous=light.FastLampUpdates;
            var door=new LevelLightScene.Door("busy-door",new Rect(30.2f,18,.2f,4),1,true);
            light.SetDoors(new[]{door});Require(light.HasPendingPublication,"Дверь не поставила геометрию в очередь.");
            light.SetDark(new[]{"lamp-0"});Require(light.FastLampUpdates==previous && light.HasPendingPublication,"Быстрый путь обошёл незавершённое событие двери.");Finish(light);
            var afterDoor=Read(polar.Sum);light.BuildAll();Require(Difference(afterDoor,Read(polar.Sum))==0,"Дверь и выключение дали другой свет, чем полная сборка.");report.busyDoor=true;
            light.SetDark(Array.Empty<string>());Finish(light);previous=light.FastLampUpdates;
            light.SetDark(new[]{"lamp-0","lamp-1"});Require(light.FastLampUpdates==previous && light.HasWork,"Batch двух ламп обошёл одиночный бюджет.");Finish(light);report.batchQueued=true;
            // Preflight is read-only, including the enabled seed state and the pending source command.
            light.Sources.All[2].Enabled=false;light.Fill.SourceChanged(2);long revision=light.Fill.Revision;
            var old=Read(light.Fill.Texture);bool enabled=light.Fill.Sources.All[2].Enabled;
            Require(!light.Fill.CanCompleteSourceChanges(1,1) && !light.Fill.TryCompleteSourceChanges(1,1) && light.Fill.HasWork && light.Fill.Revision==revision && light.Fill.Sources.All[2].Enabled==enabled && Difference(old,Read(light.Fill.Texture))==0,
                "Неуспешная предварительная проверка изменила заполнение или потеряла очередь.");report.fillPreflight=true;
            report.fastUpdates=light.FastLampUpdates;
        }
        private static void CheckSmall(Report report)
        {
            var scene=new LevelLightScene(new float[100,80],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            var source=new LevelLightSource("small",LevelLightSourceKind.Lamp,new[]{new Vector4(3,4,1,0)},Color.white,.3f,4,new Rect(2.9f,3.9f,.2f,.2f),new RectInt(0,0,100,80),true);
            using var light=new LevelLight(scene,new LevelLightSources(new[]{source},100,80),.2f);light.BuildAll();light.SetDark(new[]{source.Id});
            Require(light.FastLampUpdates==0 && light.HasWork,"Малая сцена синхронно пересчитана целиком через быстрый путь.");Finish(light);report.smallSceneQueued=true;
        }
        private static Color[] Read(Texture texture)
        {
            var request=AsyncGPUReadback.Request(texture,0,TextureFormat.RGBAFloat);request.WaitForCompletion();
            if(request.hasError)throw new InvalidOperationException("Ошибка GPU-проверки быстрого выключения лампы.");return request.GetData<Color>().ToArray();
        }
        private static float Difference(Color[] a,Color[] b)
        {
            Require(a.Length==b.Length,"Несовпадающие размеры контрольных полей.");float maximum=0;
            for(int i=0;i<a.Length;i++)maximum=Math.Max(maximum,Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b))));return maximum;
        }
        private static void Finish(LevelLight light){int steps=0;while(light.HasWork){light.Step(2,2);if(++steps>10000)throw new InvalidOperationException("Не завершилась очередь проверки лампы.");}}
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
