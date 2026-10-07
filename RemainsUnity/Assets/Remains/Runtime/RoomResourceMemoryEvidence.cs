using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.Profiling;

namespace Remains.Runtime
{
    // Explicit opt-in milestone only. Enumerating loaded objects is diagnostic work,
    // not a catalog lookup, and none of those object references survive Capture.
    public static class RoomResourceMemoryEvidence
    {
        [Serializable] public sealed class Sample
        {
            public int schema=1,frame,texture2DCount,texture2DMeasuredCount,texture2DUnavailableEstimates,
                roomAuthoringCount,roomPrefabAuthoringCount,roomSceneAuthoringCount,roomCatalogAssetCount;
            public bool optInDiagnostic=true;
            public string visit,room;
            public double elapsedSeconds,captureMilliseconds,objectEnumerationMilliseconds;
            public long? unityAllocatedBytes,unityReservedBytes,monoUsedBytes,monoReservedBytes,graphicsDriverBytes,
                texture2DEstimatedNativeBytes,processPrivateBytes,processWorkingSetBytes;
            public string[] warnings;
            public string limitation="Сумма Texture2D — оценка нативной памяти перечисленных объектов, не полный объём GPU. Счётчики Unity и ОС имеют разные границы и не складываются. Стоимость диагностической выборки записана отдельно; она не вызывает сборку мусора и не загружает каталог.";
        }

        public static Sample Capture(string visit,string room)
        {
            if(string.IsNullOrWhiteSpace(visit) || string.IsNullOrWhiteSpace(room))
                throw new ArgumentException("Замер памяти комнаты: нужны имена посещения и комнаты.");
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var result=new Sample {visit=visit,room=room,frame=Time.frameCount,elapsedSeconds=Time.realtimeSinceStartupAsDouble};
            var warnings=new List<string>();
            void Warn(string text)
            {
                string message=$"Замер памяти комнаты: visit={visit}, room={room}; {text}";
                warnings.Add(message);Debug.LogWarning(message);
            }
            long? Bytes(string name,Func<long> read,bool allowZero=false)
            {
                try
                {
                    long bytes=read();
                    if(bytes<0 || bytes==0 && !allowZero)
                    {Warn($"счётчик {name} недоступен или не подтвердил значение (returned={bytes}); записано null.");return null;}
                    return bytes;
                }
                catch(Exception error)
                {Warn($"счётчик {name} недоступен: {error.GetType().Name}: {error.Message}; записано null.");return null;}
            }

            // Sample these before allocating the enumeration arrays; enumeration cost
            // is reported separately and is never part of the transition timing journal.
            result.unityAllocatedBytes=Bytes("Unity allocated",Profiler.GetTotalAllocatedMemoryLong);
            result.unityReservedBytes=Bytes("Unity reserved",Profiler.GetTotalReservedMemoryLong);
            result.monoUsedBytes=Bytes("Mono used",Profiler.GetMonoUsedSizeLong);
            result.monoReservedBytes=Bytes("Mono reserved",Profiler.GetMonoHeapSizeLong);
            if(Application.isEditor || Debug.isDebugBuild)
                result.graphicsDriverBytes=Bytes("graphics driver",Profiler.GetAllocatedMemoryForGraphicsDriver);
            else Warn("graphics driver доступен только в Editor/development player; записано null.");
            try
            {
                using var process=System.Diagnostics.Process.GetCurrentProcess();process.Refresh();
                result.processPrivateBytes=Bytes("OS private",()=>process.PrivateMemorySize64);
                result.processWorkingSetBytes=Bytes("OS working set",()=>process.WorkingSet64);
            }
            catch(Exception error)
            {Warn($"счётчики процесса ОС недоступны: {error.GetType().Name}: {error.Message}; записано null.");}

            var enumeration=System.Diagnostics.Stopwatch.StartNew();
            var textures=Resources.FindObjectsOfTypeAll<Texture2D>();
            result.texture2DCount=textures.Length;
            long textureBytes=0;
            foreach(var texture in textures)
            {
                if(texture==null) throw new InvalidOperationException($"Замер памяти комнаты {room}: перечисление Texture2D содержит уничтоженный объект.");
                long bytes=Profiler.GetRuntimeMemorySizeLong(texture);
                if(bytes<=0) {result.texture2DUnavailableEstimates++;continue;}
                textureBytes=checked(textureBytes+bytes);result.texture2DMeasuredCount++;
            }
            if(result.texture2DUnavailableEstimates>0)
                Warn($"нативная оценка Texture2D недоступна для {result.texture2DUnavailableEstimates}/{result.texture2DCount} объектов; сумма охватывает только {result.texture2DMeasuredCount} измеренных объектов.");
            result.texture2DEstimatedNativeBytes=result.texture2DMeasuredCount==0 && result.texture2DCount>0?null:textureBytes;
            var rooms=Resources.FindObjectsOfTypeAll<RoomAuthoring>();
            result.roomAuthoringCount=rooms.Length;
            foreach(var authoring in rooms)
            {
                if(authoring==null) throw new InvalidOperationException($"Замер памяти комнаты {room}: перечисление RoomAuthoring содержит уничтоженный объект.");
                if(authoring.gameObject.scene.IsValid()) result.roomSceneAuthoringCount++;else result.roomPrefabAuthoringCount++;
            }
            // Do not call Load, Entries or Resolve: observing the loaded asset instances
            // must not load/pin any additional room or traverse serialized catalog entries.
            result.roomCatalogAssetCount=Resources.FindObjectsOfTypeAll<RoomCatalogAsset>().Length;
            result.objectEnumerationMilliseconds=enumeration.Elapsed.TotalMilliseconds;
            result.captureMilliseconds=timer.Elapsed.TotalMilliseconds;result.warnings=warnings.ToArray();
            Validate(result);return result;
        }

