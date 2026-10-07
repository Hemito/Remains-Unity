using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Remains.Content;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Standalone startup diagnostic, before PrototypeRuntime creates any service.
    // Root wiring must disable PrototypeRuntime after this branch initializes.
    public sealed class RoomCatalogLifetimeProbe : MonoBehaviour
    {
        private const string ResourceKey="Content/RoomCatalog";
        private const double TimeoutSeconds=120;
        [Serializable] private sealed class CatalogObservation
        {
            public int instanceId,entryCount;
            public string[] entryIds;
            public int[] roomInstanceIds;
        }
        [Serializable] private sealed class PhaseObservation
        {
            public string name;
            public int frame,expectedOwnedTokens;
            public double elapsedSeconds;
            public bool firstDoubleDisposed,oldTokensDisposedAgain,oldInstanceAbsent;
            public int[] loadedCatalogInstanceIds;
            public CatalogObservation catalog;
            public RoomResourceMemoryEvidence.Sample memory;
        }
        [Serializable] private sealed class Report
        {
            public bool success,standalone,noAssetMutation,noForcedResourceDestruction,noOwnerLoadRejected,finalInstanceAbsent;
            public int schema=1,firstInstanceId,reacquiredInstanceId;
            public string detail,noOwnerRejection;
            public PhaseObservation[] phases;
            public string limitation="Отдельный процесс проверяет только настоящий Resources-каталог и срок жизни его владельцев. Графические/GPU readback не выполняются. Оценки Texture2D и ОС не являются общим объёмом GPU; неизвестные счётчики памяти отмечены явно.";
        }
        private readonly List<PhaseObservation> phases=new();
        private LaunchOptions options;
        private IDisposable first,second,reacquired;
        private CatalogObservation initial,newCatalog;
        private double deadline;
        private bool initialized,finished,noOwnerRejected,finalAbsent;
        private string phase="before-acquire",noOwnerReason;

        public void Initialize(LaunchOptions value)
        {
            if(initialized) throw new InvalidOperationException("Проверка срока жизни каталога уже запущена.");
            if(value==null || value.Smoke!="catalog-lifetime" || string.IsNullOrWhiteSpace(value.Report))
                throw new InvalidOperationException("Проверка каталога требует отдельный --smoke catalog-lifetime и путь --report.");
            if(File.Exists(value.Report))
                throw new InvalidOperationException("Проверка каталога не перезаписывает прежний итоговый отчёт: "+value.Report);
            options=value;initialized=true;deadline=Time.realtimeSinceStartupAsDouble+TimeoutSeconds;
            Application.runInBackground=true;Application.targetFrameRate=value.ProbeFrameRate;
            StartCoroutine(Checked(Run()));
        }
        private IEnumerator Run()
        {
            Observe("before-acquire",0,null);
            first=RoomCatalogAsset.AcquireRuntime("catalog-lifetime:first");
            initial=InspectCatalog();Require(initial.entryCount>0,"Каталог не содержит определений комнат.");
            second=RoomCatalogAsset.AcquireRuntime("catalog-lifetime:second");
            var shared=InspectCatalog();RequireSame(initial,shared,true,"two-owners");
            Observe("two-owners",2,shared);

            first.Dispose();first.Dispose();
            phase="first-released-unload";yield return Unload();
            shared=InspectCatalog();RequireSame(initial,shared,true,"remaining-owner");
            Require(LoadedCatalogIds().Contains(initial.instanceId),"Первый Dispose выгрузил каталог при живом втором владельце.");
            Observe("remaining-owner-after-unload",1,shared,firstDoubleDisposed:true);

            second.Dispose();phase="all-released-unload";yield return Unload();
            Require(!LoadedCatalogIds().Contains(initial.instanceId),"После освобождения всех владельцев исходный native-каталог всё ещё загружен.");
            noOwnerReason=NoOwnerRejection();noOwnerRejected=true;
            Observe("all-released-after-unload",0,null,firstDoubleDisposed:true,oldAbsent:true);

            reacquired=RoomCatalogAsset.AcquireRuntime("catalog-lifetime:reacquired");
            newCatalog=InspectCatalog();RequireSame(initial,newCatalog,false,"reacquired");
            Observe("reacquired",1,newCatalog,firstDoubleDisposed:true);
            // Previously disposed tokens must not release a subsequent owner/epoch.
            // Do not require a different instance ID: absence was proven before Acquire.
            first.Dispose();second.Dispose();phase="old-tokens-after-reacquire-unload";yield return Unload();
            var surviving=InspectCatalog();RequireSame(newCatalog,surviving,true,"old-tokens-after-reacquire");
            Require(LoadedCatalogIds().Contains(newCatalog.instanceId),"Старый токен освободил новый каталог.");
            Observe("old-tokens-after-reacquire",1,surviving,firstDoubleDisposed:true,oldTokensDisposedAgain:true);

            reacquired.Dispose();phase="final-release-unload";yield return Unload();
            Require(!LoadedCatalogIds().Contains(newCatalog.instanceId),"После последнего Dispose новый native-каталог остался загружен.");
            NoOwnerRejection();finalAbsent=true;
            Observe("final-release-after-unload",0,null,firstDoubleDisposed:true,oldTokensDisposedAgain:true,oldAbsent:true);
            Finish(true,"Два владельца разделяли настоящий каталог; последний Dispose позволил штатную выгрузку. Новое приобретение сохранило определения, старые токены не затронули нового владельца.");
        }
        private IEnumerator Unload()
        {
            // Never manually Destroy/UnloadAsset the actual catalog or its rooms.
            CheckDeadline();yield return null;CheckDeadline();yield return null;CheckDeadline();
            var operation=Resources.UnloadUnusedAssets();
            while(!operation.isDone) {CheckDeadline();yield return null;}
            CheckDeadline();
        }
        private IEnumerator Checked(IEnumerator iterator)
        {
            try
            {
                while(!finished)
                {
                    object next;
                    try {CheckDeadline();if(!iterator.MoveNext()) yield break;next=iterator.Current;}
                    catch(Exception error) {Finish(false,error.ToString());yield break;}
                    // Explicitly pump nested waits too: an AsyncOperation must not hide
                    // the real-time deadline or bypass this diagnostic error boundary.
                    if(next is IEnumerator nested)
                    {
                        var checkedNested=Checked(nested);
                        try {while(checkedNested.MoveNext()) yield return checkedNested.Current;}
                        finally {(checkedNested as IDisposable)?.Dispose();}
                    }
                    else yield return next;
                }
            }
            finally {(iterator as IDisposable)?.Dispose();}
        }
        private void CheckDeadline()
        {
            if(Time.realtimeSinceStartupAsDouble>deadline)
                throw new InvalidOperationException($"Истёк срок проверки каталога: phase={phase}, timeout={TimeoutSeconds} с.");
        }
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException(reason);}
        private static void RequireSame(CatalogObservation expected,CatalogObservation actual,bool sameInstance,string context)
        {
            Require(actual!=null && actual.entryCount==expected.entryCount && actual.entryIds.SequenceEqual(expected.entryIds,StringComparer.Ordinal),
                "Изменились стабильные определения каталога: "+context);
            if(sameInstance)
            {
                Require(actual.instanceId==expected.instanceId,"Владельцы получили разные экземпляры каталога: "+context);
                Require(actual.roomInstanceIds.SequenceEqual(expected.roomInstanceIds),"Каталог заменил исходные room asset bindings: "+context);
            }
        }
        // These helpers are synchronous and intentionally do not inline: coroutine state
        // keeps only detached numbers/strings, never a catalog/entry/room/asset array.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static CatalogObservation InspectCatalog()
        {
            var catalog=RoomCatalogAsset.Load();catalog.Validate();
            var entries=catalog.Entries.OrderBy(entry=>entry.id,StringComparer.Ordinal).ToArray();
            return new CatalogObservation {instanceId=catalog.GetInstanceID(),entryCount=entries.Length,
                entryIds=entries.Select(entry=>entry.id).ToArray(),roomInstanceIds=entries.Select(entry=>entry.room.GetInstanceID()).ToArray()};
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int[] LoadedCatalogIds()=>Resources.FindObjectsOfTypeAll<RoomCatalogAsset>().Select(catalog=>catalog.GetInstanceID()).OrderBy(id=>id).ToArray();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string NoOwnerRejection()
        {
            try {RoomCatalogAsset.Load();}
            catch(InvalidOperationException error) {return error.Message;}
            throw new InvalidOperationException("Load в Play разрешён без активного runtime-владельца.");
        }
        private void Observe(string name,int ownedTokens,CatalogObservation catalog,bool firstDoubleDisposed=false,bool oldTokensDisposedAgain=false,bool oldAbsent=false)
        {
            CheckDeadline();phase=name;
            phases.Add(new PhaseObservation {name=name,frame=Time.frameCount,elapsedSeconds=Time.realtimeSinceStartupAsDouble,
                expectedOwnedTokens=ownedTokens,catalog=catalog,firstDoubleDisposed=firstDoubleDisposed,oldTokensDisposedAgain=oldTokensDisposedAgain,
                oldInstanceAbsent=oldAbsent,loadedCatalogInstanceIds=LoadedCatalogIds(),memory=RoomResourceMemoryEvidence.Capture(name,ResourceKey)});
        }
        private bool DisposeOwners()
        {
            bool success=true;
            foreach(var token in new[] {first,second,reacquired})
                try {token?.Dispose();}
                catch(Exception error) {success=false;Debug.LogError("Освобождение владельца диагностического каталога: "+error);}
            return success;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            success=success && noOwnerRejected && finalAbsent && phases.Count==7;
            finished=true;
            if(!DisposeOwners()) {success=false;detail+="; завершающее освобождение токенов завершилось ошибкой";}
            try
            {
                File.WriteAllText(options.Report,StateJson.ToJson(new Report {success=success,standalone=true,noAssetMutation=true,noForcedResourceDestruction=true,
                    noOwnerLoadRejected=noOwnerRejected,finalInstanceAbsent=finalAbsent,detail=$"phase={phase}; {detail}",noOwnerRejection=noOwnerReason,
                    firstInstanceId=initial?.instanceId??0,reacquiredInstanceId=newCatalog?.instanceId??0,phases=phases.ToArray()},true));
            }
            catch(Exception error) {success=false;Debug.LogError("Не удалось записать отчёт срока жизни каталога: "+error);}
            if(!success) Debug.LogError("Проверка срока жизни каталога не пройдена: "+detail);
            StartCoroutine(Exit(success));
        }
        private void OnDestroy()=>DisposeOwners();
        private IEnumerator Exit(bool success)
        {yield return new WaitForSecondsRealtime(.5f);Application.Quit(success?0:1);}
    }
}
