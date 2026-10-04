using System;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ArmoredTrainingTargetAuthoring
    {
        public const string AssetPath="Assets/Remains/RoomDrafts/training-armored-dummy.asset";
        public static void Create()
        {
            if(AssetDatabase.LoadMainAssetAtPath(AssetPath)!=null) throw new InvalidOperationException("Определение броневой мишени уже существует.");
            var root=new GameObject("Броневая учебная мишень");
            try
            {
                var view=root.AddComponent<SpriteRenderer>();
                view.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/RoomDrafts/ArmoredTrainingSprite.asset");
                view.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Remains/RoomDrafts/ArmoredTrainingMaterial.mat");view.sortingOrder=20;
                if(view.sprite==null || view.sharedMaterial==null) throw new InvalidOperationException("Нет сохранённой графики броневой мишени.");
                root.AddComponent<StaticActorVisual>().Configure(view,"Original/Training/armored-artwork","Original/Training/armored-dummy");
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/Remains/RoomDrafts/training-armored-dummy.prefab");
                var asset=ScriptableObject.CreateInstance<ActorTypeAsset>();
                asset.SetDefinition(new ActorTypeDefinition {id="training-armored-dummy",presentation="static",artwork="Original/Training/armored-artwork",restoreHealthAfterHit=500,hitProtection=20});
                asset.SetPrefab(prefab.GetComponent<StaticActorVisual>());AssetDatabase.CreateAsset(asset,AssetPath);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            ArmoredTrainingTargetChecks.Run();
        }
    }
}
