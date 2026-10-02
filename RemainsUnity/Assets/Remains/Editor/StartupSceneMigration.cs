using System;
using System.Linq;
using Remains.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class StartupSceneMigration
    {
        public static void Run()
        {
            const string path="Assets/Remains/Scenes/Prototype.unity";
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(path)==null) throw new InvalidOperationException("Отсутствует стартовая сцена: "+path);
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var roots=scene.GetRootGameObjects();
            int count=roots.Sum(root=>root.GetComponentsInChildren<PrototypeRuntime>(true).Length);
            if(count>1) throw new InvalidOperationException("В стартовой сцене несколько компонентов запуска; требуется исправить сцену.");
            if(count==0) new GameObject("Запуск игры").AddComponent<PrototypeRuntime>();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("Явный запуск сохранён в Prototype.unity; существующие объекты сцены сохранены.");
        }
    }
}
