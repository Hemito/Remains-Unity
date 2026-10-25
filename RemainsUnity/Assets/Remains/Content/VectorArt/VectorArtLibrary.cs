using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Content.VectorArt
{
    // Catalog art drawn from the shipped vectors instead of the zoom-8 PNGs (docs/pipelines/vector-format.md, «Встраивание»).
    // An imported art key ("Imported/Art/<sha>") maps to its RVG resource frame through the exported content catalog; the
    // frame is rasterized at the player's screen scale on first request and kept for the session. Keys without a verified
    // vector frame (other archives, zoom-1 bitmaps, resources kept as rasters) stay with their PNG sprites.
    public sealed class VectorArtLibrary : IDisposable
    {
        // Source pixels per world metre and the reference camera: orthographic size 5 → a 1080p screen shows 2.16 screen
        // pixels per source pixel, i.e. Screen.height / 500.
        public const float SourcePixelsPerUnit=50,ScreenHeightPerZoom=500;
        // Headroom over the reference screen scale for objects drawn larger than 1:1 (weapons in hooves, icons).
        public const float DefaultQuality=1.5f;
        public const float SourceZoom=8;   // zoom of the catalog PNGs this replaces

        private static VectorArtLibrary active;
        private static Task<VectorArtPackage> loading;
        private static bool unavailable;

        // Starts reading the package off the main thread as the player starts (play mode in the Editor); edit mode keeps the
        // PNG assets, which authoring code serializes into scenes and prefabs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Begin()
        {
            active?.Dispose();active=null;unavailable=false;loading=null;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-noVectorArt")>=0) {Debug.Log("Векторная графика отключена (-noVectorArt): каталог рисуется из PNG.");unavailable=true;return;}
            string root=Path.Combine(Application.streamingAssetsPath,VectorArtPackage.Folder);
            if(!File.Exists(VectorArtPackage.ManifestPath(root)))
            {
                Debug.LogWarning("Нет пакета векторной графики "+root+": графика каталога рисуется из PNG (запасной путь до установки пакета).");
                unavailable=true;return;
            }
            loading=Task.Run(()=>VectorArtPackage.Load(root));
        }

        // The vector sprite of an imported art key, or false when the key has no verified vector frame.
        public static bool TryLoad(string key,out Sprite sprite)
        {
            sprite=null;
            if(unavailable || !Application.isPlaying || !key.StartsWith("Imported/Art/",StringComparison.Ordinal)) return false;
            if(active==null)
            {
                if(loading==null) Begin();
                if(unavailable) return false;
                var watch=Stopwatch.StartNew();
                VectorArtPackage package;
                try {package=loading.GetAwaiter().GetResult();}
                catch(Exception error)
                {
                    Debug.LogError("Пакет векторной графики не загружен, графика каталога рисуется из PNG: "+error);
                    unavailable=true;return false;
                }
                active=new VectorArtLibrary(package,ExportedContentCatalog.Load(),Screen.height/ScreenHeightPerZoom*DefaultQuality);
                Debug.Log($"Векторная графика: {active.Covered} ключей каталога из векторов, масштаб {active.Zoom:F2}, ожидание пакета {watch.ElapsedMilliseconds} мс; "+string.Join("; ",package.ProfileReport));
            }
            return active.TryGet(key,out sprite);
        }

        public readonly VectorArtPackage Package;
        public readonly float Zoom;
        private readonly Dictionary<string,(RvgDocument Document,RvgResource Resource,RvgResourceFrame Frame)> frames=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Sprite> sprites=new(StringComparer.Ordinal);
        private readonly List<Texture2D> loadedBitmaps=new();
        private RvgRasterizer rasterizer;
        public int Covered=>frames.Count;
        public IEnumerable<string> Keys=>frames.Keys;
        public double RasterSeconds {get;private set;}

        public VectorArtLibrary(VectorArtPackage package,ExportedContentCatalog catalog,float zoom)
        {
            Package=package??throw new ArgumentNullException(nameof(package));
            if(!float.IsFinite(zoom) || zoom<=0) throw new ArgumentOutOfRangeException(nameof(zoom));
            Zoom=Mathf.Min(zoom,SourceZoom);
            var documents=new Dictionary<string,RvgDocument>(StringComparer.Ordinal);
            foreach(var document in package.Documents) documents.Add(document.source.archive,document);
            int mismatched=0;
            foreach(var entry in catalog.resources)
            {
                int colon=entry.id.IndexOf(':');
                if(colon<0 || !documents.TryGetValue(entry.id.Substring(0,colon),out var document) || package.RasterResources.ContainsKey(entry.id)) continue;
                if(!document.TryResource(entry.id,out var resource)) continue;
                foreach(var picture in entry.frames??Array.Empty<ExportedFrame>())
                {
                    if(string.IsNullOrEmpty(picture.spriteResource) || picture.zoom!=SourceZoom || frames.ContainsKey(picture.spriteResource)) continue;
                    var frame=resource.frames.Find(f=>f.frame==picture.frame);
                    // A frame with content the converter could not translate (text) stays a raster.
                    if(frame==null || document.unsupported.Exists(u=>u.resource==entry.id && u.frame==picture.frame)) continue;
                    // The registration must be the catalog's own; a different one means another version of the drawing.
                    if(Mathf.Abs(frame.origin[0]-picture.sourceOrigin.x)>1e-3f || Mathf.Abs(frame.origin[1]-picture.sourceOrigin.y)>1e-3f)
                    {
                        if(mismatched++<5) Debug.LogWarning($"Векторная графика: {entry.id} кадр {picture.frame} — регистрация RVG {frame.origin[0]},{frame.origin[1]} не совпадает с каталогом {picture.sourceOrigin}; кадр остаётся PNG ({picture.spriteResource}).");
                        continue;
                    }
                    frames.Add(picture.spriteResource,(document,resource,frame));
                }
            }
            if(mismatched>0) Debug.LogWarning($"Векторная графика: кадров с другой регистрацией — {mismatched}, они остаются PNG.");
        }

        public bool TryGet(string key,out Sprite sprite)
        {
            if(sprites.TryGetValue(key,out sprite)) return true;
            if(!frames.TryGetValue(key,out var source)) return false;
            var watch=Stopwatch.StartNew();
            var texture=Draw(key,Zoom,false);
            var frame=source.Frame;
            // Registration as the PNG importer sets it (ImportedArtPadding.Pivot): origin from the top-left, Y up in Unity.
            var pivot=new Vector2(frame.origin[0]*Zoom/texture.width,1-frame.origin[1]*Zoom/texture.height);
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),pivot,SourcePixelsPerUnit*Zoom,0,SpriteMeshType.FullRect);
            sprite.name=key;
            sprites.Add(key,sprite);
            RasterSeconds+=watch.Elapsed.TotalSeconds;
            return true;
        }
        public RvgResourceFrame Frame(string key)=>frames.TryGetValue(key,out var source)?source.Frame:throw new KeyNotFoundException("Нет векторного кадра: "+key);
        // The key's frame at any zoom, not kept: readable for checks, GPU-only for sprites.
        public Texture2D Draw(string key,float zoom,bool readable)
        {
            if(!frames.TryGetValue(key,out var source)) throw new KeyNotFoundException("Нет векторного кадра: "+key);
            rasterizer??=new RvgRasterizer(LoadBitmap);
            Texture2D texture;
            try {texture=readable?rasterizer.Render(source.Document,source.Resource,source.Frame,zoom):rasterizer.RenderGpu(source.Document,source.Resource,source.Frame,zoom);}
            finally
            {
                foreach(var bitmap in loadedBitmaps) Release(bitmap);
                loadedBitmaps.Clear();
            }
            texture.name=key;texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            return texture;
        }
        // The rasterizer keeps its own premultiplied copy; the decoded PNG is released after the frame.
        private Texture2D LoadBitmap(RvgBitmap bitmap)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            if(!texture.LoadImage(File.ReadAllBytes(Path.Combine(Package.Root,bitmap.file)),false))
            {
                Release(texture);
                throw new InvalidDataException("Не прочитан растр векторной графики: "+bitmap.file);
            }
            loadedBitmaps.Add(texture);
            return texture;
        }

        private static void Release(UnityEngine.Object value)
        {
            if(Application.isPlaying) UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose()
        {
            foreach(var sprite in sprites.Values) if(sprite!=null) {Release(sprite.texture);Release(sprite);}
            sprites.Clear();
            rasterizer?.Dispose();rasterizer=null;
        }
    }
}
