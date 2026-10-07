using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using Remains.Content;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Actual normal-runtime teardown observer, with no world or actor injection.
    // Root must create this on an independent GameObject before generic smoke routing.
    [DefaultExecutionOrder(10000)]
    public sealed class RuntimeCatalogReleaseProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public int schema=1;
            public bool success,normalHostReady,destroyRequested,runtimeAbsent,sessionAbsent,
                networkInactive,loadRejectedAfterRelease,unloadCompleted,oldCatalogAbsent,
                shaderDisabled,characterFileExists,worldFileExists;
            public string smoke="runtime-catalog-release",role,stage,detail,worldId,actorId,
                saveDirectory,characterDirectory,worldDirectory,loadRejection;
            public int catalogInstanceId,runtimeInstanceId,sessionInstanceId,readyFrame,destroyFrame,
                inactiveFrame,unloadCompletedFrame,nativeCatalogsBeforeUnload,nativeCatalogsAfterUnload;
            public double elapsedSeconds;
            public string[] errors;
            public string limitation="Проверяется обычная host-сессия и native освобождение каталога после уничтожения runtime. Объём освобождённого графа/памяти, повторный запуск и другие транспорты этой проверкой не доказаны.";
        }

        private PrototypeRuntime runtime;
        private readonly Report report=new();
        private readonly List<string> errors=new();
        private string reportPath;
        private double began,deadline;
        private bool finished;

        public void Initialize(PrototypeRuntime value)
        {
            began=Time.realtimeSinceStartupAsDouble;deadline=began+90;
            runtime=value;
            Application.logMessageReceived+=OnLog;
            try
            {
                Require(value!=null && value.Options!=null,"Не передан обычный runtime.");
                reportPath=value.Options.Report;
                Require(value.Options.Smoke==report.smoke && value.Options.Role=="host" &&
                    !string.IsNullOrWhiteSpace(value.Options.SaveDirectory) && !string.IsNullOrWhiteSpace(reportPath),
                    "Нужны точный --smoke runtime-catalog-release, --role host, отдельный --save-dir и --report.");
                Require(transform.parent==null && gameObject!=value.gameObject,
                    "Observer должен находиться на отдельном корневом GameObject, вне уничтожаемого runtime.");
                report.role=value.Options.Role;
                report.saveDirectory=Remains.Persistence.SavePath.Absolute(value.Options.SaveDirectory);
                report.stage="normal-host-ready";
                StartCoroutine(Drive());
            }
            catch(Exception error) {Finish(false,error.ToString());}
        }

        // Forward iterator failures to the report; Unity otherwise only logs them.
        private IEnumerator Drive()
        {
            var execution=Run();
            try
            {
                while(!finished)
                {
                    bool moved=false;Exception failure=null;
                    try {Check();moved=execution.MoveNext();}
                    catch(Exception error) {failure=error;}
                    if(failure!=null) {Finish(false,failure.ToString());yield break;}
                    if(!moved) {Finish(true,"Обычный runtime завершил сеть и запись; lease закрыт, прежний native каталог выгружен.");yield break;}
                    yield return execution.Current;
                }
            }
            finally {(execution as IDisposable)?.Dispose();}
        }

        private IEnumerator Run()
        {
            while(!NormalHostReady()) yield return null;
            CaptureReadyAndDestroy();
            // Destroy and nested helper Destroy calls are deferred to frame boundaries.
            yield return null;
            yield return null;
            report.stage="network-and-runtime-stop";
            while(NetworkServer.active || NetworkClient.active) yield return null;
            report.networkInactive=true;report.inactiveFrame=Time.frameCount;
            Require(!HasNativeInstance<PrototypeRuntime>(report.runtimeInstanceId),"Уничтоженный runtime всё ещё присутствует среди native объектов.");
            report.runtimeAbsent=true;
            Require(!HasNativeInstance<Remains.Networking.CoopSession>(report.sessionInstanceId),"Собственная сетевая сессия осталась после уничтожения runtime.");
            report.sessionAbsent=true;
            report.loadRejectedAfterRelease=LoadRejectsMissingLease(out report.loadRejection);
            Require(report.loadRejectedAfterRelease,"RoomCatalogAsset.Load принял каталог без runtime-владельца.");
            report.shaderDisabled=Shader.GetGlobalFloat("_WorldLightEnabled")==0;
            Require(report.shaderDisabled,"После освобождения комнаты shader продолжает использовать её карту света.");
            report.characterFileExists=File.Exists(Path.Combine(report.characterDirectory,"character.json"));
            report.worldFileExists=File.Exists(Path.Combine(report.worldDirectory,"world.json"));
            Require(report.characterFileExists && report.worldFileExists,"Штатный stop не оставил файлы мира/персонажа в отдельном каталоге проверки.");

            report.stage="native-unload";
            report.nativeCatalogsBeforeUnload=CountNativeCatalogs();
            var unloading=Resources.UnloadUnusedAssets();
            // Polling instead of yielding the AsyncOperation keeps the deadline active.
            while(!unloading.isDone) yield return null;
            unloading=null;
            report.unloadCompleted=true;report.unloadCompletedFrame=Time.frameCount;
            yield return null;
            report.nativeCatalogsAfterUnload=CountNativeCatalogs();
            report.oldCatalogAbsent=!HasNativeInstance<RoomCatalogAsset>(report.catalogInstanceId);
            Require(report.oldCatalogAbsent,"Прежний native каталог всё ещё удерживается после UnloadUnusedAssets.");
            // A late callback must not recreate ownership or revive the session.
            for(int frame=0;frame<3;frame++) yield return null;
            Require(!NetworkServer.active && !NetworkClient.active,"После завершения teardown снова активна сеть.");
            Require(LoadRejectsMissingLease(out _),"Поздний callback заново подготовил каталог без владельца.");
            Require(!HasNativeInstance<RoomCatalogAsset>(report.catalogInstanceId),"Поздний callback удержал прежний native каталог.");
            report.stage="complete";
        }

        private bool NormalHostReady()
        {
            Require(runtime!=null,"Runtime исчез до проверки обычной готовой host-сессии.");
            if(runtime.ActiveRoomLighting?.Failure!=null) throw runtime.ActiveRoomLighting.Failure;
            return runtime.Session!=null && NetworkServer.active && NetworkClient.isConnected &&
                NetworkManager.singleton==runtime.Session && runtime.Session.ServerWorld!=null &&
                runtime.Session.World!=null && runtime.Session.LocalActor?.connected==true &&
                runtime.ActiveRoomLighting!=null && runtime.ActiveRoomLighting.IsReady &&
                !runtime.ActiveRoomLighting.IsUpdating && !runtime.IsLevelPreparing;
        }

        private void CaptureReadyAndDestroy()
        {
            report.normalHostReady=true;report.readyFrame=Time.frameCount;
            report.catalogInstanceId=ReadCatalogInstanceId();
            report.runtimeInstanceId=runtime.GetInstanceID();
            report.sessionInstanceId=runtime.Session.GetInstanceID();
            report.worldId=runtime.Session.World.worldId;report.actorId=runtime.Session.LocalActorId;
            report.characterDirectory=runtime.Session.Saves.CharacterDirectoryPath;
            report.worldDirectory=runtime.Session.Saves.WorldDirectoryPath;
            Require(IsWithinSaveDirectory(report.characterDirectory) && IsWithinSaveDirectory(report.worldDirectory),
                "Runtime использует библиотеку вне явно указанного каталога проверки.");
            report.stage="destroy-runtime";report.destroyRequested=true;report.destroyFrame=Time.frameCount;
            Destroy(runtime.gameObject);
            runtime=null; // No runtime, Session or SO reference may survive in this observer.
        }

        private bool IsWithinSaveDirectory(string directory)
        {
            string expected=report.saveDirectory.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            string actual=Remains.Persistence.SavePath.Absolute(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            var comparison=Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            return string.Equals(actual,expected,comparison) || actual.StartsWith(expected+Path.DirectorySeparatorChar,comparison);
        }
        // These helpers return scalar values only. Native wrappers/arrays stay on
        // their short-lived method stacks, never in the coroutine or Report.
        private static int ReadCatalogInstanceId()=>RoomCatalogAsset.Load().GetInstanceID();
        private static bool HasNativeInstance<T>(int id) where T:UnityEngine.Object
        {
            foreach(var value in Resources.FindObjectsOfTypeAll<T>())
                if(value!=null && value.GetInstanceID()==id) return true;
            return false;
        }
        private static int CountNativeCatalogs()=>Resources.FindObjectsOfTypeAll<RoomCatalogAsset>().Length;
        private static bool LoadRejectsMissingLease(out string reason)
        {
            try {RoomCatalogAsset.Load();reason=null;return false;}
            catch(InvalidOperationException error)
            {reason=error.Message;return reason.Contains("Content/RoomCatalog");}
        }
        private void Check()
        {
            Require(Time.realtimeSinceStartupAsDouble<=deadline,"Истёк deadline проверки: stage="+report.stage);
            Require(errors.Count==0,"Во время обычной загрузки/teardown возникли ошибки: "+string.Join("\n",errors));
        }
        private void OnLog(string condition,string stack,LogType type)
        {
            if(type is LogType.Error or LogType.Exception or LogType.Assert)
                errors.Add(condition+"\n"+stack);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            finished=true;Application.logMessageReceived-=OnLog;
            report.success=success;report.detail=detail;report.elapsedSeconds=Time.realtimeSinceStartupAsDouble-began;
            report.errors=errors.ToArray();runtime=null;
            try
            {
                if(string.IsNullOrWhiteSpace(reportPath)) throw new InvalidOperationException("Не указан --report.");
                string path=Path.GetFullPath(reportPath);Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,StateJson.ToJson(report,true));
            }
            catch(Exception error) {success=false;Debug.LogError("Не удалось записать отчёт освобождения runtime-каталога: "+error);}
            if(success) Debug.Log("RuntimeCatalogReleaseProbe PASS: "+detail);
            else Debug.LogError("RuntimeCatalogReleaseProbe FAIL: "+detail);
            Application.Quit(success?0:1);
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Runtime catalog release: "+message);}
        private void OnDestroy() {Application.logMessageReceived-=OnLog;runtime=null;}
    }
}
