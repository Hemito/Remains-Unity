using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Runtime;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationBuild
    {
        public static void InstallAndBuild()
        {
            FactoryContinuationDraft.Import();FactoryContinuationObjectsAuthoring.Install();FactoryContinuationNpcAuthoring.Install();
            FactoryContinuationConnectionAuthoring.Connect();
            VerifyAndBuild();
        }
        public static void VerifyAndBuild()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            WeaponCatalog.Load();Anatomy.Load();PropCatalog.Load();ActorTypes.Load();
            var room=RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId);
            FactoryContinuationConnectionChecks.Check(room);
            var layout=room.CreateLayout();var batch=RoomContentBatch.Read(room,"factory:0");
            var sources=room.GetComponentsInChildren<RoomImportAudit>(true).Select(a=>a.Report.SourceId).ToArray();
            // Player building can unload asset objects. Keep detached verification data, not a Unity reference across the build.
            PrototypeBuild.BuildTo("Builds/FactoryContinuation/Remains.exe");
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/factory-continuation-r60/native.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,StateJson.ToJson(new NativeEvidence {schema=1,sourceId="original:plant:27",sourceHash=FactoryContinuationSource.SourceHash,
                layout=layout,batch=batch,moduleSources=sources,
                fullContractsPassed=true,originalRuntimeVerified=false},true));
            Debug.Log("Комната27: сохранены полные native данные пяти модулей; Windows сборка и проверки контрактов завершены.");
        }
        [Serializable] private sealed class NativeEvidence
        {
            public int schema;public string sourceId,sourceHash;public string[] moduleSources;
            public RoomLayout layout;public RoomContentBatch batch;
            public bool fullContractsPassed,originalRuntimeVerified;
        }
    }
}
