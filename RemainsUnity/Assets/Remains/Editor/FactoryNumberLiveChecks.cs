using System;
using System.IO;
using Remains.Domain;
using Remains.Content;
using Remains.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // The live run of numbered factories (FactoryNumberProbe) in the saved scene of a hidden batch Editor.
    //   -batchmode -executeMethod Remains.Editor.FactoryNumberLiveChecks.RunBatch --role host --character factory-number
    //   --port 17894 --probe-peers 1 --probe-fps 60 --smoke factory-number --save-dir <new> --report <new.json>
    // Again with --smoke factory-number-reload, the same --save-dir and a new --report: a world that holds them restarts.
    [InitializeOnLoad]
    public static class FactoryNumberLiveChecks
    {
        private const string Key="Remains.FactoryNumberLive";
        static FactoryNumberLiveChecks()
        {if(SessionState.GetBool(Key,false)) EditorApplication.update+=Watch;}

        public static void RunBatch()
        {
            try
            {
                var options=LaunchOptions.Parse();
                bool reload=options.Smoke=="factory-number-reload";
                if(!Application.isBatchMode || options.Role!="host" || !reload && options.Smoke!="factory-number" || options.ProbePeers!=1 ||
                    string.IsNullOrEmpty(options.SaveDirectory) || Directory.Exists(options.SaveDirectory)!=reload || string.IsNullOrEmpty(options.Report))
                    throw new InvalidOperationException("Живой прогон номерных заводов требует batchmode, host, одного игрока; новый --save-dir, при перезапуске — прежний; --report.");
                Anatomy.Load();WeaponCatalog.Load();
                SessionState.SetString(Key+".report",options.Report);
                SessionState.SetFloat(Key+".deadline",(float)EditorApplication.timeSinceStartup+420);
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
            {Debug.LogError("Таймаут живого прогона номерных заводов.");Finish(1);}
        }
        private static void Finish(int code)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Watch;EditorApplication.Exit(code);}
        [Serializable] private sealed class Result {public bool success;}
    }
}
