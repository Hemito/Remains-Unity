using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Narrow authoring and acceptance entry point; never rebuild the original content catalog.
    public static class LightToolsChecks
    {
        public static void PrepareBatch()
        {
            try
            {
                LightTestItemsAuthoring.Run();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                LightTestItemsChecks.Run();
                FlashlightLightChecks.Run();
                Debug.Log("Световые предметы: подготовка и целевые проверки PASS.");
                EditorApplication.Exit(0);
            }
            catch(Exception failure) {Debug.LogException(failure);EditorApplication.Exit(1);}
        }
        public static void BuildBatch()
        {
            try
            {
                LightTestItemsAuthoring.Run();
                AssetDatabase.SaveAssets();
                LightTestItemsChecks.Run();
                PrototypeBuild.BuildTo("Builds/LightTools/Remains.exe");
                EditorApplication.Exit(0);
            }
            catch(Exception failure) {Debug.LogException(failure);EditorApplication.Exit(1);}
        }
    }
}
