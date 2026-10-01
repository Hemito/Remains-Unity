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
                var texture=Resources.Load<Texture2D>("Original/Weapons/"+entry.id);
                if(texture==null || entry.x<0 || entry.y<0 || entry.width<=0 || entry.height<=0 ||
                    entry.x+entry.width>texture.width || entry.y+entry.height>texture.height)
                    throw new InvalidOperationException($"Некорректная область спрайта оружия: {entry.id}.");
                result.Add(entry.id,Sprite.Create(texture,new Rect(entry.x,entry.y,entry.width,entry.height),
                    new Vector2(entry.pivotX,entry.pivotY),WeaponRigs.PixelsPerUnit));
                SpriteArtworkMaterial.Prepare(result[entry.id]);
            }
            return result;
        }
    }
}
