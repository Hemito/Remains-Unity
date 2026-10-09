using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Remains.Editor
{
    // Builds Rooms/Original/Tiles/TileArt.asset from tiles-manifest.json: one Tile sub-asset per
    // mask frame / front sprite (transform unlocked: quarters are mirrored per tile), plus
    // texture references. Re-running updates in place; missing art is an error, not a skip.
    public static class RoomTileArtAuthoring
    {
        public const string AssetPath=RoomTileArtPostprocessor.Root+"TileArt.asset";

        [MenuItem("Remains/Комнаты/Обновить набор графики клеток")]
        public static void Build()
        {
            var manifestAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(RoomTileArtPostprocessor.ManifestPath)
                ??throw new InvalidOperationException("Нет манифеста графики клеток: "+RoomTileArtPostprocessor.ManifestPath);
            var manifest=TileManifest.Read(manifestAsset.text,RoomTileArtPostprocessor.ManifestPath);
            var asset=AssetDatabase.LoadAssetAtPath<RoomTileArtAsset>(AssetPath);
            if(asset==null) {asset=ScriptableObject.CreateInstance<RoomTileArtAsset>();AssetDatabase.CreateAsset(asset,AssetPath);}
            var existing=AssetDatabase.LoadAllAssetsAtPath(AssetPath).OfType<Tile>().ToDictionary(t=>t.name,StringComparer.Ordinal);
            var tiles=new List<RoomTileArtAsset.TileEntry>();
            var textures=new List<RoomTileArtAsset.TextureEntry>();
            foreach(var art in manifest.Art.Values)
            {
                string path=RoomTileArtPostprocessor.Root+art.File;
                if(art.Id.StartsWith("texture:",StringComparison.Ordinal))
                {
                    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path)??throw new InvalidOperationException("Нет текстуры клеток: "+path);
                    textures.Add(new RoomTileArtAsset.TextureEntry{id=art.Id,texture=texture});
                    continue;
                }
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path)??throw new InvalidOperationException("Нет спрайта клеток (проверьте импорт как Sprite): "+path);
                string tileName=art.Id.Replace(':','_');
                if(!existing.TryGetValue(tileName,out var tile))
                {tile=ScriptableObject.CreateInstance<Tile>();tile.name=tileName;AssetDatabase.AddObjectToAsset(tile,asset);}
                existing.Remove(tileName);
                tile.sprite=sprite;tile.flags=TileFlags.None;tile.colliderType=Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                tiles.Add(new RoomTileArtAsset.TileEntry{id=art.Id,tile=tile,sprite=sprite});
            }
            foreach(var stale in existing.Values) UnityEngine.Object.DestroyImmediate(stale,true);
            asset.Configure(manifestAsset,tiles.ToArray(),textures.ToArray());
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"Набор графики клеток: тайлов {tiles.Count}, текстур {textures.Count} → {AssetPath}.");
        }
    }
}
