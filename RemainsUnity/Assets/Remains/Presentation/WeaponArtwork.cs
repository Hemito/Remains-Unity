using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    // Rectangles exclude transparent export margins. Source PNG pixels stay intact.
    public static class WeaponArtwork
    {
        public static Dictionary<string,Sprite> Load()
        {
            WeaponRigs.Load();
            var result=new Dictionary<string,Sprite>(StringComparer.Ordinal);
            foreach(var entry in WeaponRigs.All)
            {
                result.Add(entry.id,WeaponRigs.Prefab(entry.id).Artwork.sprite);
                SpriteArtworkMaterial.Prepare(result[entry.id]);
            }
            return result;
        }
    }
}
