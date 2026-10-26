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
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Whole-level direct light (stage 1 of the hybrid light) on the seed104 factory: a door or lamp event rebuilds
    // only what it should, and the summed light after the event equals a full rebuild bit for bit; a small cache
    // still gives the same light. Timings are submission plus a blocking one-pixel readback, not GPU timestamps.
    public static class LevelLightChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            GameObject visual=null;LevelLight light=null;DirectLight reference=null;
            var fence=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            try
            {
                Anatomy.Load();WeaponCatalog.Load();
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");
                var authoring=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));
                var room=authoring.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();
                view.Synchronize(room,world,1);

                var clock=Stopwatch.StartNew();
                light=view.CreateLevelLight();
                double prepareMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();light.Direct.BuildAll();Fence(light.Direct.Sum,fence);double buildMs=clock.Elapsed.TotalMilliseconds;
                light.Direct.WaitForMeasurements();
                var sources=light.Sources.All;
                int enabled=sources.Count(s=>s.Enabled);
                Debug.Log($"Свет уровня: подготовка {prepareMs:F0} мс (стены, формы, сцена), прямой свет всех {enabled} включённых источников {buildMs:F0} мс "+
                    $"({buildMs/Math.Max(1,enabled):F2} мс на источник). Ламп {sources.Count(s=>s.Kind==LevelLightSourceKind.Lamp)}, окон {sources.Count(s=>s.Kind==LevelLightSourceKind.Window)}, "+
                    $"экранов {sources.Count(s=>s.Kind==LevelLightSourceKind.Screen)}. Кеш полей {light.Direct.CacheBytes/1048576.0:F1} МиБ ({light.Direct.CachedFields} полей, вытеснено {light.Direct.Evictions}), "+
                    $"сумма {light.Direct.SumBytes/1048576.0:F1} МиБ, сцена {light.Scene.Bytes/1048576.0:F1} МиБ.");
                var initial=Read(light.Direct.Sum);
                if(!initial.Any(value=>value!=0)) throw new InvalidOperationException("Сумма прямого света пуста.");
                WritePreview(light,initial,new Vector2(room.spawnX,room.spawnY),"spawn");

                // Door: the closed door lit by the most sources; opening it rebuilds only its subscribers.
                var doors=light.Scene.Doors.Values.ToList();
                var subscriptions=new Dictionary<string,int>(StringComparer.Ordinal);
                for(int i=0;i<sources.Count;i++) foreach(string id in light.Direct.SubscribedDoors(i)??throw new InvalidOperationException("Подписка не измерена: "+sources[i].Id))
                    subscriptions[id]=subscriptions.TryGetValue(id,out int n)?n+1:1;
                var door=doors.Where(d=>d.Blocks && subscriptions.ContainsKey(d.Id)).OrderByDescending(d=>subscriptions[d.Id]).ThenBy(d=>d.Id,StringComparer.Ordinal).FirstOrDefault();
                if(door.Id==null) throw new InvalidOperationException("Нет закрытой освещённой двери для проверки.");
                var reach=new List<int>();light.Sources.Overlapping(light.Scene.Texels(door.Bounds),reach);
                var opened=doors.Select(d=>d.Id==door.Id?new LevelLightScene.Door(d.Id,d.Bounds,d.Opacity,false):d).ToList();
                var afterDoor=Event("дверь "+door.Id,()=>light.SetDoors(opened),light,fence,out int doorBuilds,out double doorMs);
                Debug.Log($"Дверь {door.Id}: подписано источников {subscriptions[door.Id]}, источников с дальностью над дверью {reach.Count}, перестроено полей {doorBuilds}, событие {doorMs:F1} мс.");
                if(doorBuilds>subscriptions[door.Id]) throw new InvalidOperationException($"Дверь перестроила {doorBuilds} полей при {subscriptions[door.Id]} подписанных.");
                if(Same(afterDoor,initial)) throw new InvalidOperationException("Открытие двери не изменило свет.");
                reference=Rebuilt(light,fence,"после двери");
                Require(afterDoor,Read(reference.Sum),"дверь: событие против полной пересборки");
                reference.Dispose();reference=null;

                // Lamp: switching off and on again restores the initial light exactly.
                var lamp=sources.First(s=>s.Kind==LevelLightSourceKind.Lamp && s.Enabled);
                var dark=new HashSet<string>(StringComparer.Ordinal){lamp.Id};
                var afterLamp=Event("лампа "+lamp.Id,()=>light.SetDark(dark),light,fence,out int lampBuilds,out double lampMs);
                if(Same(afterLamp,afterDoor)) throw new InvalidOperationException("Выключение лампы не изменило свет.");
                reference=Rebuilt(light,fence,"после лампы");
                Require(afterLamp,Read(reference.Sum),"лампа: событие против полной пересборки");
                reference.Dispose();reference=null;
                var restored=Event("лампа снова",()=>light.SetDark(new HashSet<string>(StringComparer.Ordinal)),light,fence,out int relitBuilds,out double relitMs);
                Require(restored,afterDoor,"лампа: включение возвращает прежний свет");
                Debug.Log($"Лампа {lamp.Id}: выключение — полей {lampBuilds}, {lampMs:F1} мс; включение — полей {relitBuilds}, {relitMs:F1} мс.");
                var closed=Event("дверь закрыта",()=>light.SetDoors(doors),light,fence,out _,out _);
                Require(closed,initial,"дверь: закрытие возвращает исходный свет");

                // A small cache: fields are evicted and rebuilt on demand, the light stays the same.
                light.Dispose();light=null;
                light=view.CreateLevelLight(24L*1024*1024);light.Direct.BuildAll();light.Direct.WaitForMeasurements();
                if(light.Direct.Evictions==0) throw new InvalidOperationException("Малый кеш не вытеснил ни одного поля.");
                Require(Read(light.Direct.Sum),initial,"малый кеш: полная сборка");
                var smallDoor=Event("дверь при малом кеше",()=>light.SetDoors(opened),light,fence,out int smallBuilds,out _);
                Require(smallDoor,afterDoor,"малый кеш: событие двери");
                Debug.Log($"Малый кеш 24 МиБ: вытеснено {light.Direct.Evictions}, событие двери построило {smallBuilds} полей.");
                Debug.Log("LevelLightChecks: PASS — события пересобирают только свои источники, сумма совпадает с полной пересборкой бит в бит, малый кеш даёт тот же свет.");
            }
            finally
            {
                reference?.Dispose();light?.Dispose();
                if(visual!=null) Object.DestroyImmediate(visual);
                Object.DestroyImmediate(fence);
                FactoryContentSources.Clear();RoomRadianceField.ClearSpareTextures();
            }
        }

        private static ushort[] Event(string name,Action change,LevelLight light,Texture2D fence,out int builds,out double milliseconds)
        {
            int before=light.Direct.Builds;var clock=Stopwatch.StartNew();
            change();
            int steps=0;
            while(light.Step(2)) {if(++steps>10000) throw new InvalidOperationException("Событие не завершилось: "+name);light.Direct.WaitForMeasurements();}
            Fence(light.Direct.Sum,fence);milliseconds=clock.Elapsed.TotalMilliseconds;
            light.Direct.WaitForMeasurements();
            builds=light.Direct.Builds-before;
            return Read(light.Direct.Sum);
        }
        private static DirectLight Rebuilt(LevelLight light,Texture2D fence,string name)
        {
            var direct=new DirectLight(light.Scene,light.Sources);
            try {direct.BuildAll();Fence(direct.Sum,fence);return direct;}
            catch {direct.Dispose();throw;}
        }
        private static void Require(ushort[] actual,ushort[] expected,string name)
        {
            if(actual.Length!=expected.Length) throw new InvalidOperationException(name+": разный размер суммы.");
            int differ=0,first=-1;
            for(int i=0;i<actual.Length;i++) if(actual[i]!=expected[i]) {differ++;if(first<0) first=i;}
            if(differ>0) throw new InvalidOperationException($"{name}: отличаются {differ} компонент, первая #{first} ({Mathf.HalfToFloat(actual[first])} против {Mathf.HalfToFloat(expected[first])}).");
            Debug.Log(name+": совпадает бит в бит.");
        }
        private static bool Same(ushort[] a,ushort[] b)
        {
            if(a.Length!=b.Length) return false;
            for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false;
            return true;
        }
        private static ushort[] Read(Texture texture)
        {
            var target=(RenderTexture)texture;var previous=RenderTexture.active;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBAHalf,false,true);
            try
            {
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);
                return image.GetRawTextureData<ushort>().ToArray();
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static void Fence(Texture texture,Texture2D pixel)
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=(RenderTexture)texture;pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
        }
        // A 60×34 m crop of the sum around a point, Reinhard-mapped, for a look by eye.
        private static void WritePreview(LevelLight light,ushort[] sum,Vector2 center,string name)
        {
            var scene=light.Scene;int w=600,h=340;
            int cx=Mathf.FloorToInt((center.x-scene.WorldRect.x)*scene.Resolution),cy=Mathf.FloorToInt((center.y-scene.WorldRect.y)*scene.Resolution);
            int x0=Mathf.Clamp(cx-w/2,0,Math.Max(0,scene.Width-w)),y0=Mathf.Clamp(cy-h/2,0,Math.Max(0,scene.Height-h));
            w=Math.Min(w,scene.Width);h=Math.Min(h,scene.Height);
            var image=new Texture2D(w,h,TextureFormat.RGBA32,false);
            try
            {
                var pixels=new Color32[w*h];
                for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                {
                    int i=((y0+y)*scene.Width+x0+x)*4;
                    var c=new Vector3(Mathf.HalfToFloat(sum[i]),Mathf.HalfToFloat(sum[i+1]),Mathf.HalfToFloat(sum[i+2]));
                    pixels[y*w+x]=new Color(c.x/(1+c.x),c.y/(1+c.y),c.z/(1+c.z),1);
                }
                image.SetPixels32(pixels);image.Apply(false);
                string folder=Path.GetFullPath("../.reference/level-light");Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,"direct-"+name+".png"),image.EncodeToPNG());
            }
            finally {Object.DestroyImmediate(image);}
        }
    }
}
