using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Experimental.Rendering;

namespace Remains.Editor
{
    // TD-32 measurement only: unique imported/public Assets textures, not a scene or Player working set.
    public static class TextureMemoryInventoryChecks
    {
        [Serializable] private sealed class Entry
        {
            public string path,assetId,type,graphicsFormat,format,importCompression,standaloneFormat;
            public int width,height,depth,layers,mips,sourceWidth,sourceHeight,maxImportSize;
            public bool readable,importMips,standaloneOverride,compressedFormat;
            public long rgba32BaseBytes,rgba32MipBytes,runtimeMemoryBytes;
        }
        [Serializable] private sealed class Group
        {public string name;public int textures;public long rgba32MipBytes,runtimeMemoryBytes;}
        [Serializable] private sealed class Report
        {
            public string utc,unityVersion,platform,graphicsDevice,scope;
            public int assetPaths,textures,skippedPaths,actualUncompressedTextures;
            public long rgba32BaseBytes,rgba32MipBytes,runtimeMemoryBytes,actualUncompressedRuntimeBytes;
            public List<Entry> entries=new();public List<Group> formats=new();public List<string> skipped=new();
        }
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var report=new Report {utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                platform=Application.platform.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName,
                scope="All unique persisted Texture assets under public Assets, loaded by this Editor inventory. RGBA32 bytes are an equivalent at actual imported dimensions/mips, not the compressed format size. Profiler memory is the sum reported for those loaded texture objects; this is not process RSS, VRAM, scene or Player working set. Packages, builtin and runtime-created textures are excluded."};
            var paths=AssetDatabase.FindAssets("t:Texture",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.Ordinal).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            report.assetPaths=paths.Length;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(string path in paths)
            {
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                var main=AssetDatabase.LoadAssetAtPath<Texture>(path);
                // Image importers expose one main Texture plus Sprite subassets. Native .asset files may contain
                // several Texture subassets, so enumerate those instead of counting only the main object.
                var textures=importer!=null && main!=null?new[]{main}:AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture>().ToArray();
                if(textures.Length==0){report.skipped.Add(path);continue;}
                foreach(var texture in textures)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture,out string guid,out long localId);
                    string id=string.IsNullOrEmpty(guid)?path+"#instance:"+texture.GetInstanceID():guid+":"+localId;
                    if(!seen.Add(id))continue;
                    int depth=texture is Texture3D volume?volume.depth:1;
                    int layers=texture is Cubemap?6:texture is Texture2DArray array?array.depth:
                        texture is CubemapArray cubes?cubes.cubemapCount*6:1;
                    int mips=texture.mipmapCount;
                    var entry=new Entry {path=path,assetId=id,type=texture.GetType().Name,width=texture.width,height=texture.height,
                        depth=depth,layers=layers,mips=mips,graphicsFormat=texture.graphicsFormat.ToString(),
                        format=texture is Texture2D image?image.format.ToString():texture.graphicsFormat.ToString(),
                        readable=texture.isReadable,compressedFormat=GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat),
                        runtimeMemoryBytes=Profiler.GetRuntimeMemorySizeLong(texture),
                        rgba32BaseBytes=checked((long)texture.width*texture.height*depth*layers*4),
                        rgba32MipBytes=Rgba32MipBytes(texture.width,texture.height,depth,layers,mips),importCompression="No TextureImporter"};
                    if(importer!=null)
                    {
                        importer.GetSourceTextureWidthAndHeight(out entry.sourceWidth,out entry.sourceHeight);
                        entry.importCompression=importer.textureCompression.ToString();entry.importMips=importer.mipmapEnabled;
                        entry.maxImportSize=importer.maxTextureSize;
                        var standalone=importer.GetPlatformTextureSettings("Standalone");
                        entry.standaloneOverride=standalone.overridden;entry.standaloneFormat=standalone.format.ToString();
                    }
                    report.entries.Add(entry);
                    if(report.entries.Count%1000==0)Debug.Log("TEXTURE_INVENTORY_PROGRESS: textures="+report.entries.Count);
                }
            }
            report.textures=report.entries.Count;report.skippedPaths=report.skipped.Count;
            report.rgba32BaseBytes=report.entries.Sum(e=>e.rgba32BaseBytes);report.rgba32MipBytes=report.entries.Sum(e=>e.rgba32MipBytes);
            report.runtimeMemoryBytes=report.entries.Sum(e=>e.runtimeMemoryBytes);
            report.actualUncompressedTextures=report.entries.Count(e=>!e.compressedFormat);
            report.actualUncompressedRuntimeBytes=report.entries.Where(e=>!e.compressedFormat).Sum(e=>e.runtimeMemoryBytes);
            foreach(var group in report.entries.GroupBy(e=>e.importCompression+" / "+e.format).OrderByDescending(g=>g.Sum(e=>e.rgba32MipBytes)))
                report.formats.Add(new Group {name=group.Key,textures=group.Count(),rgba32MipBytes=group.Sum(e=>e.rgba32MipBytes),runtimeMemoryBytes=group.Sum(e=>e.runtimeMemoryBytes)});
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/FactoryVerification/td32-texture-memory.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(report.textures==0 || report.skippedPaths!=0)throw new InvalidOperationException("Inventory incomplete: textures="+report.textures+", skipped="+report.skippedPaths+"; report="+output);
            Debug.Log($"TEXTURE_INVENTORY_PASS: paths={report.assetPaths}, textures={report.textures}, RGBA32-base={report.rgba32BaseBytes}, RGBA32-mips={report.rgba32MipBytes}, Profiler-object-sum={report.runtimeMemoryBytes}; report={output}");
        }
        private static long Rgba32MipBytes(int width,int height,int depth,int layers,int mips)
        {
            long total=0;
            for(int mip=0;mip<mips;mip++)
            {total=checked(total+(long)width*height*depth*layers*4);width=Math.Max(1,width/2);height=Math.Max(1,height/2);depth=Math.Max(1,depth/2);}
            return total;
        }
    }
}
