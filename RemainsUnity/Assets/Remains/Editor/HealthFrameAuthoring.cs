using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class HealthFrameAuthoring
    {
        public static void Run()
        {
            const string texturePath="Assets/Remains/Resources/Original/Hud/health-frame.png";
            const string spritePath="Assets/Remains/Resources/Presentation/health-frame.asset";
            const string prefabPath="Assets/Remains/Resources/Presentation/CombatHud.prefab";
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            if(importer==null) throw new InvalidOperationException("Нет экспорта рамки здоровья.");
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var fresh=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(4,4,4,4));
            fresh.name="Рамка здоровья оригинала";
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if(sprite==null) {AssetDatabase.CreateAsset(fresh,spritePath);sprite=fresh;}
            else {EditorUtility.CopySerialized(fresh,sprite);EditorUtility.SetDirty(sprite);UnityEngine.Object.DestroyImmediate(fresh);}
            var root=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var view=root.GetComponent<CombatHudView>();
                var existing=view.Health.transform.Find("Рамка здоровья");
                var frame=existing!=null?existing.GetComponent<Image>():new GameObject("Рамка здоровья",typeof(RectTransform),typeof(Image)).GetComponent<Image>();
                frame.transform.SetParent(view.Health.transform,false);frame.sprite=sprite;frame.type=Image.Type.Sliced;
                frame.pixelsPerUnitMultiplier=4;frame.raycastTarget=false;
                frame.rectTransform.anchorMin=Vector2.zero;frame.rectTransform.anchorMax=Vector2.one;
                frame.rectTransform.offsetMin=-Vector2.one;frame.rectTransform.offsetMax=Vector2.one;
                PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
