using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class PrototypeBuild
    {
        public static void Build()
        {
            ContractChecks.Run();
            foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Remains/Resources/Original/Pony","Assets/Remains/Resources/Original/Room/Hub","Assets/Remains/Resources/Original/Shots","Assets/Remains/Resources/Original/Menu"}))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                importer.textureType=TextureImporterType.Default;
                importer.alphaIsTransparency=true;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled=false;
                importer.SaveAndReimport();
            }
            string path="Assets/Remains/Scenes/Prototype.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера").AddComponent<Camera>(); camera.tag="MainCamera";
            EditorSceneManager.SaveScene(scene,path);
            AssetDatabase.SaveAssets();
            var options=new BuildPlayerOptions { scenes=new[]{path}, locationPathName="Builds/Windows/RemainsPrototype.exe",
                target=BuildTarget.StandaloneWindows64, options=BuildOptions.Development };
            var report=BuildPipeline.BuildPlayer(options);
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException($"Сборка не завершена: {report.summary.result}, ошибок: {report.summary.totalErrors}.");
            Debug.Log($"Сборка проверена: {report.summary.outputPath}, размер={report.summary.totalSize}.");
        }
    }
}
