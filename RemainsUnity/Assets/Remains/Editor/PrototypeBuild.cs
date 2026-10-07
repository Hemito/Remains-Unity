using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class PrototypeBuild
    {
        public static void Build()
            =>BuildTo("Builds/Windows/Remains.exe");
        public static void BuildTo(string outputPath)
        {
            foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Remains/Resources/Original/Pony","Assets/Remains/Resources/Original/Room","Assets/Remains/Resources/Original/Shots","Assets/Remains/Resources/Original/Menu","Assets/Remains/Resources/Original/Hud","Assets/Remains/Resources/Original/Inventory","Assets/Remains/Resources/Original/Training","Assets/Remains/Resources/Original/Props","Assets/Remains/Resources/Original/Raider"}))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                // Room27 has an explicit native/mixed export contract; its installer and checks own every import setting.
                if(importer.assetPath.Contains("/Original/Room/Factory27R60/") ||
                    importer.assetPath.Contains("/Original/Room/ContinuationObjects/")) continue;
                if(importer.assetPath.Contains("/Original/Room/Factory53/") || importer.assetPath.Contains("/Original/Room/Factory40/"))
                {
                    // Preserve the reviewed module's transparent RGB pixels; its canvas registration is explicit.
                    if(importer.alphaIsTransparency) {importer.alphaIsTransparency=false;importer.SaveAndReimport();}
                    continue;
                }
                // These resources have explicit, validated native import policies. Preserve their Sprite IDs and RGBA pixels.
                if(importer.assetPath.Contains("/Original/Room/WorkshopToolStand/") ||
                    importer.assetPath.Contains("/Original/Pony/Wearables/") ||
                    importer.assetPath.Contains("/Original/Room/BackdropComposition/") ||
                    importer.assetPath.Contains("/Original/Room/FactoryObjects/") ||
                    importer.assetPath.Contains("/Original/Room/OfficeOpenings/") ||
                    importer.assetPath.Contains("/Original/Containers/OfficeLoot/") ||
                    importer.assetPath.Contains("/Original/Containers/OfficeFurniture/") ||
                    importer.assetPath.Contains("/Original/Inventory/office-valuables/") ||
                    importer.assetPath.Contains("/Original/Inventory/tool-components/")) continue;
                importer.textureType=TextureImporterType.Default;
                importer.alphaIsTransparency=true;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled=false;
                if(importer.assetPath.EndsWith("/Original/Inventory/shell.png"))
                {
                    importer.maxTextureSize=4096;
                    importer.npotScale=TextureImporterNPOTScale.None;
                }
                if(importer.assetPath.Contains("/Hub/light-mask-")) importer.isReadable=true;
                if(importer.assetPath.Contains("/Original/Raider/") || importer.assetPath.Contains("/Original/Shots/")) importer.maxTextureSize=4096;
                importer.SaveAndReimport();
            }
            ContractChecks.Run();
            var inventoryShell=Resources.Load<Texture2D>("Original/Inventory/shell");
            if(inventoryShell==null || inventoryShell.width!=2097 || inventoryShell.height!=1080)
                throw new InvalidOperationException("Корпус инвентаря должен сохранять исходный размер 2097×1080 после импорта.");
            var enabledScenes=EditorBuildSettings.scenes.Where(scene=>scene.enabled).ToArray();
            if(enabledScenes.Length!=1) throw new InvalidOperationException("Build Settings должны содержать одну стартовую сцену игры.");
            string path=enabledScenes[0].path;
            RoomAuthoringScenes.ValidateSavedOverrides();
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(path)==null)
                throw new InvalidOperationException($"Нет стартовой сцены {path}. Сборка не создаёт и не заменяет сцены.");
            var startupScene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var starters=startupScene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Remains.Runtime.PrototypeRuntime>(true)).ToArray();
            if(starters.Length!=1 || !starters[0].enabled || !starters[0].gameObject.activeInHierarchy)
                throw new InvalidOperationException("Стартовая сцена должна содержать ровно один активный компонент PrototypeRuntime.");
            var cameras=startupScene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Camera>(true))
                .Where(camera=>camera.enabled && camera.gameObject.activeInHierarchy && camera.CompareTag("MainCamera")).ToArray();
            if(cameras.Length!=1) throw new InvalidOperationException("Стартовая сцена должна содержать ровно одну активную MainCamera.");
            Remains.Presentation.CameraFollower.RequireCamera(cameras[0]);
            AssetDatabase.SaveAssets();
            string projectRoot=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            string absoluteOutput=Path.GetFullPath(Path.Combine(projectRoot,outputPath));
            if(!absoluteOutput.StartsWith(projectRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(absoluteOutput)!="Remains.exe")
                throw new InvalidOperationException("Путь сборки должен находиться внутри проекта и завершаться Remains.exe: "+outputPath);
            var options=new BuildPlayerOptions { scenes=new[]{path}, locationPathName=absoluteOutput,
                target=BuildTarget.StandaloneWindows64, options=BuildOptions.Development };
            var report=BuildPipeline.BuildPlayer(options);
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException($"Сборка не завершена: {report.summary.result}, ошибок: {report.summary.totalErrors}.");
            Debug.Log($"Сборка проверена: {report.summary.outputPath}, размер={report.summary.totalSize}.");
        }
    }
}
