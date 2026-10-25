using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    // Rectangles exclude transparent export margins. Source PNG pixels stay intact.
    public static class WeaponArtwork
    {
        public static bool TryGet(string id,out Sprite sprite)
        {
            sprite=null;
            if(!WeaponRigs.TryPrefab(id,out var prefab)) return false;
            sprite=prefab.ResolveRestSprite();SpriteArtworkMaterial.Prepare(sprite);return true;
        }
        public static Dictionary<string,Sprite> Load()
        {
            WeaponRigs.Load();
            var result=new Dictionary<string,Sprite>(StringComparer.Ordinal);
            foreach(var entry in WeaponRigs.All)
            {
                var prefab=WeaponRigs.Prefab(entry.id);
                // Runtime resolves catalog artwork only when used; editor authoring still enumerates every source.
                if(Application.isPlaying && !string.IsNullOrEmpty(prefab.RestReference?.Key)) continue;
                result.Add(entry.id,prefab.ResolveRestSprite());
                SpriteArtworkMaterial.Prepare(result[entry.id]);
            }
            return result;
        }
    }
}
