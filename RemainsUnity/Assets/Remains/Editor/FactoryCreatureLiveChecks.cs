using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // The live run of the factory's robots and zombies (FactoryCreatureLiveProbe) in the saved scene of a hidden batch
    // Editor: enters Play Mode and exits with the report's result.
    //   -batchmode -executeMethod Remains.Editor.FactoryCreatureLiveChecks.RunBatch --role host --character creatures-live
    //   --port 17893 --probe-peers 1 --probe-fps 60 --smoke creatures-frames-live --save-dir <new> --report <new.json>
    //   [--live-stations 0,1,10]
    [InitializeOnLoad]
    public static class FactoryCreatureLiveChecks
    {
        private const string Key="Remains.FactoryCreatureLive";
        static FactoryCreatureLiveChecks()
        {if(SessionState.GetBool(Key,false)) EditorApplication.update+=Watch;}

        public static void RunBatch()
        {
            try
            {
                var options=LaunchOptions.Parse();
                if(!Application.isBatchMode || options.Role!="host" || options.Smoke!="creatures-frames-live" || options.ProbePeers!=1 ||
                    string.IsNullOrEmpty(options.SaveDirectory) || Directory.Exists(options.SaveDirectory) || string.IsNullOrEmpty(options.Report))
                    throw new InvalidOperationException("Живой прогон существ требует batchmode, host, creatures-frames-live, одного игрока, нового save-dir и report.");
                Anatomy.Load();WeaponCatalog.Load();
                SessionState.SetString(Key+".report",options.Report);
                SessionState.SetFloat(Key+".deadline",(float)EditorApplication.timeSinceStartup+1800);
                SessionState.SetBool(Key,true);
                EditorApplication.update+=Watch;
                EditorSceneManager.OpenScene("Assets/Remains/Scenes/Remains.unity");
                EditorApplication.EnterPlaymode();
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void Watch()
        {
            string path=SessionState.GetString(Key+".report","");
            if(!string.IsNullOrEmpty(path) && File.Exists(path))
            {Finish(JsonUtility.FromJson<Result>(File.ReadAllText(path)).success?0:1);return;}
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+".deadline",0))
            {Debug.LogError("Таймаут живого прогона существ.");Finish(1);}
        }
        private static void Finish(int code)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Watch;EditorApplication.Exit(code);}
        [Serializable] private sealed class Result {public bool success;}
    }
}
