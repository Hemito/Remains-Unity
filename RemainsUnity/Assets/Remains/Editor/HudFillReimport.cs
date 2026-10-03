using System;
using System.Collections.Generic;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class HudFillReimport
    {
        public static void Run()
        {
            var view=Resources.Load<CombatHudView>("Presentation/CombatHud");view.Validate();
            var sprites=new HashSet<Sprite>(view.HealthWarningFrames) {view.Health.sprite,view.Mana.sprite};
            foreach(var sprite in sprites)
            {
                string path=AssetDatabase.GetAssetPath(sprite.texture);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;
                importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(texture.height!=48 || (texture.width!=400 && texture.width!=800))
                    throw new InvalidOperationException("Неверный размер экспорта HUD 4×: "+path);
                float scale=texture.width/sprite.rect.width;
                if(Mathf.Abs(texture.height/sprite.rect.height-scale)>.001f || sprite.rect.position!=Vector2.zero)
                    throw new InvalidOperationException("Экспорт HUD изменил пропорции или обрезку: "+path);
                var fresh=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),sprite.pivot/sprite.rect.size,
                    sprite.pixelsPerUnit*scale,0,SpriteMeshType.FullRect);
                fresh.name=sprite.name;EditorUtility.CopySerialized(fresh,sprite);EditorUtility.SetDirty(sprite);
                UnityEngine.Object.DestroyImmediate(fresh);
            }
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
