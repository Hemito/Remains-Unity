using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit authoring command; the saved sprite remains the runtime source.
    public static class TrainingArtworkReimport
    {
        public static void Run()
        {
            const string texturePath="Assets/Remains/Resources/Original/Training/dummy.png";
            const string spritePath="Assets/Remains/Prefabs/Actors/training-dummy-sprite.asset";
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            if(importer==null) throw new InvalidOperationException("Нет текстуры мишени: "+texturePath);
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;
            importer.maxTextureSize=4096;importer.spritePixelsPerUnit=200;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(texture.width!=251 || texture.height!=354)
                throw new InvalidOperationException("Ожидается прямой экспорт visualTrain 4×, 251×354.");
            var saved=AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if(saved==null) throw new InvalidOperationException("Нет сохранённого спрайта мишени: "+spritePath);
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,0),200,0,SpriteMeshType.FullRect);
            sprite.name=saved.name;EditorUtility.CopySerialized(sprite,saved);
            EditorUtility.SetDirty(saved);UnityEngine.Object.DestroyImmediate(sprite);
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
