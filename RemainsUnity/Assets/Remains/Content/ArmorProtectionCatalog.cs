using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    // A full-condition armor definition. The selected inventory instance remains the source of equipment state.
    public sealed class ArmorProtectionProfile
    {
        public string Id {get;}
        public float Physical {get;}
        public float Energy {get;}
        public float Quality {get;}
        private readonly Dictionary<string,float> resistances;
        public IReadOnlyDictionary<string,float> Resistances {get;}
        public ArmorProtectionProfile(string id,float physical,float energy,float quality,IReadOnlyDictionary<string,float> resistances=null)
        {
            if(string.IsNullOrWhiteSpace(id) || id!=id.Trim() || !float.IsFinite(physical) || physical<0 ||
                !float.IsFinite(energy) || energy<0 || !float.IsFinite(quality) || quality<0 || quality>1)
                throw new InvalidOperationException("Некорректная защита брони: definition="+id+".");
            Id=id;Physical=physical;Energy=energy;Quality=quality;
            this.resistances=new Dictionary<string,float>(StringComparer.Ordinal);
            Resistances=new System.Collections.ObjectModel.ReadOnlyDictionary<string,float>(this.resistances);
            if(resistances==null) return;
            foreach(var entry in resistances)
            {
                DamageTypes.Validate(entry.Key);
                if(!float.IsFinite(entry.Value) || entry.Value>1)
                    throw new InvalidOperationException($"Некорректное сопротивление брони: definition={id}, type={entry.Key}, resistance={entry.Value}.");
                this.resistances.Add(entry.Key,entry.Value);
            }
        }
        // An absent entry explicitly means no resistance; negative values are authored vulnerabilities.
        public float Resistance(string damageType)
        {DamageTypes.Validate(damageType);return resistances.TryGetValue(damageType,out float value)?value:0;}
        internal ArmorProtectionCatalog.Entry Entry()=>new() {id=Id,physical=Physical,energy=Energy,quality=Quality,
            resistances=resistances.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>new ArmorProtectionCatalog.ResistanceEntry {type=pair.Key,value=pair.Value}).ToArray()};
    }

    public sealed class ArmorProtectionCatalog
    {
        public const string Resource="Content/armor-protection";
        [Serializable] internal sealed class Manifest {public int schema;public string sourceHash;public Entry[] items;}
        [Serializable] internal sealed class Entry {public string id;public float physical,energy,quality;public ResistanceEntry[] resistances;}
        [Serializable] internal sealed class ResistanceEntry {public string type;public float value;}
        private static ArmorProtectionCatalog loaded;
        private readonly Dictionary<string,ArmorProtectionProfile> profiles=new(StringComparer.Ordinal);
        private readonly string sourceHash;
        private bool frozen;
        private string fingerprint;
        public static ArmorProtectionCatalog Current=>loaded??throw new InvalidOperationException("Каталог защиты брони ещё не подготовлен.");
        public string Fingerprint {get {Freeze();return fingerprint;}}
        public int Count=>profiles.Count;
        public ArmorProtectionCatalog(string json)
        {
            var manifest=StateJson.FromJson<Manifest>(json);
            if(manifest==null || manifest.schema!=1 || manifest.items==null || string.IsNullOrWhiteSpace(manifest.sourceHash))
                throw new InvalidOperationException("Некорректный каталог защиты брони: нужны schema=1, sourceHash и items.");
            sourceHash=manifest.sourceHash;
            foreach(var entry in manifest.items)
            {
                if(entry?.resistances==null) throw new InvalidOperationException("Нет явного списка сопротивлений брони.");
                var resistance=new Dictionary<string,float>(StringComparer.Ordinal);
                foreach(var value in entry.resistances)
                    if(value==null || value.type==null || !resistance.TryAdd(value.type,value.value))
                        throw new InvalidOperationException("Повторное или некорректное сопротивление брони: definition="+entry.id+".");
                Register(new ArmorProtectionProfile(entry.id,entry.physical,entry.energy,entry.quality,resistance));
            }
        }
        // Load explicitly during content composition; combat queries never access Resources or parse JSON.
        public static ArmorProtectionCatalog Load()
        {
            if(loaded!=null) return loaded;
            var asset=GameAssets.Load<TextAsset>(Resource);
            if(asset==null) throw new InvalidOperationException("Не найден каталог защиты брони: "+Resource+".");
            return loaded=new ArmorProtectionCatalog(asset.text);
        }
        public void Register(ArmorProtectionProfile profile,bool replace=false)
        {
            if(frozen) throw new InvalidOperationException("Каталог защиты брони уже используется; регистрацию выполняйте при подготовке контента.");
            if(profile==null) throw new ArgumentNullException(nameof(profile));
            if(!replace && profiles.ContainsKey(profile.Id)) throw new InvalidOperationException("Повтор защиты брони: definition="+profile.Id+".");
            profiles[profile.Id]=profile;
        }
        public bool TryGet(string definitionId,out ArmorProtectionProfile profile)
        {Freeze();profile=null;return definitionId!=null && profiles.TryGetValue(definitionId,out profile);}
        public ArmorProtectionProfile Get(string definitionId)=>TryGet(definitionId,out var profile)?profile:
            throw new InvalidOperationException("Не найдена защита брони: definition="+definitionId+".");
        private void Freeze()
        {
            if(frozen) return;
            fingerprint=Remains.Persistence.RecoverySnapshot.Fingerprint(StateJson.ToJson(new Manifest {schema=1,sourceHash=sourceHash,
                items=profiles.Values.OrderBy(profile=>profile.Id,StringComparer.Ordinal).Select(profile=>profile.Entry()).ToArray()}));
            frozen=true;
        }
    }
}
