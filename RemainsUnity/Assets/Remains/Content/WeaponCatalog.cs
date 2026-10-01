using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class WeaponDefinition
    {
        public string id;
        public float damage,speedDamage,maxSpeedDamage,range,cooldown,manaCost;
        public string ammo;
        public int magazineSize,ammoPerShot;
        public float reloadSeconds;
    }
    [Serializable] public sealed class WeaponList { public WeaponDefinition[] weapons; }
    [Serializable] public sealed class ModOrder { public string[] mods; }
    [Serializable] public sealed class ModManifest { public string id,version; public string[] dependencies; public WeaponOverride[] weaponOverrides; }
    [Serializable] public sealed class WeaponOverride { public string id,source; }

    public static class WeaponCatalog
    {
        private static Dictionary<string,WeaponDefinition> definitions=new();
        private static ContentManifest manifest;
        public static ContentManifest ManifestCopy()=>manifest?.Copy() ?? throw new InvalidOperationException("Каталог контента ещё не загружен.");
        public static IEnumerable<WeaponDefinition> All => definitions.Values;
        public static string ModFingerprint { get; private set; }="";
        public static string ModDescription { get; private set; }="";
        public static WeaponDefinition Get(string id)=>definitions.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException($"Отсутствует определение оружия: {id}.");

        public static void Load()
        {
            var asset=Resources.Load<TextAsset>("Content/weapons");
            if(asset==null) throw new InvalidOperationException("Не найдены определения оружия Content/weapons.");
            var loaded=WeaponCatalogLoader.Load(asset.text,Path.Combine(Application.streamingAssetsPath,"Mods"));
            definitions=loaded.definitions;
            manifest=loaded.manifest;
            ModFingerprint=loaded.fingerprint;ModDescription=loaded.description;
        }
    }
}
