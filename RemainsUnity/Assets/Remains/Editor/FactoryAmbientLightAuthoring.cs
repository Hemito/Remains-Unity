using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Remains.Editor
{
    // The factory's global 2D light as a prefab: Light2D fills its target sorting layers ("all") only in the
    // Editor (Awake under UNITY_EDITOR), so a Light2D added by code in a player build has none and fails.
    //   batch -executeMethod Remains.Editor.FactoryAmbientLightAuthoring.RunBatch
    public static class FactoryAmbientLightAuthoring
    {
        public const string PrefabPath="Assets/Remains/Resources/Presentation/FactoryAmbientLight.prefab";

        [MenuItem("Remains/Комнаты/Обновить общий свет завода")]
        public static void Generate()
        {
            var obj=new GameObject("Общий свет завода");
            try
            {
                var light=obj.AddComponent<Light2D>();   // Editor Awake: all sorting layers
                light.lightType=Light2D.LightType.Global;
                PrefabUtility.SaveAsPrefabAsset(obj,PrefabPath,out bool saved);
                if(!saved) throw new IOException("Не записан общий свет завода: "+PrefabPath);
            }
            finally {Object.DestroyImmediate(obj);}
            AssetDatabase.SaveAssets();
        }

        public static void RunBatch()
        {
            int code=0;
            try {Generate();}
            catch(System.Exception error) {Debug.LogError("Общий свет завода: "+error);code=1;}
            EditorApplication.Exit(code);
        }
    }
}
