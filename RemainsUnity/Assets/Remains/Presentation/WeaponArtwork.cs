using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    // Rectangles exclude transparent export margins. Source PNG pixels stay intact.
    public static class WeaponArtwork
    {
        [Serializable] private sealed class Entry {public string id;public int x,y,width,height;}
        [Serializable] private sealed class Library {public float pixelsPerUnit;public Entry[] weapons;}
        public static Dictionary<string,Sprite> Load()
        {
            var data=Resources.Load<TextAsset>("Original/Weapons/artwork");
            if(data==null) throw new InvalidOperationException("Нет описания графики оружия Original/Weapons/artwork.");
            var library=JsonUtility.FromJson<Library>(data.text);
            if(library?.weapons==null || !float.IsFinite(library.pixelsPerUnit) || library.pixelsPerUnit<=0)
                throw new InvalidOperationException("Некорректное описание масштаба графики оружия.");
            var result=new Dictionary<string,Sprite>(StringComparer.Ordinal);
            foreach(var entry in library.weapons)
            {
                var texture=Resources.Load<Texture2D>("Original/Weapons/"+entry.id);
                if(texture==null || entry.x<0 || entry.y<0 || entry.width<=0 || entry.height<=0 ||
                    entry.x+entry.width>texture.width || entry.y+entry.height>texture.height)
                    throw new InvalidOperationException($"Некорректная область спрайта оружия: {entry.id}.");
                result.Add(entry.id,Sprite.Create(texture,new Rect(entry.x,entry.y,entry.width,entry.height),
                    new Vector2(.5f,.5f),library.pixelsPerUnit));
                SpriteArtworkMaterial.Prepare(result[entry.id]);
            }
            return result;
        }
    }
}
