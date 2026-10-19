using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;

namespace Remains.Presentation
{
    public sealed class InventoryArtwork
    {
        private readonly Dictionary<string,Sprite> sprites;
        private readonly ExportedContentCatalog exported;
        private readonly HashSet<string> missing=new();
        public IEnumerable<KeyValuePair<string,Sprite>> PreparedSprites=>sprites;
        public InventoryArtwork()
        {
            sprites=WeaponArtwork.Load();
            var catalog=Resources.Load<InventoryIconAsset>("Original/Inventory/icons");
            if(catalog==null) throw new InvalidOperationException("Нет каталога иконок Original/Inventory/icons.");
            foreach(var entry in catalog.CreateLookup())
            {
                // Authored inventory icons explicitly take precedence over held artwork.
                // A stack throwable can have both a WeaponView and its own inventory icon.
                sprites[entry.Key]=entry.Value;
            }
            CosmeticItemArtwork.ApplyTo(sprites);
            exported=ExportedContentCatalog.Load();
        }
        public void Set(Image image,string id)
        {
            if(id==null) {image.enabled=false;image.sprite=null;return;}
            if(!TryGet(id,out var sprite))
            {
                if(missing.Add(id)) Debug.LogWarning($"Нет иконки инвентаря для предмета {id}. Изображение пропущено, текст сохранён.");
                image.enabled=false;image.sprite=null;return;
            }
            image.sprite=sprite;image.enabled=true;
        }
        public bool TryGet(string id,out Sprite sprite)
        {
            sprite=null;if(id==null)return false;
            if(sprites.TryGetValue(id,out sprite))return true;
            if(!exported.TryIcon(id,0,out sprite))return false;
            SpriteArtworkMaterial.Prepare(sprite);sprites.Add(id,sprite);return true;
        }
        public Sprite Require(string id)=>TryGet(id,out var sprite)?sprite:
            throw new InvalidOperationException("Нет графики брошенного предмета: definition="+id);
        public void PrepareWorldMaterials()
        {foreach(var sprite in sprites.Values) SpriteArtworkMaterial.Prepare(sprite);}
    }
}
