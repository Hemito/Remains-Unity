using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    [Serializable] public sealed class InventoryIcon {public string id,resource;public int x,y,width,height;}
    [Serializable] public sealed class InventoryIconCatalog {public InventoryIcon[] icons;}
    public sealed class InventoryArtwork
    {
        private readonly Dictionary<string,Sprite> sprites;
        private readonly HashSet<string> missing=new();
        public InventoryArtwork()
        {
            sprites=WeaponArtwork.Load();
            var data=Resources.Load<TextAsset>("Original/Inventory/icons");
            if(data==null) throw new InvalidOperationException("Нет каталога Original/Inventory/icons.");
            var catalog=JsonUtility.FromJson<InventoryIconCatalog>(data.text);
            if(catalog?.icons==null) throw new InvalidOperationException("Пустой каталог иконок инвентаря.");
            foreach(var entry in catalog.icons)
            {
                var texture=Resources.Load<Texture2D>(entry.resource);
                if(texture==null || entry.x<0 || entry.y<0 || entry.width<=0 || entry.height<=0 ||
                    entry.x+entry.width>texture.width || entry.y+entry.height>texture.height)
                    throw new InvalidOperationException($"Некорректная иконка: id={entry.id}, resource={entry.resource}.");
                sprites.Add(entry.id,Sprite.Create(texture,new Rect(entry.x,entry.y,entry.width,entry.height),new Vector2(.5f,.5f),50));
            }
        }
        public Image Create(Transform parent,Vector2 position,Vector2 size,string id=null)
        {
            var obj=new GameObject("Иконка предмета",typeof(RectTransform));obj.transform.SetParent(parent,false);
            HudElements.Rect(obj.GetComponent<RectTransform>(),position,size);
            var image=obj.AddComponent<Image>();image.raycastTarget=false;image.preserveAspect=true;Set(image,id);return image;
        }
        public void Set(Image image,string id)
        {
            if(id==null) {image.enabled=false;image.sprite=null;return;}
            if(!sprites.TryGetValue(id,out var sprite))
            {
                if(missing.Add(id)) Debug.LogWarning($"Нет иконки инвентаря для предмета {id}. Изображение пропущено, текст сохранён.");
                image.enabled=false;image.sprite=null;return;
            }
            image.sprite=sprite;image.enabled=true;
        }
    }
}
