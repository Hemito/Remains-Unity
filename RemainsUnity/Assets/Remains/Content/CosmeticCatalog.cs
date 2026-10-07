using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    // Capability metadata only: no protection, collision, statistics or item quantities.
    public sealed class CosmeticDefinition
    {
        public string Id {get;}
        public string Slot {get;}
        public string Visual {get;}
        public string Anatomy {get;}
        public CosmeticDefinition(string id,string slot,string visual,string anatomy)
        {Id=id;Slot=slot;Visual=visual;Anatomy=anatomy;}
    }
    public sealed class CosmeticCatalog
    {
        public const string Resource="Content/cosmetic-equipment";
        [Serializable] private sealed class Manifest
        {public int schema;public string[] slots;public Entry[] items;}
        [Serializable] private sealed class Entry
        {public string id,slot,visual,anatomy;}
        private static CosmeticCatalog loaded;
        private readonly HashSet<string> slots=new(StringComparer.Ordinal);
        private readonly Dictionary<string,CosmeticDefinition> items=new(StringComparer.Ordinal);
        private readonly HashSet<string> warnings=new(StringComparer.Ordinal);
        private readonly Action<string> warning;
        private IReadOnlyList<string> frozenSlots;
        private IReadOnlyList<CosmeticDefinition> frozenItems;
        private string fingerprint;
        public bool IsFrozen {get;private set;}
        public static CosmeticCatalog Current=>loaded??throw new InvalidOperationException(
            "Каталог визуального снаряжения не подготовлен. Вызовите CosmeticCatalog.Load при настройке контента.");
        public IReadOnlyList<string> Slots {get {Freeze();return frozenSlots;}}
        public IReadOnlyList<CosmeticDefinition> Definitions {get {Freeze();return frozenItems;}}
        public string Fingerprint {get {Freeze();return fingerprint;}}
        public CosmeticCatalog(string json,Action<string> warning=null)
        {
            this.warning=warning??(message=>Debug.LogWarning(message));
            RegisterPack(json,"конструктор каталога");
        }
        // Explicit startup load. Queries never perform Resources access or decode assets.
        public static CosmeticCatalog Load()
        {
            if(loaded!=null) return loaded;
            var asset=Resources.Load<TextAsset>(Resource);
            if(asset==null) throw new InvalidOperationException("Нет каталога визуального снаряжения: "+Resource+".");
            return loaded=new CosmeticCatalog(asset.text);
        }
        public void RegisterSlot(string id)
        {
            RequireRegistration();RequireId(id,"slot");
            if(!slots.Add(id)) throw new InvalidOperationException("Повтор слота визуального снаряжения: slot="+id+".");
        }
        public void Register(CosmeticDefinition definition,bool replace=false)
        {
            RequireRegistration();
            ValidateDefinition(definition,slots);
            if(!replace && items.ContainsKey(definition.Id)) throw new InvalidOperationException(
                "Повтор определения визуального снаряжения: definition="+definition.Id+".");
            items[definition.Id]=definition;
        }
        public void RegisterPack(string json,string source)
        {
            RequireRegistration();
            if(string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Не указан источник пакета визуального снаряжения.",nameof(source));
            try
            {
                var manifest=StateJson.FromJson<Manifest>(json);
                if(manifest==null || manifest.schema!=1 || manifest.slots==null || manifest.items==null)
                    throw new InvalidOperationException("Нужны schema=1, slots и items.");
                var nextSlots=new HashSet<string>(slots,StringComparer.Ordinal);
                var nextItems=new Dictionary<string,CosmeticDefinition>(items,StringComparer.Ordinal);
                foreach(string slot in manifest.slots)
                {
                    RequireId(slot,"slot");
                    if(!nextSlots.Add(slot)) throw new InvalidOperationException("Повтор объявленного слота: slot="+slot+".");
                }
                foreach(var entry in manifest.items)
                {
                    if(entry==null) throw new InvalidOperationException("Пустая запись предмета.");
                    var definition=new CosmeticDefinition(entry.id,entry.slot,entry.visual,entry.anatomy);
                    ValidateDefinition(definition,nextSlots);
                    if(!nextItems.TryAdd(definition.Id,definition)) throw new InvalidOperationException("Повтор определения: definition="+definition.Id+".");
                }
                // Publish only a complete validated pack; errors never retain half its declarations.
                slots.Clear();slots.UnionWith(nextSlots);items.Clear();
                foreach(var pair in nextItems) items.Add(pair.Key,pair.Value);
            }
            catch(Exception error) when(error is InvalidOperationException || error is System.IO.InvalidDataException || error is ArgumentException)
            {throw new InvalidOperationException("Не принят пакет визуального снаряжения: source="+source+". "+error.Message,error);}
        }
        public bool ContainsSlot(string id) {Freeze();return id!=null && slots.Contains(id);}
        public bool TryGet(string id,out CosmeticDefinition definition)
        {Freeze();definition=null;return id!=null && items.TryGetValue(id,out definition);}
        public CosmeticDefinition Get(string id)=>TryGet(id,out var definition)?definition:
            throw new InvalidOperationException("Нет определения визуального снаряжения: definition="+id+".");
        public void WarnUnavailable(string id,string context)
        {
            if(string.IsNullOrWhiteSpace(context)) throw new ArgumentException("Нужен контекст недоступного визуального снаряжения.",nameof(context));
            string key=id+"\n"+context;
            lock(warnings) if(!warnings.Add(key)) return;
            warning($"Не подключено визуальное снаряжение: definition={id??"<null>"}, context={context}, catalog={Resource}. Данные сохранены; подмена предмета не применяется.");
        }
        private void Freeze()
        {
            if(IsFrozen) return;
            var slotArray=slots.OrderBy(id=>id,StringComparer.Ordinal).ToArray();
            var itemArray=items.Values.OrderBy(item=>item.Id,StringComparer.Ordinal).ToArray();
            frozenSlots=Array.AsReadOnly(slotArray);frozenItems=Array.AsReadOnly(itemArray);
            var manifest=new Manifest {schema=1,slots=slotArray,items=itemArray.Select(item=>new Entry
                {id=item.Id,slot=item.Slot,visual=item.Visual,anatomy=item.Anatomy}).ToArray()};
            fingerprint=Remains.Persistence.RecoverySnapshot.Fingerprint(StateJson.ToJson(manifest));IsFrozen=true;
        }
        private void RequireRegistration()
        {if(IsFrozen) throw new InvalidOperationException("Каталог визуального снаряжения уже используется; регистрацию/замену выполняйте до первого запроса.");}
        private static void RequireId(string id,string field)
        {if(string.IsNullOrWhiteSpace(id) || id!=id.Trim()) throw new InvalidOperationException("Некорректный ID визуального снаряжения: field="+field+", value="+id+".");}
        private static void ValidateDefinition(CosmeticDefinition definition,HashSet<string> declaredSlots)
        {
            if(definition==null) throw new ArgumentNullException(nameof(definition));
            RequireId(definition.Id,"definition");RequireId(definition.Slot,"slot");
            RequireId(definition.Visual,"visual");RequireId(definition.Anatomy,"anatomy");
            if(!declaredSlots.Contains(definition.Slot)) throw new InvalidOperationException(
                $"Не объявлен слот визуального предмета: definition={definition.Id}, slot={definition.Slot}.");
        }
    }
}
