using System;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropReferenceMigration
    {
        public static void Run()
        {
            foreach(string guid in AssetDatabase.FindAssets("t:PropAsset",new[]{"Assets/Remains/Resources/Content"}))
            {
                var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if(asset.Texture!=null) continue;
                var definition=asset.CreateDefinition();var texture=Resources.Load<Texture2D>(definition.resource);
                if(texture==null) throw new InvalidOperationException($"Нет исходной текстуры {definition.id}: {definition.resource}.");
                asset.SetTexture(texture);EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();ContractChecks.Run();
            Debug.Log("Явные ссылки текстур физических объектов сохранены и проверены.");
        }
    }
}
