using System;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropPrefabMigration
    {
        public static void Run()
        {
            const string folder="Assets/Remains/Prefabs/Props";
            if(!AssetDatabase.IsValidFolder("Assets/Remains/Prefabs")) AssetDatabase.CreateFolder("Assets/Remains","Prefabs");
            if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Remains/Prefabs","Props");
            foreach(string guid in AssetDatabase.FindAssets("t:PropAsset",new[]{"Assets/Remains/Resources/Content"}))
            {
                var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if(asset.Prefab!=null) continue;
                var definition=asset.CreateDefinition();var texture=Resources.Load<Texture2D>(definition.resource);
                if(texture==null) throw new InvalidOperationException("Нет текстуры объекта "+definition.id);
                string path=folder+"/"+definition.id;
                if(AssetDatabase.LoadMainAssetAtPath(path+".prefab")!=null) throw new InvalidOperationException("Префаб уже существует: "+path);
                var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),50);sprite.name=definition.id;
                AssetDatabase.CreateAsset(sprite,path+"-sprite.asset");
                var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,path+".mat");
                var root=new GameObject(definition.name);
                try
                {
                    var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                    var view=root.AddComponent<PropView>();view.SetArtwork(renderer);view.FitWidth(definition.width);
                    var prefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");asset.SetPrefab(prefab.GetComponent<PropView>());EditorUtility.SetDirty(asset);
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
            AssetDatabase.SaveAssets();
            var paths=new System.Collections.Generic.List<string>();
            foreach(string guid in AssetDatabase.FindAssets("t:PropAsset",new[]{"Assets/Remains/Resources/Content"})) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            AssetDatabase.ForceReserializeAssets(paths);
            ContractChecks.Run();
            Debug.Log("Префабы физических объектов созданы; существующие префабы не перезаписаны.");
        }
    }
}
