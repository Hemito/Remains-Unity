using System;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit asset migration: preserve saved sprite identities and every prefab reference.
    public static class InventoryIconReimport
    {
        public static void Run()
        {
            var catalog=GameAssets.Load<InventoryIconAsset>("Original/Inventory/icons").CreateLookup();
            foreach(string id in new[]{"heal","caps","p10","batt"})
            {
                string path=$"Assets/Remains/Resources/Original/Inventory/{id}.png";
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;
                importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=2048;
                importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(Math.Max(texture.width,texture.height)<150)
                    throw new InvalidOperationException($"Иконка {id}: требуется новый экспорт высокого разрешения.");
                var saved=catalog[id];
                var fresh=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),400,0,SpriteMeshType.FullRect);
                fresh.name=saved.name;EditorUtility.CopySerialized(fresh,saved);EditorUtility.SetDirty(saved);
                UnityEngine.Object.DestroyImmediate(fresh);
            }
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}

