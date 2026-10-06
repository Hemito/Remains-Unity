using System;
using System.Linq;
using Remains.Domain;
using Remains.Runtime;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class OfficeRecoveryEvidenceChecks
    {
        public static void Run()=>Run(true);
        public static void Run(bool log)
        {
            int count=0;
            void Check(bool valid,string reason) {count++;if(!valid) throw new InvalidOperationException("Office recovery evidence: "+reason);}
            var source=new ActorState {id="client",player=true,connected=true,owner="connection:2",level="factory:0",x=103.8f,y=11.2f,generation=4,lifecycleSequence=8};
            source.inventory.Add(new ItemStack {id="starter-heal",definition="heal",count=4});
            source.inventoryAliases["absorbed-heal"]="starter-heal";
            source.inventoryUses.Add(new InventoryUse {id="past-heal",debits=new System.Collections.Generic.List<ItemDebit> {new() {item="absorbed-heal",count=1}}});
            source.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken> {{"mod:body",new Newtonsoft.Json.Linq.JObject {{"kept",1}}}};
            var host=new ActorState {id="host",player=true,connected=true,owner="connection:1",level="factory:0",x=106,y=11.2f};
            var before=new ActorState {id="npc",actorType="raider",level="factory:0",owner=source.owner,target=source.id,generation=4,health=140,x=104.8f,y=11.2f,
                attackBusy=true,attackId="prepared-exact",attackTarget=source.id,attackRemaining=.4f,equippedWeaponItem="npc-pistol",
                npcWeaponAction=new NpcWeaponActionState {kind="shot",item="npc-pistol",weapon="pistol"}};
            before.inventory.Add(new ItemStack {id="npc-pistol",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=5,revision=1}});
            before.inventory.Add(new ItemStack {id="npc-caps",definition="caps",count=6});
            // Regression from real .034s canonical preparation: the owner has
            // already fired before the two paused witnesses, so no cancellation exists.
            var gate=new OfficeRecoveryPreparationGate();gate.Begin(before,host.id,source.id);int firstAttempt=gate.Attempt;
            var consumed=before.Copy();consumed.attackBusy=false;consumed.attackRemaining=0;consumed.attackId=null;
            consumed.npcWeaponAction=null;consumed.inventory[0].weaponState.rounds--;
            var outcome=gate.Observe(firstAttempt,source.id,consumed,true,true,out string preparationError);
            Check(outcome==OfficeRecoveryPreparationOutcome.Retry && preparationError!=null && !gate.Forbids(before.attackId),
                "Владелец уже выстрелил до barrier: отрицательное наблюдение, не ложный cancelled-shot FAIL.");
            Check(gate.Confirm(before,true,false)!=null && gate.ConfirmedAttackId==null,"Отклонённый кандидат не разрешает departure.");
            outcome=gate.Observe(firstAttempt,host.id,before.Copy(),true,false,out preparationError);
            Check(outcome==OfficeRecoveryPreparationOutcome.Waiting,"Canonical-like host witness недостаточен для consensus.");
            Check(gate.Observe(firstAttempt,host.id,before.Copy(),true,false,out _)==OfficeRecoveryPreparationOutcome.Waiting && gate.Observers==1,
                "Повтор одного peer witness не заменяет второго участника.");
            var wrongAmmo=before.Copy();wrongAmmo.inventory[0].weaponState.rounds--;
            Check(gate.Observe(firstAttempt,source.id,wrongAmmo,true,false,out _) == OfficeRecoveryPreparationOutcome.Retry && gate.Observers==1,
                "Exact ammo mismatch никогда не считается положительным witness даже при busy/ID match.");
            Check(gate.Observe(firstAttempt,source.id,before.Copy(),false,false,out _)==OfficeRecoveryPreparationOutcome.Retry,
                "Неподтверждённая pause не разрешает departure.");
            gate.Reset();var next=before.Copy();next.attackId="next-real-preparation";gate.Begin(next,host.id,source.id);
            Check(gate.Attempt==firstAttempt+1 && gate.Observers==0,"Retry сбрасывает witnesses и меняет номер настоящего кандидата.");
            Check(gate.Observe(firstAttempt,source.id,before.Copy(),true,false,out _)==OfficeRecoveryPreparationOutcome.Stale && gate.Observers==0,
                "Запоздалый witness прежней атаки не завершает новый barrier.");
            Check(gate.Observe(gate.Attempt,host.id,next.Copy(),true,false,out _)==OfficeRecoveryPreparationOutcome.Waiting,
                "Новый candidate требует нового host witness.");
            Check(gate.Confirm(next,true,false)!=null,"Даже актуальный canonical snapshot не заменяет второй peer.");
            Check(gate.Observe(gate.Attempt,source.id,next.Copy(),true,false,out _)==OfficeRecoveryPreparationOutcome.Ready,
                "Две разные paused replicas с exact weapon state готовы к final recheck.");
            Check(gate.Confirm(next,true,true)!=null && !gate.Forbids(next.attackId),
                "Выстрел, обнаруженный canonical recheck до consensus, требует retry без ложного запрета ID.");
            var staleCanonical=next.Copy();staleCanonical.inventoryUses.Add(new InventoryUse {id="real-shot-use"});
            Check(gate.Confirm(staleCanonical,true,false)!=null && gate.ConfirmedAttackId==null,
                "Final canonical recheck сохраняет exact journal witness.");
            Check(gate.Confirm(next,false,false)!=null,"Снятие pause между witness и final recheck не разрешает departure.");
            Check(gate.Confirm(next,true,false)==null && gate.Forbids(next.attackId),
                "Только consensus + final recheck запрещают exact attackID, включая позднюю доставку выстрела.");
            Check(!gate.Forbids(before.attackId) && !gate.Forbids("new-owner-shot"),
                "Реальный выстрел отвергнутой попытки и новая атака владельца не называются отменёнными.");
            bool resetRejected=false;try {gate.Reset();} catch(InvalidOperationException) {resetRejected=true;}
            Check(resetRejected && gate.Forbids(next.attackId),"После consensus нельзя спрятать запрещённый выстрел сбросом кандидата.");
            foreach(var mutate in new Action<ActorState>[] {a=>a.owner="other-owner",a=>a.generation++,a=>a.lifecycleSequence++,a=>a.level="hub",
                a=>a.attackTarget="other-target",a=>a.equippedWeaponItem="other-pistol",a=>a.inventoryAliases["new-alias"]="npc-pistol",
                a=>a.npcWeaponAction.selectionRevision++,a=>a.attackRemaining=0})
            {var invalid=next.Copy();mutate(invalid);Check(OfficeRecoveryEvidence.Preparation(next,invalid,true,false)!=null,
                "Различная identity/owner/generation/life/level/target/weapon/aliases/action/completed preparation не проходит witness.");}
            var after=before.Copy();after.owner=host.owner;after.generation=5;after.attackBusy=false;after.attackId=null;after.attackTarget=null;
            after.attackRemaining=0;after.attackCooldown=2;after.npcWeaponAction=null;
            var disconnected=source.Copy();disconnected.connected=false;disconnected.controlStateVersion=1;disconnected.lifecycleSequence=9;disconnected.owner=host.owner;disconnected.generation=5;
            bool Accept(ActorState next,ActorState departed=null,ActorState active=null)=>OfficeRecoveryEvidence.Handoff(before,next,source,departed??disconnected,active??host)==null;
            Check(Accept(after),"Безопасная передача без новой смерти/расхода должна пройти.");
            foreach(var mutate in new Action<ActorState>[] {a=>a.generation=4,a=>a.owner="connection:2",a=>a.attackId="prepared-exact",a=>a.attackBusy=true,a=>a.attackCooldown=0,
                a=>a.inventory[0].weaponState.rounds--,a=>a.inventory[1].count++,a=>a.health--,a=>a.x+=.1f})
            {var invalid=after.Copy();mutate(invalid);Check(!Accept(invalid),"Несогласованное after свидетельство должно отклоняться.");}
            var bad=disconnected.Copy();bad.down=true;Check(!Accept(after,bad),"Disconnect не означает новую gameplay смерть.");
            bad=host.Copy();bad.connected=false;Check(!Accept(after,null,bad),"Хост обязан оставаться активным в этом уровне.");
            bad=host.Copy();bad.level="hub";Check(!Accept(after,null,bad),"Хост в хабе не доказывает office handoff.");
            var world=new WorldState {worldId="recovery-evidence"};var dead=after.Copy();dead.down=true;dead.health=-4;dead.inventory.Clear();
            var grantedHost=host.Copy();grantedHost.inventory.Add(new ItemStack {id="own-caps",definition="caps",count=16});
            grantedHost.inventoryAliases["npc-caps"]="own-caps";grantedHost.inventory.Add(before.inventory[0].Copy());
            var baseline=host.Copy();baseline.inventory.Add(new ItemStack {id="own-caps",definition="caps",count=10});
            world.actors.AddRange(new[]{dead,grantedHost,disconnected.Copy()});world.completedTransactions.AddRange(new[]{"claim:pistol","claim:caps"});
            var proof=new OfficeRecoveryWitness {worldId=world.worldId,npcId=dead.id,hostId=host.id,departedId=source.id,departedAfter=disconnected.Copy(),departedSaved=disconnected.Copy(),deadNpc=dead.Copy(),
                hostBeforeLoot=baseline,corpseItems=before.inventory.Select(i=>i.Copy()).ToArray(),claims=world.completedTransactions.ToArray()};
            bool Loot(WorldState next)=>OfficeRecoveryEvidence.Loot(next,proof)==null;
            Check(Loot(world),"Merged aliases и ровно одна выдача полного тела должны пройти.");
            foreach(var mutate in new Action<WorldState>[] {w=>w.Find("npc").down=false,w=>w.Find("npc").inventory.Add(before.inventory[0].Copy()),
                w=>w.Find("host").inventory[0].count++,w=>w.Find("host").inventoryAliases.Clear(),w=>w.Find("host").inventory[1].weaponState.rounds++,
                w=>w.completedTransactions.RemoveAt(0),w=>w.pendingGrants.Add(new PendingGrant {containerId="body:npc"}),
                w=>w.Find("client").inventory.Add(before.inventory[0].Copy()),w=>w.worldId="another-world"})
            {var invalid=world.Copy();mutate(invalid);Check(!Loot(invalid),"Потеря/дубль/пополнение/другой мир должны отклоняться.");}
            foreach(var mutate in new Action<WorldState>[] {w=>w.Find("client").inventory.Clear(),w=>w.Find("client").inventoryAliases.Clear(),
                w=>w.Find("client").inventoryUses.Clear(),w=>w.Find("client").x+=.05f,w=>w.Find("client").y+=.05f,
                w=>w.Find("client").lifecycleSequence++,w=>w.Find("client").controlStateVersion=0,w=>w.Find("client").damageSequence++,
                w=>w.Find("client").extensions.Clear()})
            {var invalid=world.Copy();mutate(invalid);Check(!Loot(invalid),"Независимый body witness обязан отвергать потерю inventory/aliases/uses/position/lifecycle/control/damage/unknown.");}
            var rebound=world.Copy();rebound.Find("client").owner="connection:new-session";rebound.Find("client").generation+=2;
            Check(Loot(rebound),"Owner/gen rebind не должен скрывать обязательность неизменных core body fields.");
            var serialized=StateJson.FromJson<WorldState>(StateJson.ToJson(world));Check(Loot(serialized),"Внешняя сериализация не должна терять IDs/алиасы/магазин/журнал.");
            var hurt=world.Copy();hurt.Find("client").health=73;proof.departedSaved=hurt.Find("client").Copy();
            Check(Loot(hurt),"Поздний фактический offline damage сохраняется без восстановленного HP.");
            var restored=StateJson.FromJson<WorldState>(StateJson.ToJson(hurt));Check(Loot(restored),"Независимый canonical offline-damage expected переживает сериализацию диска.");
            restored.Find("client").health=100;Check(!Loot(restored),"Restart не должен отбрасывать сохранённый offline damage.");
            Check(proof.departedSaved.health==73,"Проверяемая disk-копия не имеет права менять независимый live expected.");
            if(log) Debug.Log($"Office recovery evidence: PASS {count} — preconsensus retry/nonce/две paused replicas/exact ammo/final recheck/strict confirmed ID, самостоятельные before/after, отказ stale owner/gen/attack/ammo/death, активный хост, unique loot/aliases/magazine/journal и сериализация. Это checks доказательств; реальные процессы/контент/UI проверяет отдельный probe.");
        }
    }
}
