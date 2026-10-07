using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit build/capture command. It never creates fixtures or installs authored content.
    public static class RaidEntryReadinessBuild
    {
        public const string OutputPath="Builds/RaidEntryReadiness/Remains.exe";
        [Serializable] private sealed class Evidence
        {
            public int schema=1;
            public string output;
            public bool fullContractsPassed,assetInstallationRequested,originalRuntimeVerified;
            public RoomLayout layout;
            public RoomContentBatch content;
            public string[] moduleSources;
        }
        public static void Build()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            WeaponCatalog.Load();Anatomy.Load();PropCatalog.Load();ActorTypes.Load();
            var room=RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId);
            var evidence=new Evidence {output=OutputPath,layout=room.CreateLayout(),
                content=RoomContentBatch.Read(room,"factory:0"),
                moduleSources=room.GetComponentsInChildren<RoomImportAudit>(true).Select(a=>a.Report.SourceId).ToArray()};
            // Detached data remains valid if the build unloads native authoring objects.
            PrototypeBuild.BuildTo(OutputPath);
            evidence.fullContractsPassed=true;
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/raid-entry-r61/build.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,StateJson.ToJson(evidence,true));
            Debug.Log("Подготовлена отдельная сборка проверки входа в рейд; контент не устанавливался, native данные сохранены: "+path);
        }
    }
}
