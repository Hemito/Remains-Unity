using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class CosmeticSelection : ExtensibleState
    {public string slot,item,definition;}
    [Serializable] public sealed class CosmeticEquipmentState : ExtensibleState
    {public int version=1;public long revision;public List<CosmeticSelection> entries=new();}

    // Selection references ordinary canonical stacks; wearing never reserves or debits a unit.
    public static class CosmeticEquipment
    {
        public const int Version=1;
        private static readonly HashSet<string> unknownVersions=new(StringComparer.Ordinal);
        public static bool IsWearable(ItemStack item,CosmeticCatalog catalog=null)
            =>item!=null && item.count>0 && (catalog??CosmeticCatalog.Current).TryGet(item.definition,out _);
        public static bool IsEquipped(ActorState actor,string itemId)
        {
            if(actor==null || string.IsNullOrEmpty(itemId)) return false;
            Validate(actor);var state=actor.cosmeticEquipment;
            if(state==null || state.version!=Version) return false;
            var item=InventoryStacks.Find(actor,itemId);if(item==null || item.count<=0) return false;
            foreach(var selection in state.entries)
                if(selection.item!=null && InventoryStacks.ResolveId(actor.inventoryAliases,selection.item)==item.id &&
                    selection.definition==item.definition) return true;
            return false;
        }
        // A resolved selection is a live canonical stack plus a declared visual capability.
        // No mutation, quantity reservation or asset lookup is performed here.
        public static bool TryResolve(ActorState actor,CosmeticSelection selection,out CosmeticDefinition definition,
            out string reason,CosmeticCatalog catalog=null)
        {
            definition=null;reason=null;
            if(actor==null) {reason="Персонаж отсутствует.";return false;}
            Validate(actor);
            if(actor.cosmeticEquipment==null || actor.cosmeticEquipment.version!=Version)
            {reason="Формат визуального снаряжения не поддерживается. Данные сохранены.";return false;}
            if(selection==null || selection.item==null) return false;
            if(!Id(selection.slot) || !Id(selection.item) || !Id(selection.definition))
                throw Error(actor,"некорректная разрешаемая запись слота");
            var item=InventoryStacks.Find(actor,selection.item);
            if(item==null || item.count<=0) {reason="Выбранная стопка снаряжения больше отсутствует.";return false;}
            if(item.definition!=selection.definition) throw Error(actor,"определение выбранной стопки не совпало");
            catalog??=CosmeticCatalog.Current;
            if(!catalog.TryGet(selection.definition,out definition))
            {
                catalog.WarnUnavailable(selection.definition,"actor="+actor.id+", slot="+selection.slot);
                reason="Для этого предмета не подключено визуальное снаряжение.";return false;
            }
            if(definition.Slot!=selection.slot || definition.Anatomy!=actor.anatomy)
                throw Error(actor,$"определение не соответствует слоту или анатомии: definition={definition.Id}, slot={selection.slot}, expectedSlot={definition.Slot}, anatomy={actor.anatomy}, expectedAnatomy={definition.Anatomy}");
            return true;
        }
        public static bool TryEquip(ActorState actor,string itemId,bool toggle,out string reason,CosmeticCatalog catalog=null)
        {
            reason=null;
            if(actor==null) {reason="Персонаж отсутствует.";return false;}
            Validate(actor);var state=actor.cosmeticEquipment;
            if(state!=null && state.version!=Version) {reason="Формат визуального снаряжения не поддерживается. Данные сохранены.";return false;}
            var item=InventoryStacks.Find(actor,itemId);
            if(item==null || InventoryAvailability.Count(actor,item)<=0)
            {reason="Предмет отсутствует или зарезервирован для передачи.";return false;}
            catalog??=CosmeticCatalog.Current;
            Validate(actor,catalog);
            if(!catalog.TryGet(item.definition,out var definition))
            {
                catalog.WarnUnavailable(item.definition,"actor="+actor.id+", item="+item.id);
                reason="Для этого предмета не подключено визуальное снаряжение.";return false;
            }
            if(definition.Anatomy!=actor.anatomy)
            {reason="Этот предмет не подходит анатомии персонажа.";return false;}
            ReconcileInventory(actor);state=actor.cosmeticEquipment;
            CosmeticSelection selection=null;
            if(state!=null) foreach(var current in state.entries) if(current.slot==definition.Slot) {selection=current;break;}
            bool same=selection?.item==item.id && selection.definition==item.definition;
            if(same && !toggle) return true;
            long revision=checked((state?.revision??0)+1);long actorRevision=checked(actor.revision+1);
            state??=new CosmeticEquipmentState();
            if(selection==null) {selection=new CosmeticSelection {slot=definition.Slot};state.entries.Add(selection);}
            // Empty slot records retain unknown selection fields and definition provenance.
            selection.item=same?null:item.id;selection.definition=item.definition;
            state.revision=revision;actor.cosmeticEquipment=state;actor.revision=actorRevision;return true;
        }
        // Call after coordinated inventory changes/merges. Entity/world revisions remain
        // owned by those operations; this method advances only the equipment selection stamp.
        public static bool ReconcileInventory(ActorState actor)
        {
            if(actor==null) return false;return ReconcileInventory(actor,actor.cosmeticEquipment);
        }
        private static bool ReconcileInventory(ActorState actor,CosmeticEquipmentState state)
        {
            Validate(actor,state);
            if(state==null || state.version!=Version) return false;
            bool changed=false;
            foreach(var selection in state.entries)
            {
                if(selection.item==null) continue;
                var live=InventoryStacks.Find(actor,selection.item);
                if(live==null || live.id!=selection.item) changed=true;
            }
            if(!changed) return false;
            long revision=checked(state.revision+1);
            foreach(var selection in state.entries)
                if(selection.item!=null) selection.item=InventoryStacks.Find(actor,selection.item)?.id;
            state.revision=revision;return true;
        }
        public static void PreserveSelection(ActorState current,ActorState incoming)
        {
            if(incoming==null) throw new ArgumentNullException(nameof(incoming));
            Validate(incoming);if(current==null) return;Validate(current);
            if(!ActorMotionReplication.SameLifetime(current,incoming) || current.character!=incoming.character || current.cosmeticEquipment==null) return;
            var previous=current.cosmeticEquipment;var next=incoming.cosmeticEquipment;
            if(next==null || previous.version!=Version && next.version==Version ||
                previous.version==next.version && previous.revision>next.revision)
                incoming.cosmeticEquipment=StateJson.Copy(previous);
        }
        public static void Reconcile(ActorState current,ActorState incoming)
        {PreserveSelection(current,incoming);ReconcileInventory(incoming);}
        // Shared grants can advance the actor revision ahead of an already sent equip.
        // Accept only this independently stamped selection, against canonical inventory.
        public static bool TryApplyOwnerSelection(ActorState current,ActorState incoming,CosmeticCatalog catalog=null)
        {
            if(!ActorMotionReplication.SameLifetime(current,incoming) || !current.player || !current.connected ||
                !incoming.player || !incoming.connected || current.character!=incoming.character || incoming.cosmeticEquipment==null) return false;
            Validate(current);Validate(incoming);
            var proposed=incoming.cosmeticEquipment;var prior=current.cosmeticEquipment;
            if(proposed.version!=Version || prior!=null && prior.version!=Version ||
                proposed.revision<=(prior?.revision??0)) return false;
            catalog??=CosmeticCatalog.Current;Validate(current,catalog);Validate(incoming,catalog);
            var prepared=StateJson.Copy(proposed);
            ReconcileInventory(current,prepared);
            long revision=checked(current.revision+1);
            current.cosmeticEquipment=prepared;current.revision=revision;return true;
        }
        public static void ValidateWorld(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            foreach(var actor in world.actors) Validate(actor);
        }
        public static void Validate(ActorState actor)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor));
            Validate(actor,actor.cosmeticEquipment);
        }
        // Explicit prepared capability validation for gameplay/presentation events.
        // Save workers use structural Validate(actor), without catalog/asset access.
        public static void Validate(ActorState actor,CosmeticCatalog catalog)
        {
            if(catalog==null) throw new ArgumentNullException(nameof(catalog));
            Validate(actor);var state=actor.cosmeticEquipment;
            if(state==null || state.version!=Version) return;
            foreach(var selection in state.entries)
                TryResolve(actor,selection,out _,out _,catalog);
        }
        private static void Validate(ActorState actor,CosmeticEquipmentState state)
        {
            if(state==null) return; // Known legacy absence.
            if(state.version<=0 || state.revision<0) throw Error(actor,"некорректная версия или отметка выбора");
            if(state.version!=Version)
            {
                string key=(actor.character??actor.id)+"/"+state.version;
                bool warn;lock(unknownVersions) warn=unknownVersions.Add(key);
                if(warn) Debug.LogWarning($"Не поддерживается формат визуального снаряжения: actor={actor.id}, version={state.version}. Компонент сохранён целиком; надевание и визуальная подмена отключены.");
                return;
            }
            if(state.entries==null) throw Error(actor,"отсутствует список слотов");
            var slots=new HashSet<string>(StringComparer.Ordinal);
            foreach(var selection in state.entries)
            {
                if(selection==null || !Id(selection.slot) || !slots.Add(selection.slot) ||
                    selection.item!=null && !Id(selection.item) || selection.definition!=null && !Id(selection.definition) ||
                    selection.item!=null && selection.definition==null)
                    throw Error(actor,"некорректная или повторная запись слота");
                if(selection.item==null) continue;
                var item=InventoryStacks.Find(actor,selection.item);
                if(item!=null && item.definition!=selection.definition)
                    throw Error(actor,$"ссылка имеет другое определение: slot={selection.slot}, item={selection.item}, saved={selection.definition}, live={item.definition}");
            }
        }
        private static bool Id(string value)=>!string.IsNullOrWhiteSpace(value) && value==value.Trim();
        private static InvalidOperationException Error(ActorState actor,string reason)=>new(
            $"Некорректное визуальное снаряжение: actor={actor.id}, character={actor.character}, version={actor.cosmeticEquipment?.version}. {reason}.");
    }
}
