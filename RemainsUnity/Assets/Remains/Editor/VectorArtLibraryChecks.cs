using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Content.VectorArt;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // The shipped vector package against the PNG sprites it replaces (GPU; edit mode, so GameAssets still gives the PNGs):
    //  1. every covered key keeps its world registration: left and top edges equal (PNGs are padded right/bottom only);
    //  2. a sample at zoom 8 matches the PNG pixels (the right frame of the right resource), and its sprite has the PNG's bounds;
    //  3. rasterizing every covered key at the 1080p and 4K game scales — the first-run cost.
    // -executeMethod Remains.Editor.VectorArtLibraryChecks.RunBatch [-vectorSample 40]
    public static class VectorArtLibraryChecks
    {
        public static void RunBatch()
        {
            try {Run(int.Parse(Argument("-vectorSample")??"40"));EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run(int samples)
        {
            var package=VectorArtPackage.Load(VectorArtPackaging.Destination);
            var catalog=ExportedContentCatalog.Load();
            using var library=new VectorArtLibrary(package,catalog,1080/VectorArtLibrary.ScreenHeightPerZoom*VectorArtLibrary.DefaultQuality);
            var keys=library.Keys.OrderBy(k=>k,StringComparer.Ordinal).ToList();
            if(keys.Count<1000) throw new InvalidOperationException($"Векторная графика покрывает только {keys.Count} ключей каталога.");

            // 1. Registration of every key, from the frame data.
            const float png=1/(VectorArtLibrary.SourcePixelsPerUnit*VectorArtLibrary.SourceZoom);
            int checkedKeys=0;
            foreach(string key in keys)
            {
                var sprite=GameAssets.Load<Sprite>(key)??throw new InvalidOperationException("Нет PNG-спрайта для ключа "+key);
                var frame=library.Frame(key);
                float left=-frame.origin[0]/VectorArtLibrary.SourcePixelsPerUnit,top=frame.origin[1]/VectorArtLibrary.SourcePixelsPerUnit;
                float width=frame.size[0]/VectorArtLibrary.SourcePixelsPerUnit,height=frame.size[1]/VectorArtLibrary.SourcePixelsPerUnit;
                var b=sprite.bounds;
                if(Mathf.Abs(b.min.x-left)>png*1.01f || Mathf.Abs(b.max.y-top)>png*1.01f)
                    throw new InvalidOperationException($"Регистрация {key}: PNG слева {b.min.x}, сверху {b.max.y}; вектор {left}, {top}.");
                if(width>b.size.x+png*1.01f || width<b.size.x-png*4.01f || height>b.size.y+png*1.01f || height<b.size.y-png*4.01f)
                    throw new InvalidOperationException($"Размер {key}: PNG {b.size.x}×{b.size.y}, вектор {width}×{height}.");
                checkedKeys++;
            }

            // 2. A spread sample: pixels at zoom 8 against the PNG file, and the game sprite's bounds.
            double worst=0;string worstKey=null;
            for(int i=0;i<samples;i++)
            {
                string key=keys[(int)((long)i*keys.Count/samples)];
                var rendered=library.Draw(key,VectorArtLibrary.SourceZoom,true);
                Texture2D file=null,cropped=null;
                try
                {
                    string path=Path.Combine(Application.dataPath,"Remains/ImportedContent/Art",key.Substring("Imported/Art/".Length)+".png");
                    file=TextureSizingComparison.Read(path);
                    if(file.width<rendered.width || file.height<rendered.height || file.width>rendered.width+3 || file.height>rendered.height+3)
                        throw new InvalidOperationException($"Холст {key}: PNG {file.width}×{file.height}, вектор {rendered.width}×{rendered.height}.");
                    // The padded PNG keeps the image at the top-left: Unity rows are bottom-up.
                    cropped=new Texture2D(rendered.width,rendered.height,TextureFormat.RGBA32,false,false);
                    cropped.SetPixels32(Crop(file.GetPixels32(),file.width,file.height,rendered.width,rendered.height));cropped.Apply(false);
                    var metric=TextureSizingComparison.Measure(cropped,rendered,key,null);
                    if(metric.mae>worst) {worst=metric.mae;worstKey=key;}
                    if(metric.mae>VectorArtPackaging.TargetMae) throw new InvalidOperationException($"Картинка {key}: MAE {metric.mae:F4} против PNG — не тот кадр или регистрация.");
                }
                finally {UnityEngine.Object.DestroyImmediate(rendered);if(file!=null) UnityEngine.Object.DestroyImmediate(file);if(cropped!=null) UnityEngine.Object.DestroyImmediate(cropped);}
                if(!library.TryGet(key,out var vector)) throw new InvalidOperationException("Библиотека не дала спрайт "+key);
                var expected=GameAssets.Load<Sprite>(key).bounds;var actual=vector.bounds;
                float tolerance=png+1/(VectorArtLibrary.SourcePixelsPerUnit*library.Zoom);
                if(Mathf.Abs(actual.min.x-expected.min.x)>tolerance || Mathf.Abs(actual.max.y-expected.max.y)>tolerance)
                    throw new InvalidOperationException($"Спрайт {key}: границы {actual}, у PNG {expected}.");
            }

            // 3. First-run cost: every key at the 1080p and 4K game scales (GPU-only, synchronised by a readback at the end).
            string Time(float zoom)
            {
                var watch=Stopwatch.StartNew();long pixels=0;
                foreach(string key in keys)
                {
                    var texture=library.Draw(key,zoom,false);pixels+=(long)texture.width*texture.height;
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                UnityEngine.Object.DestroyImmediate(library.Draw(keys[0],1,true));
                return $"{zoom:F2}× — {watch.Elapsed.TotalSeconds:F1} с, {pixels/1e6:F0} Мпикс";
            }
            float zoom1080=1080/VectorArtLibrary.ScreenHeightPerZoom*VectorArtLibrary.DefaultQuality;
            string time1080=Time(zoom1080),time4k=Time(zoom1080*2);
            Debug.Log($"Векторная графика: PASS — ключей {keys.Count}, регистрация {checkedKeys}, выборка {samples} (худшая MAE {worst:F4}, {worstKey}), "+
                $"растровых исключений {package.RasterResources.Count}; растеризация всех ключей: 1080p {time1080}; 4K {time4k}.");
        }
        private static Color32[] Crop(Color32[] source,int sourceWidth,int sourceHeight,int width,int height)
        {
            var result=new Color32[width*height];
            for(int y=0;y<height;y++) Array.Copy(source,(sourceHeight-height+y)*sourceWidth,result,y*width,width);
            return result;
        }
        private static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,name);
            return at>=0 && at+1<args.Length?args[at+1]:null;
        }
    }
}
