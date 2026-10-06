using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // EditMode checks use real readable Unity pixels and native colour conversion on Task.Run.
    // They neither publish shader maps nor claim Play-mode coroutine/GPU lifecycle coverage.
    public static class RoomLightingPreparationChecks
    {
        [Serializable] private sealed class Evidence
        {
            public int schema=1,width,height,uniqueTextures;
            public long decodedPixels;
            public bool frozenCapture,singleUse,preCancelled,inFlightCancelled,concurrentSingleWinner,latestDoorBeforePublication;
            public double prepareMilliseconds,getPixelsMilliseconds,decodeMilliseconds,evaluateMilliseconds;
            public string limitation="Проверены BuildPlan, настоящий Unity GetPixels и расчёт Task.Run. Play-mode coroutine, смена камеры и публикация GPU-карт требуют отдельного runtime сценария.";
        }
        public static void Run()
        {
            var result=CheckFrozenCapture();
            CheckCancellation();CheckConcurrentAttempt();CheckLatestOcclusion();
            result.singleUse=result.preCancelled=result.inFlightCancelled=result.concurrentSingleWinner=result.latestDoorBeforePublication=true;
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/room-lighting-preparation-r44"));
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"report.json"),StateJson.ToJson(result,true));
            Debug.Log("Подготовка света: PASS — actual Unity pixels/native math в Task.Run, frozen capture после удаления текстуры, single-use/отмена начатой работы/конкурентный вызов, latest door перед первой публикацией. Coroutine/GPU lifecycle отдельно.");
        }
        private static Evidence CheckFrozenCapture()
        {
            var texture=Texture();
            try
            {
                var room=Room();var profile=Profile(texture);var bounds=new Rect(0,0,4.13f,3.17f);
                var expected=new RoomLightMaps(room,bounds,profile);
                var plan=RoomLightMaps.PrepareBuild(room,bounds,profile);
                Require(plan.UniqueDecodedTextures==1 && plan.DecodedPixels==128 && !plan.IsConsumed,
                    "общая текстура трёх светильников/FG/BG не захвачена ровно один раз");
                RequireNoUnityReferences(plan,new HashSet<object>());
                // All live inputs change after capture; the plan must retain its original values.
                room.platforms[0].x=99;room.platforms=Array.Empty<RoomSurface>();
                profile.ambient=Color.black;profile.backgroundAmbientScale=0;profile.lights[0].intensity=0;
                profile.lights[1].x=99;profile.lights=Array.Empty<RoomLight>();
                texture.SetPixels(new Color[128]);texture.Apply(false,false);
                UnityEngine.Object.DestroyImmediate(texture);texture=null;
                var actual=Task.Run(()=>plan.Build()).GetAwaiter().GetResult();
                Require(plan.IsConsumed,"успешный Build не пометил план использованным");
                Same(actual,expected,"изменение/удаление live inputs после PrepareBuild");
                Repeated(plan);
                return new Evidence {width=actual.Width,height=actual.Height,uniqueTextures=actual.UniqueDecodedTextures,
                    decodedPixels=actual.DecodedPixels,frozenCapture=true,prepareMilliseconds=actual.PrepareMilliseconds,
                    getPixelsMilliseconds=actual.GetPixelsMilliseconds,decodeMilliseconds=actual.DecodeMilliseconds,evaluateMilliseconds=actual.EvaluateMilliseconds};
            }
            finally {if(texture!=null) UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static void CheckCancellation()
        {
            var plain=PlainProfile();
            var plan=RoomLightMaps.PrepareBuild(Room(),new Rect(0,0,2,2),plain);
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();
                try {Task.Run(()=>plan.Build(cancelled.Token)).GetAwaiter().GetResult();Require(false,"отмена до Build проигнорирована");}
                catch(OperationCanceledException) { }
            }
            Require(plan.IsConsumed,"отменённый вызов оставил план повторно используемым");Repeated(plan);

            // Distant volumes cannot short-circuit a ray. This bounds native work but provides
            // enough full-evaluator work to cancel an actually entered attempt, without timers.
            var surfaces=new RoomSurface[2048];
            for(int i=0;i<surfaces.Length;i++) surfaces[i]=new RoomSurface {x=100+i*.25f,y=100,width=.1f,height=.1f};
            var room=new RoomLayout {id="lighting-in-flight-cancellation",platforms=surfaces};
            var active=RoomLightMaps.PrepareBuild(room,new Rect(0,0,8,8),PlainProfile());
            using(var cancellation=new CancellationTokenSource())
            {
                var task=Task.Run(()=>active.Build(cancellation.Token));
                Require(SpinWait.SpinUntil(()=>active.IsConsumed || task.IsCompleted,5000),"worker не начал Build за 5 секунд");
                Require(active.IsConsumed && !task.IsCompleted,"fixture завершился до отмены: проверка начатой работы не выполнена");
                cancellation.Cancel();
                try {task.GetAwaiter().GetResult();Require(false,"начатый Build не наблюдает отмену");}
                catch(OperationCanceledException) { }
            }
            Repeated(active);
        }
        private static void CheckConcurrentAttempt()
        {
            var plan=RoomLightMaps.PrepareBuild(Room(),new Rect(0,0,2,2),PlainProfile());
            int winners=0,rejected=0;
            Action attempt=()=>
            {
                try {plan.Build();Interlocked.Increment(ref winners);}
                catch(InvalidOperationException) {Interlocked.Increment(ref rejected);}
            };
            Task.WaitAll(Task.Run(attempt),Task.Run(attempt));
            Require(winners==1 && rejected==1,"конкурентный Build получил несколько владельцев raw buffers");
        }
        private static void CheckLatestOcclusion()
        {
            var texture=Texture();
            try
            {
                var room=Room();var profile=Profile(texture);var bounds=new Rect(0,0,4.13f,3.17f);
                var plan=RoomLightMaps.PrepareBuild(room,bounds,profile);
                room.platforms=Array.Empty<RoomSurface>();
                var opened=RoomLightMaps.CaptureOcclusion(room.platforms);
                var current=Task.Run(()=>plan.Build()).GetAwaiter().GetResult();
                var closedForeground=new Color[current.Foreground.Count];var closedBackground=new Color[closedForeground.Length];
                current.CopyPixels(closedForeground,closedBackground);
                var patch=Task.Run(()=>current.CreatePatch(opened)).GetAwaiter().GetResult();
                Require(!patch.IsEmpty,"обычный blocker fixture не дал initial patch");
                Require(current.PublishedVersion==0,"initial patch изменил baseline до Commit");
                for(int i=0;i<closedForeground.Length;i++)
                    Require(Exact(closedForeground[i],current.Foreground[i]) && Exact(closedBackground[i],current.Background[i]),
                        "initial patch изменил pixels до Commit: "+i);
                Require(current.Commit(patch),"latest door после initial Build не принят");
                var expected=new RoomLightMaps(room,bounds,profile);
                Same(current,expected,"latest door до первой GPU публикации");
                bool changed=false;
                for(int i=0;i<closedForeground.Length;i++)
                    if(!Exact(closedForeground[i],current.Foreground[i]) || !Exact(closedBackground[i],current.Background[i])) {changed=true;break;}
                Require(changed && current.PublishedVersion==1 && patch.DirtyPixels>0,"latest geometry не изменила опубликованный CPU свет");
            }
            finally {UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static Texture2D Texture()
        {
            var texture=new Texture2D(16,8,TextureFormat.RGBA32,false,false) {name="Маска проверки подготовки света"};
            var colors=new Color[128];
            for(int i=0;i<colors.Length;i++) colors[i]=new Color(.5f+(i%3)*.1f,.35f,.2f,.65f);
            texture.SetPixels(colors);texture.Apply(false,false);return texture;
        }
        private static RoomLayout Room()=>new() {id="lighting-preparation-check",platforms=new[]{new RoomSurface {x=1.5f,y=0,width=.2f,height=3.17f}}};
        private static RoomLightProfile Profile(Texture2D texture)=>new() {ambient=new Color(.2f,.3f,.4f),backgroundAmbientScale=.47f,
            lights=new[]{new RoomLight {id="fixture-a",x=.5f,y=1,radius=4,intensity=1.7f,backgroundIntensity=.4f,
                maskTexture=texture,maskX=0,maskY=0,maskWidth=4.13f,maskHeight=3.17f},
                new RoomLight {id="fixture-b",x=.7f,y=2,radius=4,intensity=1.1f,backgroundIntensity=.3f,
                    maskTexture=texture,maskX=0,maskY=0,maskWidth=4.13f,maskHeight=3.17f},
                new RoomLight {id="overlay",x=0,y=0,radius=1,intensity=.35f,shadow=true,
                    maskTexture=texture,maskX=0,maskY=0,maskWidth=4.13f,maskHeight=3.17f}}};
        private static RoomLightProfile PlainProfile()=>new() {lights=new[]{new RoomLight {id="radial",x=4,y=4,radius=16,intensity=1}}};
        private static void Repeated(RoomLightMaps.BuildPlan plan)
        {
            bool rejected=false;
            try {plan.Build();} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"BuildPlan повторно декодирует принадлежащие первому вызову raw buffers");
        }
        private static bool Exact(Color a,Color b)=>a.r.Equals(b.r) && a.g.Equals(b.g) && a.b.Equals(b.b) && a.a.Equals(b.a);
        private static void Same(RoomLightMaps actual,RoomLightMaps expected,string context)
        {
            Require(actual.Width==expected.Width && actual.Height==expected.Height,"разная сетка: "+context);
            for(int i=0;i<actual.Foreground.Count;i++)
                Require(Exact(actual.Foreground[i],expected.Foreground[i]) && Exact(actual.Background[i],expected.Background[i]),
                    $"не совпали foreground/background: context={context}, pixel={i}");
        }
        private static void RequireNoUnityReferences(object value,HashSet<object> seen)
        {
            if(value==null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return;
            Require(!(value is UnityEngine.Object) && !(value is RoomLayout) && !(value is RoomLightProfile) && !(value is RoomLight),
                "BuildPlan удерживает live Unity/profile/geometry: "+value.GetType().FullName);
            if(!value.GetType().IsValueType && !seen.Add(value)) return;
            if(value is IDictionary dictionary)
            {foreach(DictionaryEntry entry in dictionary) {RequireNoUnityReferences(entry.Key,seen);RequireNoUnityReferences(entry.Value,seen);}return;}
            if(value is Array array) {foreach(var item in array) RequireNoUnityReferences(item,seen);return;}
            foreach(var field in value.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))
                RequireNoUnityReferences(field.GetValue(value),seen);
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Подготовка света: "+reason);}
    }
}
