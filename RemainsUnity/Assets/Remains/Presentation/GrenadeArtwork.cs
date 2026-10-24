using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    public static class GrenadeArtwork
    {
        public static SpriteRenderer LoadPrefab()
        {
            var definition=GameAssets.Load<WeaponAsset>("Content/weapons/grenade");
            if(definition==null || definition.GrenadeFlight==null)
                throw new InvalidOperationException("Граната grenade: не назначен префаб полёта в Content/weapons/grenade.");
            var prefab=definition.GrenadeFlight;
            if(prefab.sprite==null || prefab.sharedMaterial==null)
                throw new InvalidOperationException($"Префаб гранаты {prefab.name}: отсутствует спрайт или материал.");
            return prefab;
        }
    }
}
