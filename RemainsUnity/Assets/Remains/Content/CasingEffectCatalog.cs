using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    // A null profile is an explicit no-casing declaration, not an unknown weapon.
    // Prepare once during content loading; owner launch and presentation share it.
    public static class CasingEffectCatalog
    {
        private static Dictionary<string,CasingEffectProfile> loaded;
        private static CasingEffectProfile[] profiles=Array.Empty<CasingEffectProfile>();
        public static IReadOnlyList<CasingEffectProfile> Profiles=>loaded!=null?profiles:
            throw new InvalidOperationException("Каталог гильз ещё не подготовлен.");
        public static void Load()=>Load(GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog")??
            throw new InvalidOperationException("Нет Content/WeaponCatalog для эффектов гильз."));
        public static void Load(WeaponAssetCatalog catalog)
        {
            if(catalog==null)throw new ArgumentNullException(nameof(catalog));
            var next=new Dictionary<string,CasingEffectProfile>(StringComparer.Ordinal);var unique=new HashSet<CasingEffectProfile>();
            foreach(var weapon in catalog.AllAssets())
            {
                if(string.IsNullOrWhiteSpace(weapon.Id) || !next.TryAdd(weapon.Id,weapon.CasingProfile))
                    throw new InvalidOperationException("Каталог гильз: пустой или повторный ID оружия, catalog="+catalog.name+", weapon="+weapon.Id);
            }
            foreach(var weapon in catalog.CasingWeapons())unique.Add(weapon.CasingProfile);
            var prepared=new CasingEffectProfile[unique.Count];unique.CopyTo(prepared);loaded=next;profiles=prepared;
        }
        public static bool TryGet(string weapon,out CasingEffectProfile profile)
        {
            if(loaded==null)throw new InvalidOperationException("Каталог гильз ещё не подготовлен: weapon="+weapon);
            if(string.IsNullOrWhiteSpace(weapon) || !loaded.TryGetValue(weapon,out profile))
                throw new InvalidOperationException("В каталоге гильз нет явного назначения оружия: weapon="+weapon);
            if(ReferenceEquals(profile,null))return false;
            profile.Validate("cached назначение оружия "+weapon);return true;
        }
    }
}
