using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Content.VectorArt;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // The in-game rasterizer on the converter's RVG against the catalog's 8× reference PNGs (docs/pipelines/vector-format.md,
    // «Проверка и приёмка пробы»), plus the rasterization time: the estimate of the first run on a player's machine.
    // -executeMethod Remains.Editor.RvgReferenceComparison.RunBatch -rvgDir <folder with *.rvg.json> -catalogDir <catalog folder>
    //   [-rvgOut <folder>] [-rvgZoom 8] [-rvgLimit N] [-rvgWorst 50] [-rvgLinePixels 1]
    public static class RvgReferenceComparison
    {
        [Serializable] public sealed class FrameResult
        {
            public string resource,reference,problem;
            public int frame,width,height;
            public double mae,rmse,pixelsAboveThreshold,alphaMismatchFraction,milliseconds;
        }
        [Serializable] public sealed class Report
        {
            public float zoom;public float minLinePixels;
            public int frames,rendered,meetingTarget,sizeMismatch,failed;
            public double medianMae,p95Mae,medianAlphaMismatch,totalSeconds,megapixelsPerSecond;
            public List<string> approximations=new();
            public List<FrameResult> results=new();
        }
        public static void RunBatch()
        {
            try
            {
                string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
                var report=Compare(Argument("-rvgDir")??throw new ArgumentException("Нужен -rvgDir с файлами *.rvg.json."),
                    Argument("-catalogDir")??Path.Combine(root,".reference/content-catalog-v9"),
                    Argument("-rvgOut")??Path.Combine(root,".reference/vector-probe/game-rasterizer"),
                    float.Parse(Argument("-rvgZoom")??"8",System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(Argument("-rvgLimit")??"0"),int.Parse(Argument("-rvgWorst")??"50"),
                    float.Parse(Argument("-rvgLinePixels")??"1",System.Globalization.CultureInfo.InvariantCulture));
                Debug.Log($"RVG против эталонов: кадров {report.frames}, растрировано {report.rendered}, в цели {report.meetingTarget}, несовпадение размера {report.sizeMismatch}, ошибок {report.failed}; "+
                    $"MAE медиана {report.medianMae:F4}, 95% {report.p95Mae:F4}; время {report.totalSeconds:F1} с, {report.megapixelsPerSecond:F1} Мпикс/с.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static Report Compare(string rvgDir,string catalogDir,string output,float zoom,int limit,int worst,float minLinePixels)
        {
            rvgDir=Path.GetFullPath(rvgDir);catalogDir=Path.GetFullPath(catalogDir);output=Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            var report=new Report {zoom=zoom,minLinePixels=minLinePixels};
            var loaded=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
            Texture2D LoadBitmap(RvgBitmap bitmap)
            {
                string path=Path.Combine(rvgDir,bitmap.file);
                if(loaded.TryGetValue(path,out var texture)) return texture;
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                if(!texture.LoadImage(File.ReadAllBytes(path),false)) throw new InvalidDataException("Не прочитан растр RVG: "+path);
                loaded.Add(path,texture);return texture;
            }
            var documents=Directory.GetFiles(rvgDir,"*.rvg.json").OrderBy(p=>p,StringComparer.Ordinal)
                .Select(p=>RvgDocument.Parse(File.ReadAllText(p),Path.GetFileName(p))).ToList();
            using var rasterizer=new RvgRasterizer(LoadBitmap) {MinLinePixels=minLinePixels};
            var watch=new Stopwatch();double pixels=0;
            try
            {
                foreach(var document in documents)
                    foreach(var resource in document.resources)
                        foreach(var frame in resource.frames)
                        {
                            if(limit>0 && report.frames>=limit) break;
                            report.frames++;
                            var result=new FrameResult {resource=resource.id,frame=frame.frame,reference=frame.reference};
                            report.results.Add(result);
                            Texture2D rendered=null,reference=null;
                            try
                            {
                                watch.Restart();rendered=rasterizer.Render(document,resource,frame,zoom);watch.Stop();
                                result.milliseconds=watch.Elapsed.TotalMilliseconds;result.width=rendered.width;result.height=rendered.height;
                                report.totalSeconds+=watch.Elapsed.TotalSeconds;pixels+=rendered.width*(double)rendered.height;report.rendered++;
                                if(string.IsNullOrEmpty(frame.reference)) {result.problem="нет эталона";continue;}
                                reference=TextureSizingComparison.Read(Path.Combine(catalogDir,frame.reference));
                                if(reference.width!=rendered.width || reference.height!=rendered.height)
                                {result.problem=$"размер {rendered.width}×{rendered.height}, эталон {reference.width}×{reference.height}";report.sizeMismatch++;continue;}
                                var metric=TextureSizingComparison.Measure(reference,rendered,resource.id+"#"+frame.frame,null);
                                result.mae=metric.mae;result.rmse=metric.rmse;result.pixelsAboveThreshold=metric.pixelsAboveThreshold;result.alphaMismatchFraction=metric.alphaMismatchFraction;
                            }
                            catch(Exception error) when(error is InvalidDataException || error is NotSupportedException)
                            {result.problem=error.Message;report.failed++;}
                            finally {if(rendered!=null) UnityEngine.Object.DestroyImmediate(rendered);if(reference!=null) UnityEngine.Object.DestroyImmediate(reference);}
                        }
                var measured=report.results.Where(r=>r.problem==null).ToList();
                // Target of the contract: MAE ≤ 0.01 and alpha differing on at most 1% of the area.
                report.meetingTarget=measured.Count(r=>r.mae<=.01 && r.alphaMismatchFraction<=.01);
                if(measured.Count>0)
                {
                    var mae=measured.Select(r=>r.mae).OrderBy(v=>v).ToList();var alpha=measured.Select(r=>r.alphaMismatchFraction).OrderBy(v=>v).ToList();
                    report.medianMae=mae[mae.Count/2];report.p95Mae=mae[Math.Min(mae.Count-1,(int)(mae.Count*.95))];report.medianAlphaMismatch=alpha[alpha.Count/2];
                }
                report.megapixelsPerSecond=report.totalSeconds>0?pixels/1e6/report.totalSeconds:0;
                report.approximations=rasterizer.Approximations.OrderByDescending(p=>p.Value).Select(p=>$"{p.Key}: {p.Value}").ToList();
                // The worst frames as triples: reference, game rasterizer, amplified difference.
                foreach(var result in measured.OrderByDescending(r=>r.mae).Take(worst))
                {
                    var document=documents.First(d=>d.TryResource(result.resource,out _));
                    document.TryResource(result.resource,out var resource);
                    var frame=resource.frames.First(f=>f.frame==result.frame);
                    string name=result.resource.Replace(':','_')+"_"+result.frame;
                    Texture2D rendered=null,reference=null;
                    try
                    {
                        rendered=rasterizer.Render(document,resource,frame,zoom);reference=TextureSizingComparison.Read(Path.Combine(catalogDir,frame.reference));
                        string folder=Path.Combine(output,"worst");Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder,name+"-render.png"),rendered.EncodeToPNG());
                        File.Copy(Path.Combine(catalogDir,frame.reference),Path.Combine(folder,name+"-reference.png"),true);
                        TextureSizingComparison.Measure(reference,rendered,name,Path.Combine(folder,name+"-difference.png"));
                    }
                    finally {if(rendered!=null) UnityEngine.Object.DestroyImmediate(rendered);if(reference!=null) UnityEngine.Object.DestroyImmediate(reference);}
                }
            }
            finally {foreach(var texture in loaded.Values) UnityEngine.Object.DestroyImmediate(texture);}
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            return report;
        }
        private static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,name);
            return at>=0 && at+1<args.Length?args[at+1]:null;
        }
    }
}
