using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Runtime
{
    [Serializable] public sealed class RoomLightingGpuBounds {public float x,y,width,height;}
    [Serializable] public sealed class RoomLightingGpuMap
    {public string name,sha256,expectedSha256;public int width,height,bytes,maxError;}
    [Serializable] public sealed class RoomLightingGpuResult
    {
        public string room,profile,graphics;
        public long revision;
        public RoomLightingGpuBounds bounds;
        public RoomLightingGpuMap foreground,background;
        public double referenceMilliseconds,readbackMilliseconds;
        public bool sourceGuardVerified,shaderBindingsVerified;
        [NonSerialized] public RoomLighting source;
        [NonSerialized] public RoomLayout sourceRoom;
        [NonSerialized] public RoomLightProfile sourceProfile;
        [NonSerialized] public Texture2D sourceForeground,sourceBackground;
        [NonSerialized] public Rect sourceBounds;
        [NonSerialized] public byte[] foregroundPixels,backgroundPixels;
    }

    // Expensive, explicitly requested diagnostic; never part of the normal lighting update.
    public static class RoomLightingGpuEvidence
    {
        private sealed class Readback {public bool done;public string error;public byte[] pixels;}
        public static IEnumerator Compare(RoomLighting source,Func<bool> isSourceCurrent,Action<RoomLightingGpuResult> completed,float deadline)
        {
            if(source==null || isSourceCurrent==null || completed==null)
                throw new ArgumentException("GPU проверке нужны источник света, guard и получатель результата.");
            if(!SystemInfo.supportsAsyncGPUReadback)
                throw new InvalidOperationException("GPU не поддерживает readback карт света; пиксельное свидетельство отсутствует.");
            var result=new RoomLightingGpuResult {source=source,sourceRoom=source.Room,sourceProfile=source.Profile,
                sourceForeground=source.ForegroundMap,sourceBackground=source.BackgroundMap,sourceBounds=source.Bounds,
                revision=source.AppliedOcclusionRevision,room=source.Room?.id,graphics=SystemInfo.graphicsDeviceType.ToString(),
                profile=RecoverySnapshot.Fingerprint(StateJson.ToJson(source.Profile)),
                bounds=new RoomLightingGpuBounds {x=source.Bounds.x,y=source.Bounds.y,width=source.Bounds.width,height=source.Bounds.height}};
            EnsureCurrent(result,isSourceCurrent);
            Texture2D expectedForeground=null,expectedBackground=null;
            try
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                expectedForeground=RoomLightMap.Build(result.sourceRoom,result.sourceBounds,result.sourceProfile);
                expectedBackground=RoomLightMap.Build(result.sourceRoom,result.sourceBounds,result.sourceProfile.ForBackground());
                result.referenceMilliseconds=timer.Elapsed.TotalMilliseconds;
                EnsureCurrent(result,isSourceCurrent);
                timer.Restart();
                var foreground=Begin(result.sourceForeground);var background=Begin(result.sourceBackground);
                var referenceForeground=Begin(expectedForeground);var referenceBackground=Begin(expectedBackground);
                while(!foreground.done || !background.done || !referenceForeground.done || !referenceBackground.done)
                {
                    if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут GPU readback света: "+result.room);
                    yield return null;
                }
                if(foreground.error!=null || background.error!=null || referenceForeground.error!=null || referenceBackground.error!=null)
                    throw new InvalidOperationException($"GPU readback света завершился ошибкой: room={result.room}, FG={foreground.error}, BG={background.error}, referenceFG={referenceForeground.error}, referenceBG={referenceBackground.error}.");
                EnsureCurrent(result,isSourceCurrent);
                result.foreground=CompareMap(result.sourceForeground,expectedForeground,foreground.pixels,referenceForeground.pixels);
                result.background=CompareMap(result.sourceBackground,expectedBackground,background.pixels,referenceBackground.pixels);
                result.foregroundPixels=foreground.pixels;result.backgroundPixels=background.pixels;
                result.readbackMilliseconds=timer.Elapsed.TotalMilliseconds;
                result.sourceGuardVerified=result.shaderBindingsVerified=true;
                completed(result);
            }
            finally
            {
                if(expectedForeground!=null) UnityEngine.Object.Destroy(expectedForeground);
                if(expectedBackground!=null) UnityEngine.Object.Destroy(expectedBackground);
            }
        }
        public static void EnsureCurrent(RoomLightingGpuResult result,Func<bool> isSourceCurrent)
        {
            var source=result?.source;
            if(source==null || isSourceCurrent==null || !isSourceCurrent() || !ReferenceEquals(source.Profile,result.sourceProfile) ||
                RecoverySnapshot.Fingerprint(StateJson.ToJson(source.Profile))!=result.profile)
                throw new InvalidOperationException($"Проверяемый источник света сменился/устарел: room={result?.room}, revision={result?.revision}, applied={source?.AppliedOcclusionRevision}.");
            EnsurePublished(source,result.sourceRoom,result.revision,result.sourceForeground,result.sourceBackground,result.sourceBounds);
        }
        public static void EnsurePublished(RoomLighting source,RoomLayout room,long revision,Texture foreground,Texture background,Rect bounds)
        {
            if(source==null || room==null || !source.IsReady || source.IsUpdating || !ReferenceEquals(source.Room,room) ||
                foreground==null || background==null || source.ForegroundMap!=foreground || source.BackgroundMap!=background ||
                source.AppliedOcclusionRevision!=revision || source.Bounds!=bounds || !float.IsFinite(bounds.x) || !float.IsFinite(bounds.y) ||
                !float.IsFinite(bounds.width) || !float.IsFinite(bounds.height) || bounds.width<=0 || bounds.height<=0)
                throw new InvalidOperationException($"Опубликованная пара света сменилась/устарела: room={room?.id}, revision={revision}, applied={source?.AppliedOcclusionRevision}.");
            if(Shader.GetGlobalTexture("_WorldLightMap")!=foreground || Shader.GetGlobalTexture("_BackgroundLightMap")!=background ||
                Shader.GetGlobalFloat("_WorldLightEnabled")!=1 || Shader.GetGlobalVector("_WorldLightBounds")!=new Vector4(bounds.x,bounds.y,1/bounds.width,1/bounds.height))
                throw new InvalidOperationException("Actual shader binding не совпал с проверяемой парой/границами света: "+room.id);
        }
        private static Readback Begin(Texture texture)
        {
            var result=new Readback();
            AsyncGPUReadback.Request(texture,0,TextureFormat.RGBA32,request=>
            {
                try {if(request.hasError) result.error="GPU отказал в readback";else result.pixels=request.GetData<byte>().ToArray();}
                catch(Exception error) {result.error=error.Message;}
                finally {result.done=true;}
            });
            return result;
        }
        private static RoomLightingGpuMap CompareMap(Texture actual,Texture expected,byte[] pixels,byte[] reference)
        {
            if(actual.width!=expected.width || actual.height!=expected.height || pixels==null || reference==null ||
                pixels.Length!=actual.width*actual.height*4 || reference.Length!=pixels.Length)
                throw new InvalidOperationException("GPU размеры/readback света отличаются от fresh reference: "+actual.name);
            int maximum=MaximumError(pixels,reference);
            if(maximum>1) throw new InvalidOperationException($"GPU карта {actual.name} отличается от fresh build: maxByteError={maximum}.");
            return new RoomLightingGpuMap {name=actual.name,width=actual.width,height=actual.height,bytes=pixels.Length,maxError=maximum,
                sha256=ByteHash(pixels),expectedSha256=ByteHash(reference)};
        }
        public static int MaximumError(byte[] first,byte[] second)
        {
            if(first==null || second==null || first.Length!=second.Length) throw new InvalidOperationException("Не совпали размеры сравниваемых пикселей света.");
            int maximum=0;for(int index=0;index<first.Length;index++) maximum=Math.Max(maximum,Math.Abs(first[index]-second[index]));return maximum;
        }
        public static int ChangedPixels(byte[] first,byte[] second)
        {
            if(first==null || second==null || first.Length!=second.Length || first.Length%4!=0) throw new InvalidOperationException("Некорректный RGBA readback для сравнения света.");
            int count=0;for(int index=0;index<first.Length;index+=4)
                if(first[index]!=second[index] || first[index+1]!=second[index+1] || first[index+2]!=second[index+2]) count++;
            return count;
        }
        public static string ByteHash(byte[] bytes)
        {using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");}
        public static void WriteMap(string path,int width,int height,byte[] pixels)
        {
            if(width<=0 || height<=0 || pixels==null || pixels.Length!=width*height*4)
                throw new InvalidOperationException("Некорректные размеры/pixels карты света: "+path);
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            try {texture.LoadRawTextureData(pixels);texture.Apply(false,false);File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally {UnityEngine.Object.Destroy(texture);}
        }
    }
}
