using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Remains.Content;
using Remains.Diagnostics;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // Light window rebuild on the full factory (seed 104): the actor walks across the level, every window change is
    // timed (CPU, game thread) by its FrameHitchLog steps. Budget: a window change must fit one frame.
    //   batch (with graphics) -executeMethod Remains.Editor.LightWindowBench.RunBatch
    public static class LightWindowBench
    {
        // Batch mode presents no frames: wait for the submitted GPU work outside the timer, or later windows measure the
        // driver blocking on the queue left by earlier ones.
        private static void DrainGpu()
        {
            var radiance=Shader.GetGlobalTexture("_RemainsRadiance") as RenderTexture;
            if(radiance==null) return;
            var previous=RenderTexture.active;var pixel=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            try {RenderTexture.active=radiance;pixel.ReadPixels(new Rect(0,0,1,1),0,0);}
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixel);}
        }
        public static void RunBatch()
        {
            GameObject visual=null;
            try
            {
                FrameHitchLog.MeasureOnly();
                var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,catalog,0);
                FactoryContentSources.Prepare(world,"factory:0");
                var source=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(source,"factory:0"));
                var room=source.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();
                view.Synchronize(room,world,1);
                view.Focus(new Vector2(room.spawnX,room.spawnY));
                FrameHitchLog.TakeSections();
                // A serpentine walk over the whole level grid, twice: the first pass includes one-time measurements.
                var points=new List<Vector2>();bool forward=true;
                for(float y=room.bottom+1;y<room.top-1;y+=6,forward=!forward)
                    for(int i=0;i<(int)((room.right-room.left)/3);i++)
                        points.Add(new Vector2(forward?room.left+1+i*3:room.right-1-i*3,y));
                for(int pass=1;pass<=2;pass++)
                {
                    var totals=new Dictionary<string,double>(StringComparer.Ordinal);
                    var rebuilds=new List<double>();
                    var current=view.LightWindowRooms;
                    foreach(var point in points)
                    {
                        if(view.WindowFor(point).Equals(current)) continue;
                        var timer=Stopwatch.StartNew();
                        view.Focus(point);
                        rebuilds.Add(timer.Elapsed.TotalMilliseconds);
                        current=view.LightWindowRooms;
                        DrainGpu();
                        foreach(var section in FrameHitchLog.TakeSections())
                            totals[section.Name]=(totals.TryGetValue(section.Name,out double sum)?sum:0)+section.Milliseconds;
                    }
                    if(rebuilds.Count==0) throw new InvalidOperationException("Проход по заводу не сменил окно света.");
                    rebuilds.Sort();
                    string steps=string.Join("; ",totals.OrderByDescending(pair=>pair.Value).Select(pair=>$"{pair.Key} {pair.Value/rebuilds.Count:F1}"));
                    Debug.Log($"Окно света завода, проход {pass}: смен {rebuilds.Count}, среднее {rebuilds.Average():F1} мс, медиана {rebuilds[rebuilds.Count/2]:F1}, 90% {rebuilds[(int)(rebuilds.Count*.9)]:F1}, макс {rebuilds[^1]:F1}. По шагам (среднее на смену): {steps}");
                }
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);
                FactoryContentSources.Clear();
            }
        }
    }
}
