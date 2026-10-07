using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponVectorReserveAuthoring
    {
        public static void Install()
        {
            var entries=VectorArtworkReserveManifest.Load(VectorArtworkReserveManifest.WeaponPath).entries;
            VectorArtworkReserveManifest.Require(entries.Length==2,"Ожидались копьё и гильза.");
            // Preflight every persisted object before changing either registration.
            foreach(var entry in entries)
            {
                VectorArtworkReserveManifest.VerifyStaged(entry);
                VectorArtworkReserveManifest.Require(VectorArtworkReserveManifest.Hash(entry.texturePath)==entry.pngSha256,
                    "Сначала нужны подготовленные8× пиксели: "+entry.texturePath);
                VectorArtworkReserveAuthoring.RequireCompatible(entry);
            }
            foreach(var entry in entries)
            {
                var importer=AssetImporter.GetAtPath(entry.texturePath) as TextureImporter;
                VectorArtworkReserveManifest.Require(importer!=null,"Нет импортёра: "+entry.texturePath);
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;
                importer.maxTextureSize=Math.Max(importer.maxTextureSize,entry.requiredMaxTextureSize);
                var declaration=entry.sprites[0];
                if(entry.id=="casing")
                {
                    VectorArtworkReserveManifest.Require(declaration.assetPath==entry.texturePath && entry.sprites.Length==1,
                        "Гильзе нужна сохранённая импортируемая Sprite-ссылка.");
                    importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                    var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                    settings.spriteAlignment=(int)SpriteAlignment.Custom;settings.spriteMeshType=SpriteMeshType.FullRect;
                    settings.spritePixelsPerUnit=declaration.pixelsPerUnit;
                    settings.spritePivot=new Vector2(declaration.pivotPixelsBottomLeft[0]/declaration.rect[2],declaration.pivotPixelsBottomLeft[1]/declaration.rect[3]);
                    settings.spriteBorder=new Vector4(declaration.border[0],declaration.border[1],declaration.border[2],declaration.border[3]);
                    importer.SetTextureSettings(settings);
                }
                else VectorArtworkReserveManifest.Require(entry.id=="spear" && entry.sprites.Length==1,"Неизвестная графика оружия: "+entry.id);
                importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(entry.texturePath);
                VectorArtworkReserveManifest.Require(texture!=null && texture.width==entry.pixelWidth && texture.height==entry.pixelHeight,
                    "Unity уменьшила экспорт: "+entry.texturePath);
                if(entry.id=="spear")
                {
                    var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(declaration.assetPath);
                    var rect=new Rect(declaration.rect[0],declaration.rect[1],declaration.rect[2],declaration.rect[3]);
                    var pivot=new Vector2(declaration.pivotPixelsBottomLeft[0]/rect.width,declaration.pivotPixelsBottomLeft[1]/rect.height);
                    if(sprite.rect!=rect || sprite.pixelsPerUnit!=declaration.pixelsPerUnit || Vector2.Distance(sprite.pivot,new Vector2(declaration.pivotPixelsBottomLeft[0],declaration.pivotPixelsBottomLeft[1]))>.0001f)
                    {
                        var fresh=Sprite.Create(texture,rect,pivot,declaration.pixelsPerUnit,0,SpriteMeshType.FullRect,
                            new Vector4(declaration.border[0],declaration.border[1],declaration.border[2],declaration.border[3]));
                        try {fresh.name=sprite.name;EditorUtility.CopySerialized(fresh,sprite);EditorUtility.SetDirty(sprite);}
                        finally {UnityEngine.Object.DestroyImmediate(fresh);}
                    }
                }
            }
            AssetDatabase.SaveAssets();
            foreach(var entry in entries) VectorArtworkReserveChecks.VerifyEntry(entry.id);
            Debug.Log("Копьё и гильза: векторный экспорт8× установлен; прежние GUID, Sprite ID и мировая регистрация сохранены.");
        }
    }
}
