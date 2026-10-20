using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class MeleePoseKey { public float time,angle,reach; public bool rest; }
    // A mine (original Mine.as): placed by the level or a player, it waits for a hostile actor inside its sensing
    // zone, counts down and explodes as its weapon. Lengths in metres (original px / 50), times in seconds (frames / 30).
    [Serializable] public sealed class MineProfile
    {
        public float sensitivity;    // half-width of the sensing zone; it reaches sensitivity above and 0.4 × below the mine
        public float delaySeconds;   // trigger → explosion
        public float visibility;     // a player notices an unnoticed mine within this distance (interim, no perception skill)
        public float width,height;   // drawn and interaction size
        public float mass;           // lying, for telekinesis (original massafix/50; preliminary kilograms)
        public string artwork;       // exported vector resource: frame 1 disarmed, 2 armed, 3+ triggered
        public string beepSound,defuseSound,placeSound;   // exported sound resources
        public bool Configured=>sensitivity>0;
    }
    [Serializable] public sealed class WeaponDefinition
    {
        public string id;
        public float damage,speedDamage,maxSpeedDamage,range,cooldown,manaCost;
        public string ammo;
        public int magazineSize,ammoPerShot;
        public float reloadSeconds,projectileSpeed,projectileSpeedVariation,projectileLifetime;
        [Min(0),InspectorName("Самоподзарядка одного заряда, с (0 — отключена)")] public float rechargeSeconds;
        public float shotSpreadHalfAngleDegrees;
        public WeaponRecoilProfile recoil;
        // An explosion (grenade, mine): resolved as a persisted ExplosionState around its position within `range`;
        // it may hurt its own author, does not take the direct friendly-fire choice, damages no doors or lamps directly.
        public bool explosive;
        public MineProfile mine;
        public float grenadeFuseSeconds,grenadeRestitution,grenadeGroundRetention,grenadeGravity;
        public float grenadeMaxLaunchSpeed,grenadeFullEffortDistance,grenadeAimLift;
        public float grenadeSpinRate,grenadeAngularRetention;
        public float meleeContactStart,meleeContactEnd=1;
        public MeleePoseKey[] meleeMotion;
        public HoldingArea holdingArea;
        public Vector2 ProjectileSpeedRange=>new(projectileSpeed*(1-projectileSpeedVariation),projectileSpeed*(1+projectileSpeedVariation));
        public float FlightRange(float speed)=>projectileLifetime>0?speed*projectileLifetime:range;
        public bool stopAtFirstActor,stopAtTerrain;
        // Projectiles per shot (original kol): a volley fanned shotSpreadHalfAngleDegrees apart around the aim, each pellet
        // with its own spread and damage. 0 or 1: a single projectile.
        public int pellets;
        public int Pellets=>Math.Max(1,pellets);
        // A magazine refilled by reloading without ammunition items (original turret guns: holder and reload with an
        // endless supply). Weapons with an ammo item use their magazine as well.
        public bool unlimitedReserve;
        public bool UsesMagazine=>!string.IsNullOrEmpty(ammo) || unlimitedReserve || rechargeSeconds>0;
    }
    [Serializable] public sealed class WeaponList { public WeaponDefinition[] weapons; }
    [Serializable] public sealed class ModOrder { public string[] mods; }
    [Serializable] public sealed class ModManifest { public string id,version; public string[] dependencies; public WeaponOverride[] weaponOverrides; public CodeModEntry code; public CosmeticPackEntry cosmetics; }
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
        public static bool Contains(string id)=>id!=null && definitions.ContainsKey(id);
        public static bool IsExplosive(string id)=>id!=null && definitions.TryGetValue(id,out var value) && value.explosive;
        public static bool IsMine(string id)=>id!=null && definitions.TryGetValue(id,out var value) && value.explosive && value.mine!=null && value.mine.Configured;

        public static void Load()
        {
            WeaponRigs.Load();
            ActorTypes.Load();PropCatalog.Load();HazardCatalog.Load();
            var asset=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(asset==null) throw new InvalidOperationException("Не найден каталог оружия Content/WeaponCatalog.");
            CasingEffectCatalog.Load();
            FootSupportCatalog.Load();
            var loaded=WeaponCatalogLoader.Load(asset.CreateJson(),Path.Combine(Application.streamingAssetsPath,"Mods"));
            CosmeticContentMods.Prepare(loaded);
            // Code mods may register additional visuals/definitions during Configure, before first queries freeze the registries.
            var rules=Rules??GameplayCodeMods.Compose(loaded.codeMods);
            var inputs=new SortedDictionary<string,string>(StringComparer.Ordinal);
            inputs.Add(ExportedContentCatalog.ResourcePath,ExportedContentCatalog.Load().sourceHash);
            var factoryLoot=Resources.Load<FactoryLootCatalog>("Content/FactoryLoot")??throw new InvalidOperationException("Нет каталога исходного лута завода.");
            inputs.Add("Content/FactoryLoot",factoryLoot.sourceHash+"|"+FactoryContainerLoot.Version+"|"+
                factoryLoot.baseDifficulty.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+
                factoryLoot.depthDifficulty.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+factoryLoot.gameStage);
            inputs.Add(CosmeticCatalog.Resource,CosmeticCatalog.Current.Fingerprint);
            inputs.Add("Presentation/visual-equipment",WearableRenderCatalog.ContentFingerprint);
            inputs.Add(FootSupportCatalog.Resource,FootSupportCatalog.Fingerprint);
            inputs.Add(HazardCatalog.Resource,HazardCatalog.Json);
            inputs.Add("Rules/actor-body-support","sampled-centres-v1");
            inputs.Add("Rules/prop-impacts","multi-victim-v2|level-clock|live-impulse-epoch|canonical-admission-v1|transient-source-history");
            inputs.Add("Rules/factory-enemy-scaling","floor-v3|unit:hp=.11,damage=.07,protection=.05|turret:hp=.20,damage=.12,protection=.10|mine:after10=.10|saved-baseline-v1");
            foreach(string path in new[]{"Content/anatomy","Content/movement","Content/telekinesis","Content/standing-edge-poses",SlopePoseCatalog.Resource,"Original/Training/artwork","Original/Pony/pony","Original/Pony/motion-frames"})
            {
                var data=Resources.Load<TextAsset>(path);
                if(data==null) throw new InvalidOperationException($"Нет данных игрового манифеста: {path}.");
                inputs.Add(path,data.text);
            }
            inputs.Add("Content/prototype-room",Remains.Serialization.StateJson.ToJson(RoomAuthoring.Load("factory:0").CreateLayout(includeArtwork:false)));
            inputs.Add("Content/prototype-hub",Remains.Serialization.StateJson.ToJson(RoomAuthoring.Load("hub").CreateLayout(includeArtwork:false)));
            inputs.Add("Content/hub-training",Remains.Serialization.StateJson.ToJson(HubTrainingContent.ReadLayout()));
            foreach(var entry in RoomCatalogAsset.Load().Entries)
            {
                inputs.Add("Content/rooms/"+entry.id,Remains.Serialization.StateJson.ToJson(entry.room.CreateLayout(includeArtwork:false)));
                inputs.Add("Content/room-objects/"+entry.id,Remains.Serialization.StateJson.ToJson(RoomContentBatch.Read(entry.room,entry.id)));
            }
            foreach(string level in new[]{"hub","factory:0"})
            {
                var doors=RoomDoorContent.Read(RoomAuthoring.Load(level),level);
                if(doors!=null) inputs.Add("Content/doors/"+level,Remains.Serialization.StateJson.ToJson(doors));
                var physical=RoomPhysicalContent.Read(RoomAuthoring.Load(level),level);
                if(physical!=null) inputs.Add("Content/physical/"+level,Remains.Serialization.StateJson.ToJson(physical));
                var npcs=RoomNpcContent.Read(RoomAuthoring.Load(level),level);
                if(npcs!=null) inputs.Add("Content/npcs/"+level,Remains.Serialization.StateJson.ToJson(npcs));
            }
            var entities=EntityAssetCatalog.Load();
            inputs.Add("Content/actors",entities.ActorsJson());inputs.Add("Content/props",entities.PropsJson());
            inputs.Add("Original/Weapons/artwork",WeaponRigs.ManifestJson());
            foreach(string key in entities.CreateActors().Where(a=>a.presentation=="atlas").Select(a=>a.artwork).Distinct(StringComparer.Ordinal))
                inputs.Add(key,JsonUtility.ToJson(AtlasAnimationData.Get(key)));
            var complete=GameplayContentManifest.Compose(loaded.manifest,inputs);
            definitions=loaded.definitions;
            HazardCatalog.ValidateWeapons();
            manifest=complete;Rules=rules;
            ModFingerprint=complete.fingerprint;ModDescription=loaded.description+"\nГеометрия, анатомия и позы: "+inputs.Count+" набора данных";
        }
    }
}

