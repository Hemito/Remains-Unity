using System;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Evidence only: these comparisons neither choose an owner nor apply a claim.
    [Serializable] public sealed class OfficeRecoveryWitness
    {
        public int schema=1;
        public string worldId,npcId,hostId,departedId,sourceSnapshot;
        public ActorState initialNpc,preparedNpc,recoveredNpc,departedBefore,departedAfter,deadNpc,hostBeforeLoot,hostSaved,departedSaved;
        public ItemStack[] corpseItems;
        public string[] claims;
    }
    public static class OfficeRecoveryEvidence
    {
        public static string Handoff(ActorState before,ActorState after,ActorState departedBefore,ActorState departed,ActorState host)
        {
            if(before==null || after==null || departedBefore==null || departed==null || host==null) return "Нет полного before/after свидетельства передачи.";
            if(before.player || before.down || before.health<=0 || !before.attackBusy || string.IsNullOrEmpty(before.attackId) ||
                before.owner!=departedBefore.owner || before.attackTarget!=departedBefore.id)
                return "До отключения не подтверждены владелец и настоящая незавершённая атака на клиента.";
            if(!host.player || !host.connected || host.down || host.health<=0 || host.level!=before.level || string.IsNullOrEmpty(host.owner))
                return "Хост не остался активным участником того же уровня.";
            if(after.id!=before.id || after.actorType!=before.actorType || after.level!=before.level || after.owner!=host.owner ||
                after.generation!=before.generation+1 || after.lifecycleSequence!=before.lifecycleSequence || after.health!=before.health || after.down ||
                Vector2.Distance(after.Position,before.Position)>.001f)
                return "Изменены идентичность/жизнь/положение NPC или не получено единственное новое поколение владельца.";
            if(after.attackBusy || after.attackRemaining!=0 || !string.IsNullOrEmpty(after.attackId) || !string.IsNullOrEmpty(after.attackTarget) ||
                after.npcWeaponAction!=null || after.attackCooldown<=0)
                return "Незавершённая атака сохранилась либо восстановление не оставило паузу между атаками.";
            if(after.equippedWeaponItem!=before.equippedWeaponItem || StateJson.ToJson(after.inventory)!=StateJson.ToJson(before.inventory) ||
                StateJson.ToJson(after.inventoryUses)!=StateJson.ToJson(before.inventoryUses) ||
                StateJson.ToJson(after.inventoryAliases)!=StateJson.ToJson(before.inventoryAliases))
                return "Передача изменила предмет оружия, магазин, запас или журнал NPC.";
            if(departed.id!=departedBefore.id || departed.connected || departed.level!=before.level ||
                departed.owner!=host.owner || departed.generation!=departedBefore.generation+1 ||
                departed.controlStateVersion!=ActorSessionRules.ControlStateVersion || departed.health!=departedBefore.health ||
                departed.down!=departedBefore.down || departed.lifecycleSequence!=departedBefore.lifecycleSequence+1 ||
                Vector2.Distance(departed.Position,departedBefore.Position)>.001f ||
                StateJson.ToJson(departed.inventory)!=StateJson.ToJson(departedBefore.inventory))
                return "Отключённое тело потеряло исходную жизнь/инвентарь или не получило production lifecycle отключения.";
            return null;
        }
        public static string Loot(WorldState world,OfficeRecoveryWitness proof)
        {
            if(world==null || proof==null || proof.schema!=1 || world.worldId!=proof.worldId || proof.deadNpc==null ||
                proof.hostBeforeLoot==null || proof.departedAfter==null || proof.corpseItems==null || proof.corpseItems.Length==0 || proof.claims==null)
                return "Нет полного свидетельства мира/смерти/исходного инвентаря тела.";
            var npc=world.Find(proof.npcId);var host=world.Find(proof.hostId);var departed=world.Find(proof.departedId);
            if(npc==null || npc.player || !npc.down || npc.health>0 || npc.health!=proof.deadNpc.health || npc.actorType!=proof.deadNpc.actorType ||
                npc.level!=proof.deadNpc.level || npc.inventory.Count!=0 || Vector2.Distance(npc.Position,proof.deadNpc.Position)>.001f)
                return "Смерть, пустой инвентарь или стабильное тело NPC не сохранились.";
            var savedBody=proof.departedSaved??proof.departedAfter;
            if(host==null || departed==null || departed.connected || departed.level!=savedBody.level ||
                departed.health!=savedBody.health || departed.down!=savedBody.down)
                return "Сохранение потеряло игрока или состояние отключённого тела клиента.";
            if(world.pendingGrants.Any(g=>g.containerId==InventorySource.BodyId(proof.npcId)) || proof.claims.Length!=proof.corpseItems.Length ||
                proof.claims.Distinct(StringComparer.Ordinal).Count()!=proof.claims.Length || !proof.claims.All(world.completedTransactions.Contains))
                return "Резерв тела не завершён или журнал не содержит ровно по одной уникальной выдаче каждого предмета.";
            foreach(var item in proof.corpseItems)
            {
                var owners=world.actors.Where(actor=>InventoryStacks.Find(actor,item.id)!=null).ToArray();
                var received=InventoryStacks.Find(host,item.id);
                if(owners.Length!=1 || owners[0].id!=host.id || received==null || received.definition!=item.definition)
                    return "Исходный ID предмета потерян либо присутствует у нескольких получателей: "+item.id;
                if(item.weaponState!=null && (received.id!=item.id || received.count!=1 || StateJson.ToJson(received.weaponState)!=StateJson.ToJson(item.weaponState)))
                    return "Выданный экземпляр оружия/магазин не совпадает с фактическим предметом тела: "+item.id;
            }
            foreach(string definition in proof.corpseItems.Select(i=>i.definition).Distinct(StringComparer.Ordinal))
                if(InventoryJournal.Count(host,definition)!=InventoryJournal.Count(proof.hostBeforeLoot,definition)+
                    proof.corpseItems.Where(i=>i.definition==definition).Sum(i=>i.count))
                    return "Количество выдано повторно, потеряно или израсходовано после baseline: "+definition;
            return null;
        }
    }
}
