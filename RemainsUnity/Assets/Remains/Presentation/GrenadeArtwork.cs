using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    public static class GrenadeArtwork
    {
        // The flight prefab of a hand grenade (WeaponAsset.GrenadeFlight), found in the weapon catalog by the grenade's ID.
        public static SpriteRenderer LoadPrefab(string weapon="grenade")
        {
            var catalog=GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog")??throw new InvalidOperationException("Нет каталога оружия Content/WeaponCatalog.");
            WeaponAsset definition=null;
            foreach(var asset in catalog.AllAssets()) if(asset!=null && asset.Id==weapon) {definition=asset;break;}
            if(definition==null || definition.GrenadeFlight==null)
                throw new InvalidOperationException($"Граната {weapon}: не назначен префаб полёта в каталоге оружия.");
            var prefab=definition.GrenadeFlight;
            if(prefab.sprite==null || prefab.sharedMaterial==null)
                throw new InvalidOperationException($"Префаб гранаты {prefab.name}: отсутствует спрайт или материал.");
            return prefab;
        }
    }
}
