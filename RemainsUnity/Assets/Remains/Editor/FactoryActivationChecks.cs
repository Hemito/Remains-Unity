using System;
using System.IO;
using Remains.Runtime;
using Remains.Domain;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remains.Editor
{
    // Uses the saved ordinary scene and the existing input/UI/network travel probe.
    [InitializeOnLoad]
    public static class FactoryActivationChecks
    {
        private const string Key="Remains.FactoryActivation";
        private static bool inputConfigured;
        static FactoryActivationChecks()
        {
            if(SessionState.GetBool(Key,false)) EditorApplication.update+=Watch;
        }
        public static void RunBatch()
        {
            try
            {
                var options=LaunchOptions.Parse();
                if(options.Smoke!="travel-points-host" || options.ProbePeers!=1 ||
                    string.IsNullOrEmpty(options.SaveDirectory) || Directory.Exists(options.SaveDirectory) ||
                    string.IsNullOrEmpty(options.Report))
                    throw new InvalidOperationException("Проверка завода требует нового изолированного save-dir, report и одиночного travel-points-host.");
                Anatomy.Load();WeaponCatalog.Load();
                WorldRoomChecks.Run();
                SessionState.SetString(Key+".report",options.Report);
                SessionState.SetFloat(Key+".deadline",(float)EditorApplication.timeSinceStartup+360);
                SessionState.SetBool(Key,true);
                EditorApplication.update+=Watch;
                EditorSceneManager.OpenScene("Assets/Remains/Scenes/Remains.unity");
                EditorApplication.EnterPlaymode();
            }
            catch(Exception error) {Debug.LogError("Включение завода прервано: "+error);EditorApplication.Exit(1);}
        }
        private static void Watch()
        {
            if(EditorApplication.isPlaying && !inputConfigured)
            {
                // Detached settings: only this background verification process ignores
                // Game View focus, so its virtual mouse can exercise the actual UI.
                InputSystem.settings=UnityEngine.Object.Instantiate(InputSystem.settings);
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                inputConfigured=true;
            }
            string path=SessionState.GetString(Key+".report","");
            if(!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var report=StateJson.FromJson<Report>(File.ReadAllText(path));
                Finish(report.success?0:1,report.detail);return;
            }
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+".deadline",0))
                Finish(1,"Таймаут игрового прохода завод/хаб.");
        }
        private static void Finish(int code,string detail)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Watch;
            Debug.Log("Игровое включение завода: "+(code==0?"PASS; ":"FAIL; ")+detail);
            EditorApplication.Exit(code);
        }
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
