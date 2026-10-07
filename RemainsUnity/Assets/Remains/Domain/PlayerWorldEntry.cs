using System;
using System.Linq;
using Remains.Serialization;

namespace Remains.Domain
{
    // Personal progress follows the character. A disconnected body outside the hub
    // remains world-owned until that same body is resumed, including its consequences.
    public static class PlayerWorldEntry
    {
        public static ActorState Reconcile(WorldState world,ActorState saved,ActorState profile,out string notice)
        {
            notice=null;
            if(saved==null) return profile;
            if(!saved.player || saved.connected || profile!=null && (!profile.player || profile.character!=saved.character))
                throw new InvalidOperationException($"Нельзя согласовать вход персонажа: world={world.worldId}, actor={saved.id}, character={saved.character}, connected={saved.connected}.");
            if(saved.level!="hub")
            {
                if(profile!=null && (StateJson.ToJson(profile.inventory)!=StateJson.ToJson(saved.inventory) ||
                    StateJson.ToJson(profile.appearance)!=StateJson.ToJson(saved.appearance) || profile.health!=saved.health))
                    notice=$"Персонаж {saved.character} возвращается в оставленное тело: его здоровье, вещи и последствия в этом мире сохранены. Отличающийся личный профиль не объединяется с телом автоматически.";
                CosmeticEquipment.ReconcileInventory(saved);return saved;
            }
            if(profile==null)
                throw new InvalidOperationException($"Для входа из хаба нужен личный профиль: world={world.worldId}, actor={saved.id}, character={saved.character}. Сохранённая копия мира не заменяет библиотеку персонажей.");
            var result=profile.Copy();
            // Reapply only coordinated, recorded debits not yet present in this profile.
            // Never merge unrelated personal journals or restore stale weapon state.
            var coordinated=saved.inventoryUses.Where(use=>world.completedTransactions.Contains(use.id) ||
                world.pendingDeposits.Any(pending=>pending.source==saved.id && pending.transaction==use.id)).ToList();
            foreach(var pending in world.pendingDeposits.Where(pending=>pending.source==saved.id))
                if(!coordinated.Any(use=>use.id==pending.transaction))
                    throw new InvalidOperationException($"Не найден журнал резерва: world={world.worldId}, actor={saved.id}, transaction={pending.transaction}. Вход отменён, исходные состояния сохранены.");
            foreach(var use in coordinated)
            {
                var existing=result.inventoryUses.Find(value=>value.id==use.id);
                if(existing!=null && StateJson.ToJson(existing)!=StateJson.ToJson(use))
                    throw new InvalidOperationException($"Различается журнал операции: world={world.worldId}, actor={saved.id}, transaction={use.id}. Автоматическое объединение отменено.");
            }
            var debitJournal=new ActorState {inventoryUses=coordinated};
            if(!InventoryJournal.Merge(result,debitJournal,out var inventory,out var uses))
                throw new InvalidOperationException($"Личный профиль не согласуется с подтверждённым расходом: world={world.worldId}, actor={saved.id}. Вход отменён, копии сохранены; требуется восстановление согласованной пары мира и персонажа.");
            result.inventory=inventory;result.inventoryUses=uses;
            CosmeticEquipment.ReconcileInventory(result);
            foreach(var use in coordinated)
                foreach(var debit in use.debits) WeaponInventory.ClearConsumedSelection(result,debit.item);
            result.id=saved.id;result.character=saved.character;result.player=true;
            result.level=saved.level;result.x=saved.x;result.y=saved.y;result.facing=saved.facing;
            result.generation=saved.generation;result.lifecycleSequence=saved.lifecycleSequence;
            result.revision=Math.Max(saved.revision,result.revision);
            result.connected=false;result.owner=null;result.target=null;
            result.vx=result.vy=0;result.climbing=result.crouched=false;
            result.attackBusy=false;result.attackRemaining=0;result.attackId=result.attackTarget=null;
            ActorPose.Select(result,result.down?"down":"idle");
            return result;
        }
    }
}
