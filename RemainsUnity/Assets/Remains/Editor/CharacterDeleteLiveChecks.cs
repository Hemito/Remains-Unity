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
    // Live deletion of characters (CharacterDeleteProbe) in the saved scene of a hidden batch Editor:
    //   -batchmode -executeMethod Remains.Editor.CharacterDeleteLiveChecks.RunBatch --role menu --character pony-one
    //   --smoke characters-delete --save-dir <new> --report <new.json>
    [InitializeOnLoad]
    public static class CharacterDeleteLiveChecks
    {
        private const string Key="Remains.CharacterDeleteLive";
        static CharacterDeleteLiveChecks()
        {if(SessionState.GetBool(Key,false)) EditorApplication.update+=Watch;}
        public static void RunBatch()
        {
            try
            {
                var options=LaunchOptions.Parse();
                if(!Application.isBatchMode || options.Role!="menu" || options.Smoke!="characters-delete" ||
                    string.IsNullOrEmpty(options.SaveDirectory) || Directory.Exists(options.SaveDirectory) || string.IsNullOrEmpty(options.Report))
                    throw new InvalidOperationException("Живое удаление персонажа требует batchmode, --role menu, новый --save-dir и --report.");
                Anatomy.Load();WeaponCatalog.Load();
                SessionState.SetString(Key+".report",options.Report);
                SessionState.SetFloat(Key+".deadline",(float)EditorApplication.timeSinceStartup+180);
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
            {Debug.LogError("Таймаут живого удаления персонажа.");Finish(1);}
        }
        private static void Finish(int code)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Watch;EditorApplication.Exit(code);}
        [Serializable] private sealed class Result {public bool success;}
    }
}