        public static void Validate(Sample sample)
        {
            if(sample==null || sample.schema!=1 || !sample.optInDiagnostic ||
                string.IsNullOrWhiteSpace(sample.visit) || string.IsNullOrWhiteSpace(sample.room) ||
                sample.frame<0 || !double.IsFinite(sample.elapsedSeconds) || sample.elapsedSeconds<0 ||
                !double.IsFinite(sample.captureMilliseconds) || sample.captureMilliseconds<0 ||
                !double.IsFinite(sample.objectEnumerationMilliseconds) || sample.objectEnumerationMilliseconds<0 ||
                sample.objectEnumerationMilliseconds>sample.captureMilliseconds ||
                sample.texture2DCount<0 || sample.texture2DMeasuredCount<0 || sample.texture2DUnavailableEstimates<0 ||
                (long)sample.texture2DMeasuredCount+sample.texture2DUnavailableEstimates!=sample.texture2DCount ||
                sample.roomAuthoringCount<0 || sample.roomPrefabAuthoringCount<0 || sample.roomSceneAuthoringCount<0 || sample.roomCatalogAssetCount<0 ||
                (long)sample.roomPrefabAuthoringCount+sample.roomSceneAuthoringCount!=sample.roomAuthoringCount || sample.warnings==null)
                throw new InvalidOperationException($"Некорректная диагностическая выборка памяти: visit={sample?.visit}, room={sample?.room}.");
            foreach(var bytes in new[] {sample.unityAllocatedBytes,sample.unityReservedBytes,sample.monoUsedBytes,sample.monoReservedBytes,
                sample.graphicsDriverBytes,sample.texture2DEstimatedNativeBytes,sample.processPrivateBytes,sample.processWorkingSetBytes})
            {
                if(!bytes.HasValue && sample.warnings.Length==0)
                    throw new InvalidOperationException($"Нет объяснения недоступного счётчика памяти: visit={sample.visit}, room={sample.room}.");
                if(bytes.HasValue && bytes.Value<0)
                    throw new InvalidOperationException($"Отрицательная оценка памяти: visit={sample.visit}, room={sample.room}, bytes={bytes}.");
            }
        }
    }
}
