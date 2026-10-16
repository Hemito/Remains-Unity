using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Content
{
    // An original unit that is a fixed hazard (HazardState): trap (UnitTrap) or charge (UnitDamager un tip 3). Lengths
    // in metres (original px / 50); health and skin in the original's units, as the creatures'. Generated from the
    // original export by HazardAuthoring.
    [Serializable] public sealed class HazardDefinition
    {
        public string id,kind,title;
        public string[] objects;          // room template objects (AllData obj) that place it
        public float width,height,health,skin,visibility;
        public string artwork;            // exported vector resource: trap frames 1 open, 2–4 snapping, 4 shut, 5–20 reopening
        public string biteWeapon;         // trap: its bite (WeaponCatalog, AllData comb damage)
        public string blastWeapon;        // charge: its explosion (WeaponCatalog)
        public string snapSound,rearmSound;   // trap: exported sound resources (trap_a, trap_reload)
        public string disarmItem;         // charge: the item disarming leaves (an exported item key), disarmCount of them
        public int disarmCount;
        public void Validate()
        {
            bool trap=kind==Remains.Domain.HazardRules.Trap,charge=kind==Remains.Domain.HazardRules.Charge;
            if(string.IsNullOrWhiteSpace(id) || !trap && !charge || string.IsNullOrWhiteSpace(title) || objects==null || objects.Length==0 ||
                !float.IsFinite(width) || width<=0 || !float.IsFinite(height) || height<=0 || !float.IsFinite(health) || health<=0 ||
                !float.IsFinite(skin) || skin<0 || !float.IsFinite(visibility) || visibility<=0 || string.IsNullOrWhiteSpace(artwork) ||
                trap && (string.IsNullOrWhiteSpace(biteWeapon) || string.IsNullOrWhiteSpace(snapSound) || string.IsNullOrWhiteSpace(rearmSound)) ||
                charge && (string.IsNullOrWhiteSpace(blastWeapon) || string.IsNullOrWhiteSpace(disarmItem) || disarmCount<=0))
                throw new InvalidOperationException($"Некорректное описание ловушки: {id} ({kind}).");
        }
    }

    public sealed class HazardCatalog : ScriptableObject
    {
        public const string Resource="Content/HazardCatalog";
        [SerializeField] private HazardDefinition[] definitions=Array.Empty<HazardDefinition>();
        public IReadOnlyList<HazardDefinition> Definitions=>definitions;
        public void Configure(HazardDefinition[] values)=>definitions=values??throw new ArgumentNullException(nameof(values));

        private static Dictionary<string,HazardDefinition> loaded;
        private static Dictionary<string,HazardDefinition> byObject;
        public static string Json {get;private set;}="";
        public static HazardDefinition Get(string id)=>loaded!=null && id!=null && loaded.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException(loaded==null?"Каталог ловушек не загружен.":$"Нет описания ловушки: {id ?? "<null>"}.");
        // The hazard a room template object places (null: none).
        public static HazardDefinition ForObject(string objectId)=>
            (byObject??throw new InvalidOperationException("Каталог ловушек не загружен.")).TryGetValue(objectId,out var value)?value:null;
        public static IEnumerable<HazardDefinition> All=>loaded?.Values??throw new InvalidOperationException("Каталог ловушек не загружен.");

        // Loaded with the weapons (WeaponCatalog.Load): the definitions name their bite and blast weapons.
        public static void Load()
        {
            var asset=Resources.Load<HazardCatalog>(Resource)??throw new InvalidOperationException("Нет каталога ловушек "+Resource+".");
            var definitions=new Dictionary<string,HazardDefinition>(StringComparer.Ordinal);
            var objects=new Dictionary<string,HazardDefinition>(StringComparer.Ordinal);
            foreach(var definition in asset.definitions)
            {
                definition?.Validate();
                if(definition==null || !definitions.TryAdd(definition.id,definition)) throw new InvalidOperationException("Пустое или повторное описание ловушки: "+definition?.id);
                foreach(string obj in definition.objects)
                    if(!objects.TryAdd(obj,definition)) throw new InvalidOperationException($"Объект шаблона {obj} ставит две ловушки: {objects[obj].id}, {definition.id}.");
            }
            loaded=definitions;byObject=objects;
            Json=JsonUtility.ToJson(asset);
        }
        public static void ValidateWeapons()
        {
            foreach(var definition in All)
            {
                if(definition.kind==Remains.Domain.HazardRules.Trap && WeaponCatalog.Get(definition.biteWeapon) is var bite && (bite.explosive || bite.projectileSpeed>0 || bite.meleeMotion!=null))
                    throw new InvalidOperationException($"Ловушка {definition.id}: укус {definition.biteWeapon} должен быть мгновенным контактом.");
                if(definition.kind==Remains.Domain.HazardRules.Charge && !WeaponCatalog.IsExplosive(definition.blastWeapon))
                    throw new InvalidOperationException($"Заряд {definition.id}: взрыв {definition.blastWeapon} не взрывчатое оружие.");
            }
        }
    }
}
