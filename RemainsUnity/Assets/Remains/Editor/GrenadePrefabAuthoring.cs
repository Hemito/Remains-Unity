using System;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class GrenadePrefabAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Prefabs/Props/grenade-flight";
            var definition=GameAssets.Load<WeaponAsset>("Content/weapons/grenade");
            if(definition==null) throw new InvalidOperationException("Нет определения гранаты.");
            if(definition.GrenadeFlight!=null) throw new InvalidOperationException("Префаб гранаты уже назначен; повторная миграция отменена.");
            foreach(string extension in new[]{".prefab","-sprite.asset",".mat"})
                if(AssetDatabase.LoadMainAssetAtPath(path+extension)!=null) throw new InvalidOperationException("Ресурс уже существует: "+path+extension);
            var texture=GameAssets.Load<Texture2D>("Original/Props/grenade-flight");
            if(texture==null) throw new InvalidOperationException("Нет исходного изображения гранаты.");
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),200);
            sprite.name="Граната в полёте";AssetDatabase.CreateAsset(sprite,path+"-sprite.asset");
            var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,path+".mat");
            var root=new GameObject("Граната в полёте");
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                renderer.sortingOrder=ActorVisualLayers.WorldObjectOrder;
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
                definition.SetGrenadeFlight(prefab.GetComponent<SpriteRenderer>());EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            ContractChecks.Run();
        }
    }
}
