using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class UnityLocalizationChecks
    {
        public static void BuildLoadingFixBatch()
        {
            try
            {
                RoomLoadingViewChecks.Run();
                PrototypeBuild.BuildTo("Builds/Windows/Remains.exe",runContractChecks:false);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void BuildBatch()
        {
            try {PrototypeBuild.BuildTo("Builds/UnityLocalization/Remains.exe");EditorApplication.Exit(0);}
            catch(Exception failure){Debug.LogException(failure);EditorApplication.Exit(1);}
        }
        public static void RebuildBatch()
        {
            try
            {
                UnityLocalizationAuthoring.Migrate();UnityLocalizationAuthoring.RunChecks();
                LocalizationModChecks.Run();LocalizationModChecks.RunUnity();LocalizationChecks.Run();
                PrototypeBuild.BuildTo("Builds/UnityLocalization/Remains.exe",runContractChecks:false);EditorApplication.Exit(0);
            }
            catch(Exception failure){Debug.LogException(failure);EditorApplication.Exit(1);}
        }
        public static async void RunBatch()
        {
            try
            {
                UnityLocalizationAuthoring.Migrate();
                UnityLocalizationAuthoring.RunChecks();
                LocalizationModChecks.Run();
                LocalizationModChecks.RunUnity();
                LocalizationChecks.Run();
                await LocalizationChecks.RenderSnapshots();
                Debug.Log("Unity Localization: PASS — таблицы, стабильные ID, моды, язык и интерфейс.");
                EditorApplication.Exit(0);
            }
            catch(Exception failure){Debug.LogException(failure);EditorApplication.Exit(1);}
        }
    }
}
