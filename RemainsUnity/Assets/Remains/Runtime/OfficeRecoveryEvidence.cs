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
        public int preparationAttempt;
        public string worldId,npcId,hostId,departedId,sourceSnapshot,confirmedAttackId;
        public ActorState initialNpc,preparedNpc,recoveredNpc,departedBefore,departedAfter,deadNpc,hostBeforeLoot,hostSaved,departedSaved;
        public ItemStack[] corpseItems;
        public string[] claims;
    }
    public enum OfficeRecoveryPreparationOutcome {Stale,Retry,Waiting,Ready}

    // Probe-only consensus. A sampled candidate is not yet a cancelled attack.
    public sealed class OfficeRecoveryPreparationGate
    {
        private readonly System.Collections.Generic.HashSet<string> observers=new(StringComparer.Ordinal);
        private string first,second;
        public int Attempt {get;private set;}
        public ActorState Candidate {get;private set;}
        public string ConfirmedAttackId {get;private set;}
        public int Observers=>observers.Count;
        public void Begin(ActorState npc,string host,string departing)
        {
            if(ConfirmedAttackId!=null) throw new InvalidOperationException("Нельзя заменить уже согласованную подготовку recovery.");
            string error=OfficeRecoveryEvidence.Preparation(npc,npc,true,false);
            if(error!=null || string.IsNullOrEmpty(host) || string.IsNullOrEmpty(departing) || host==departing)
                throw new InvalidOperationException(error??"Нет двух разных участников barrier recovery.");
            Candidate=npc.Copy();first=host;second=departing;Attempt++;observers.Clear();
        }
        public OfficeRecoveryPreparationOutcome Observe(int attempt,string actor,ActorState npc,bool paused,bool shotObserved,out string reason)
        {
            reason=null;
            if(attempt!=Attempt || Candidate==null) return OfficeRecoveryPreparationOutcome.Stale;
            if(actor!=first && actor!=second) reason="Свидетель не является участником recovery barrier.";
            else reason=OfficeRecoveryEvidence.Preparation(Candidate,npc,paused,shotObserved);
            if(reason!=null) return OfficeRecoveryPreparationOutcome.Retry;
            observers.Add(actor);
            return observers.Count==2?OfficeRecoveryPreparationOutcome.Ready:OfficeRecoveryPreparationOutcome.Waiting;
        }
        public string Confirm(ActorState canonical,bool paused,bool shotObserved)
        {
            if(Candidate==null || observers.Count!=2) return "Нет двух независимых положительных paused witness.";
            string error=OfficeRecoveryEvidence.Preparation(Candidate,canonical,paused,shotObserved);
            if(error!=null) return error;
            ConfirmedAttackId=Candidate.attackId;return null;
        }
        public void Reset()
        {
            if(ConfirmedAttackId!=null) throw new InvalidOperationException("Нельзя сбросить согласованную подготовку recovery.");
            Candidate=null;observers.Clear();
        }
        public bool Forbids(string attack)=>ConfirmedAttackId!=null && attack==ConfirmedAttackId;
    }
    public static class OfficeRecoveryEvidence
    {
        public static string Preparation(ActorState candidate,ActorState observed,bool paused,bool shotObserved)
        {
            if(candidate==null || candidate.player || candidate.down || candidate.health<=0 || !candidate.attackBusy ||
                string.IsNullOrEmpty(candidate.attackId) || string.IsNullOrEmpty(candidate.attackTarget) ||
                candidate.attackRemaining<=0 || candidate.npcWeaponAction?.kind!=NpcWeaponActions.Shot)
                return "Кандидат не содержит настоящей незавершённой shot preparation.";
            if(shotObserved) return "Выстрел кандидата уже наблюдался до consensus.";
            if(!paused) return "Участник ещё не подтвердил pause уровня.";
            if(observed==null || observed.id!=candidate.id || observed.owner!=candidate.owner || observed.generation!=candidate.generation ||
                observed.lifecycleSequence!=candidate.lifecycleSequence || observed.level!=candidate.level || observed.down || observed.health<=0 ||
                !observed.attackBusy || observed.attackId!=candidate.attackId || observed.attackTarget!=candidate.attackTarget ||
                observed.attackRemaining<=0 || observed.npcWeaponAction?.kind!=NpcWeaponActions.Shot)
                return "Независимая реплика уже завершила/заменила подготовку либо имеет другую identity/owner/generation/lifecycle.";
            if(observed.equippedWeaponItem!=candidate.equippedWeaponItem || StateJson.ToJson(observed.npcWeaponAction)!=StateJson.ToJson(candidate.npcWeaponAction) ||
                StateJson.ToJson(observed.inventory)!=StateJson.ToJson(candidate.inventory) ||
                StateJson.ToJson(observed.inventoryUses)!=StateJson.ToJson(candidate.inventoryUses) ||
                StateJson.ToJson(observed.inventoryAliases)!=StateJson.ToJson(candidate.inventoryAliases))
                return "Подготовка replica/canonical разошлась по exact оружию/магазину/инвентарю/журналу NPC.";
            return null;
        }
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
        // Session authority owner/generation may rebind on shutdown/reload. Core body
        // state must match an independently captured canonical live witness exactly;
        // pose clocks/replication counters are not assertions about persisted gameplay.
        public static string Body(ActorState expected,ActorState actual)
        {
            if(expected==null || actual==null || expected.connected || actual.connected ||
                actual.id!=expected.id || actual.character!=expected.character || actual.player!=expected.player ||
                actual.actorType!=expected.actorType || actual.anatomy!=expected.anatomy || actual.level!=expected.level)
                return "Сохранение потеряло идентичность/уровень отключённого тела.";
            if(actual.health!=expected.health || actual.mana!=expected.mana || actual.down!=expected.down ||
                actual.lifecycleSequence!=expected.lifecycleSequence || actual.controlStateVersion!=expected.controlStateVersion ||
                actual.damageSequence!=expected.damageSequence || actual.x!=expected.x || actual.y!=expected.y ||
                actual.vx!=expected.vx || actual.vy!=expected.vy || actual.climbing!=expected.climbing || actual.crouched!=expected.crouched)
                return "Сохранение изменило жизнь/control/lifecycle/damage/позицию отключённого тела.";
            if(actual.loadoutVersion!=expected.loadoutVersion || actual.weaponInventoryVersion!=expected.weaponInventoryVersion ||
                actual.equippedWeaponItem!=expected.equippedWeaponItem || actual.heldItemId!=expected.heldItemId ||
                StateJson.ToJson(actual.inventory)!=StateJson.ToJson(expected.inventory) ||
                StateJson.ToJson(actual.inventoryAliases)!=StateJson.ToJson(expected.inventoryAliases) ||
                StateJson.ToJson(actual.inventoryUses)!=StateJson.ToJson(expected.inventoryUses) ||
                StateJson.ToJson(actual.weaponShortcuts)!=StateJson.ToJson(expected.weaponShortcuts))
                return "Сохранение изменило exact предметы/алиасы/журнал/назначения отключённого тела.";
            if(StateJson.ToJson(actual.injuries)!=StateJson.ToJson(expected.injuries) ||
                StateJson.ToJson(actual.resolvedAttacks)!=StateJson.ToJson(expected.resolvedAttacks) ||
                StateJson.ToJson(actual.resolvedExplosions)!=StateJson.ToJson(expected.resolvedExplosions) ||
                StateJson.ToJson(actual.appearance)!=StateJson.ToJson(expected.appearance) ||
                StateJson.ToJson(actual.extensions)!=StateJson.ToJson(expected.extensions))
                return "Сохранение изменило ранения/receipts/внешность/unknown данные отключённого тела.";
            return null;
        }
        public static string Loot(WorldState world,OfficeRecoveryWitness proof)
        {
            if(world==null || proof==null || proof.schema!=1 || world.worldId!=proof.worldId || proof.deadNpc==null ||
                proof.hostBeforeLoot==null || proof.departedAfter==null || proof.departedSaved==null || proof.corpseItems==null || proof.corpseItems.Length==0 || proof.claims==null)
                return "Нет полного свидетельства мира/смерти/исходного инвентаря тела.";
            var npc=world.Find(proof.npcId);var host=world.Find(proof.hostId);var departed=world.Find(proof.departedId);
            if(npc==null || npc.player || !npc.down || npc.health>0 || npc.health!=proof.deadNpc.health || npc.actorType!=proof.deadNpc.actorType ||
                npc.level!=proof.deadNpc.level || npc.inventory.Count!=0 || Vector2.Distance(npc.Position,proof.deadNpc.Position)>.001f)
                return "Смерть, пустой инвентарь или стабильное тело NPC не сохранились.";
            if(host==null) return "Сохранение потеряло получателя лута.";
            string bodyError=Body(proof.departedSaved,departed);
            if(bodyError!=null) return bodyError;
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
