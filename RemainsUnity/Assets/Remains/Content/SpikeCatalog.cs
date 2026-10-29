using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    // Spikes of the original (loc/Trap.as; AllData obj tip='trap'): a fixed object of the level that hurts a unit which
    // falls into it (attack "fall", att 1) or rises into it head first (attack "rise", att 2). Lengths in metres
    // (original px / 50); damage in the original's units. Generated from the original export by SpikeAuthoring.
    [Serializable] public sealed class SpikeDefinition
    {
        public const string Fall="fall",Rise="rise";
        public string id,title;
        public string[] objects;          // room template objects (AllData obj) that place it
        public string attack;             // Fall (att 1) or Rise (att 2)
        public float damage;              // AllData damage
        public string damageType;         // DamageTypes of AllData tipdam
        public float width,height;        // the hurting box: size × tileX wide, sY (or wid × tileY) high
        // AllData floor: the box stands on the bottom of its cell; otherwise it hangs from the cell's top (Trap.as Y1).
        public bool bottomAnchored;
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || objects==null || objects.Length==0 ||
                attack!=Fall && attack!=Rise || !float.IsFinite(damage) || damage<=0 || string.IsNullOrWhiteSpace(damageType) ||
                !float.IsFinite(width) || width<=0 || !float.IsFinite(height) || height<=0)
                throw new InvalidOperationException($"Некорректное описание шипов: {id} ({attack}).");
            Remains.Domain.DamageTypes.Validate(damageType);
        }
    }

    public sealed class SpikeCatalog : ScriptableObject
    {
        public const string Resource="Content/SpikeCatalog";
        [SerializeField] private SpikeDefinition[] definitions=Array.Empty<SpikeDefinition>();
        public IReadOnlyList<SpikeDefinition> Definitions=>definitions;
        public void Configure(SpikeDefinition[] values)=>definitions=values??throw new ArgumentNullException(nameof(values));

        private static Dictionary<string,SpikeDefinition> loaded;
        private static Dictionary<string,SpikeDefinition> byObject;
        public static string Json {get;private set;}="";
        public static SpikeDefinition Get(string id)=>loaded!=null && id!=null && loaded.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException(loaded==null?"Каталог шипов не загружен.":$"Нет описания шипов: {id ?? "<null>"}.");
        // The spikes a room template object places (null: none).
        public static SpikeDefinition ForObject(string objectId)=>
            (byObject??throw new InvalidOperationException("Каталог шипов не загружен.")).TryGetValue(objectId,out var value)?value:null;

        // Loaded with the weapons (WeaponCatalog.Load), as the hazards.
        public static void Load()
        {
            var asset=GameAssets.Load<SpikeCatalog>(Resource)??throw new InvalidOperationException("Нет каталога шипов "+Resource+".");
            var definitions=new Dictionary<string,SpikeDefinition>(StringComparer.Ordinal);
            var objects=new Dictionary<string,SpikeDefinition>(StringComparer.Ordinal);
            foreach(var definition in asset.definitions)
            {
                definition?.Validate();
                if(definition==null || !definitions.TryAdd(definition.id,definition)) throw new InvalidOperationException("Пустое или повторное описание шипов: "+definition?.id);
                foreach(string obj in definition.objects)
                    if(!objects.TryAdd(obj,definition)) throw new InvalidOperationException($"Объект {obj} ставит несколько описаний шипов.");
            }
            if(definitions.Count==0) throw new InvalidOperationException("Каталог шипов пуст: "+Resource+".");
            loaded=definitions;byObject=objects;Json=JsonUtility.ToJson(asset);
        }
    }
}
