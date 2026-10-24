using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // PNG captures only: this diagnostic never imports or modifies project textures.
    public static class TextureSizingComparison
    {
        [Serializable] public sealed class Frame
        {
            public string path;
            public int width,height;
            public double mae,rmse,pixelsAboveThreshold,alphaMismatchFraction;
            public double[] channelMae,channelRmse;
            public int[] channelMax;
            public string difference;
        }
        [Serializable] public sealed class Report
        {
            public string before,after;
            public int threshold=8,differenceAmplification=8;
            public string metric="RGBA 8-bit sRGB values; MAE/RMSE normalized to 0..1; pixel threshold is max absolute RGBA error > threshold; alpha mismatch means any alpha byte difference. Not a perceptual acceptance gate.";
            public List<Frame> frames=new();
        }

        // -executeMethod Remains.Editor.TextureSizingComparison.RunBatch
        // -textureBefore <folder> -textureAfter <folder> [-textureComparisonOut <folder>]
        public static void RunBatch()
        {
            try
            {
                string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
                var report=Compare(Argument("-textureBefore"),Argument("-textureAfter"),
                    Argument("-textureComparisonOut")??Path.Combine(root,".reference/texture-sizing"));
                Debug.Log($"Сравнение текстур: проверено кадров={report.frames.Count}; метрики и усиленные различия сохранены.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static Report Compare(string before,string after,string output)
        {
            if(string.IsNullOrEmpty(before) || string.IsNullOrEmpty(after))
                throw new ArgumentException("Нужны -textureBefore и -textureAfter с папками PNG одинаковых кадров.");
            before=Path.GetFullPath(before);after=Path.GetFullPath(after);output=Path.GetFullPath(output);
            if(!Directory.Exists(before) || !Directory.Exists(after)) throw new DirectoryNotFoundException($"Нет папки сравнения: before={before}, after={after}.");
            var left=Files(before);var right=Files(after);
            if(left.Length==0 || !left.SequenceEqual(right,StringComparer.Ordinal))
                throw new InvalidOperationException("Наборы PNG до/после пусты или различаются: "+string.Join(", ",left.Except(right).Concat(right.Except(left))));
            // Keeping reports outside both input trees makes repeated comparisons deterministic.
            if(Inside(output,before) || Inside(output,after))
                throw new ArgumentException("Выход сравнения должен находиться вне папок PNG до/после.");
            Directory.CreateDirectory(output);
            var report=new Report{before=before,after=after};
            foreach(string relative in left)
            {
                Texture2D a=null,b=null;
                try
                {
                    a=Read(Path.Combine(before,relative));b=Read(Path.Combine(after,relative));
                    report.frames.Add(Measure(a,b,relative,Path.Combine(output,"differences",relative),report.threshold,report.differenceAmplification));
                }
                finally {if(a!=null) UnityEngine.Object.DestroyImmediate(a);if(b!=null) UnityEngine.Object.DestroyImmediate(b);}
            }
            File.WriteAllText(Path.Combine(output,"comparison.json"),JsonUtility.ToJson(report,true));
            return report;
        }

        // Metrics of one frame pair (shared by the texture sizing comparison and RvgReferenceComparison) and its amplified
        // difference image written to diffPath (null: metrics only).
        public static Frame Measure(Texture2D a,Texture2D b,string relative,string diffPath,int threshold=8,int amplification=8)
        {
            if(a.width!=b.width || a.height!=b.height) throw new InvalidOperationException($"Размер кадров различается: {relative}: {a.width}×{a.height} и {b.width}×{b.height}.");
            var x=a.GetPixels32();var y=b.GetPixels32();var differences=new Color32[x.Length];
            var sum=new double[4];var squares=new double[4];var maximum=new int[4];int above=0,alpha=0;
            for(int i=0;i<x.Length;i++)
            {
                int r=Math.Abs(x[i].r-y[i].r),g=Math.Abs(x[i].g-y[i].g),bl=Math.Abs(x[i].b-y[i].b),al=Math.Abs(x[i].a-y[i].a);
                Add(0,r);Add(1,g);Add(2,bl);Add(3,al);
                if(Math.Max(Math.Max(r,g),Math.Max(bl,al))>threshold) above++;
                if(al!=0) alpha++;
                // Alpha error is also made visible in the opaque RGB diagnostic.
                differences[i]=new Color32(Boost(Math.Max(r,al)),Boost(Math.Max(g,al)),Boost(Math.Max(bl,al)),255);
            }
            Texture2D diff=null;
            if(diffPath!=null) try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath));
                diff=new Texture2D(a.width,a.height,TextureFormat.RGBA32,false,false);diff.SetPixels32(differences);diff.Apply();
                File.WriteAllBytes(diffPath,diff.EncodeToPNG());
            }
            finally {if(diff!=null) UnityEngine.Object.DestroyImmediate(diff);}
            return new Frame{path=relative,width=a.width,height=a.height,
                mae=sum.Sum()/(x.Length*4d*255),rmse=Math.Sqrt(squares.Sum()/(x.Length*4d))/255,
                channelMae=sum.Select(v=>v/x.Length/255).ToArray(),channelRmse=squares.Select(v=>Math.Sqrt(v/x.Length)/255).ToArray(),
                channelMax=maximum,pixelsAboveThreshold=above/(double)x.Length,alphaMismatchFraction=alpha/(double)x.Length,difference=diffPath};
            void Add(int channel,int error) {sum[channel]+=error;squares[channel]+=error*error;maximum[channel]=Math.Max(maximum[channel],error);}
            byte Boost(int error)=>(byte)Math.Min(255,error*amplification);
        }
        public static Texture2D Read(string path)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
            try
            {
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false)) throw new InvalidOperationException("Не удалось прочитать PNG: "+path);
                return texture;
            }
            catch {UnityEngine.Object.DestroyImmediate(texture);throw;}
        }
        private static string[] Files(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories)
            .Where(p=>p.EndsWith(".png",StringComparison.OrdinalIgnoreCase)).Select(p=>Path.GetRelativePath(root,p).Replace('\\','/'))
            .OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        private static bool Inside(string candidate,string root)=>candidate.Equals(root,StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        private static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);
            return index>=0 && index+1<args.Length?args[index+1]:null;
        }
    }
}
