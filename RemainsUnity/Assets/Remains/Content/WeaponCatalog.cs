using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class MeleePoseKey { public float time,angle,reach; public bool rest; }
    [Serializable] public sealed class WeaponDefinition
    {
        public string id;
        public float damage,speedDamage,maxSpeedDamage,range,cooldown,manaCost;
        public string ammo;
        public int magazineSize,ammoPerShot;
        public float reloadSeconds,projectileSpeed,projectileSpeedVariation,projectileLifetime;
        public float meleeContactStart,meleeContactEnd=1;
        public MeleePoseKey[] meleeMotion;
        public Vector2 ProjectileSpeedRange=>new(projectileSpeed*(1-projectileSpeedVariation),projectileSpeed*(1+projectileSpeedVariation));
        public float FlightRange(float speed)=>projectileLifetime>0?speed*projectileLifetime:range;
        public bool stopAtFirstActor,stopAtTerrain;
    }
    [Serializable] public sealed class WeaponList { public WeaponDefinition[] weapons; }
    [Serializable] public sealed class ModOrder { public string[] mods; }
    [Serializable] public sealed class ModManifest { public string id,version; public string[] dependencies; public WeaponOverride[] weaponOverrides; public CodeModEntry code; }
    [Serializable] public sealed class WeaponOverride { public string id,source; }

    public static class WeaponCatalog
    {
        private static Dictionary<string,WeaponDefinition> definitions=new();
        private static ContentManifest manifest;
        public static Remains.Domain.GameplayRuleSet Rules {get;private set;}
        public static ContentManifest ManifestCopy()=>manifest?.Copy() ?? throw new InvalidOperationException("Каталог контента ещё не загружен.");
        public static IEnumerable<WeaponDefinition> All => definitions.Values;
        public static string ModFingerprint { get; private set; }="";
        public static string ModDescription { get; private set; }="";
        public static WeaponDefinition Get(string id)=>definitions.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException($"Отсутствует определение оружия: {id}.");

        public static void Load()
        {
            WeaponRigs.Load();
            ActorTypes.Load();PropCatalog.Load();
            var asset=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(asset==null) throw new InvalidOperationException("Не найден каталог оружия Content/WeaponCatalog.");
            var loaded=WeaponCatalogLoader.Load(asset.CreateJson(),Path.Combine(Application.streamingAssetsPath,"Mods"));
            var inputs=new SortedDictionary<string,string>(StringComparer.Ordinal);
            foreach(string path in new[]{"Content/anatomy","Content/movement","Content/telekinesis","Content/hub-training","Original/Training/artwork","Original/Raider/artwork","Original/Pony/pony","Original/Pony/motion-frames","Original/Weapons/artwork"})
            {
                var data=Resources.Load<TextAsset>(path);
                if(data==null) throw new InvalidOperationException($"Нет данных игрового манифеста: {path}.");
                inputs.Add(path,data.text);
            }
            inputs.Add("Content/prototype-room",Remains.Serialization.StateJson.ToJson(Remains.Domain.RoomLayout.Load()));
            inputs.Add("Content/prototype-hub",Remains.Serialization.StateJson.ToJson(Remains.Domain.RoomLayout.Load("hub")));
            var entities=EntityAssetCatalog.Load();
            inputs.Add("Content/actors",entities.ActorsJson());inputs.Add("Content/props",entities.PropsJson());
            var complete=GameplayContentManifest.Compose(loaded.manifest,inputs);
            var rules=GameplayCodeMods.Compose(loaded.codeMods);
            definitions=loaded.definitions;
            manifest=complete;Rules=rules;
            ModFingerprint=complete.fingerprint;ModDescription=loaded.description+"\nГеометрия, анатомия и позы: "+inputs.Count+" набора данных";
        }
    }
}
