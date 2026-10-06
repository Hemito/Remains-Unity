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
            var host=new ActorState {id="host",player=true,connected=true,owner="connection:1",level="factory:0",x=106,y=11.2f};
            var before=new ActorState {id="npc",actorType="raider",level="factory:0",owner=source.owner,target=source.id,generation=4,health=140,x=104.8f,y=11.2f,
                attackBusy=true,attackId="prepared-exact",attackTarget=source.id,attackRemaining=.4f,equippedWeaponItem="npc-pistol",
                npcWeaponAction=new NpcWeaponActionState {kind="shot",item="npc-pistol",weapon="pistol"}};
            before.inventory.Add(new ItemStack {id="npc-pistol",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=5,revision=1}});
            before.inventory.Add(new ItemStack {id="npc-caps",definition="caps",count=6});
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
            var proof=new OfficeRecoveryWitness {worldId=world.worldId,npcId=dead.id,hostId=host.id,departedId=source.id,departedAfter=disconnected.Copy(),deadNpc=dead.Copy(),
                hostBeforeLoot=baseline,corpseItems=before.inventory.Select(i=>i.Copy()).ToArray(),claims=world.completedTransactions.ToArray()};
            bool Loot(WorldState next)=>OfficeRecoveryEvidence.Loot(next,proof)==null;
            Check(Loot(world),"Merged aliases и ровно одна выдача полного тела должны пройти.");
            foreach(var mutate in new Action<WorldState>[] {w=>w.Find("npc").down=false,w=>w.Find("npc").inventory.Add(before.inventory[0].Copy()),
                w=>w.Find("host").inventory[0].count++,w=>w.Find("host").inventoryAliases.Clear(),w=>w.Find("host").inventory[1].weaponState.rounds++,
                w=>w.completedTransactions.RemoveAt(0),w=>w.pendingGrants.Add(new PendingGrant {containerId="body:npc"}),
                w=>w.Find("client").inventory.Add(before.inventory[0].Copy()),w=>w.worldId="another-world"})
            {var invalid=world.Copy();mutate(invalid);Check(!Loot(invalid),"Потеря/дубль/пополнение/другой мир должны отклоняться.");}
            var serialized=StateJson.FromJson<WorldState>(StateJson.ToJson(world));Check(Loot(serialized),"Внешняя сериализация не должна терять IDs/алиасы/магазин/журнал.");
            var hurt=world.Copy();hurt.Find("client").health=73;proof.departedSaved=hurt.Find("client").Copy();
            Check(Loot(hurt),"Поздний фактический offline damage сохраняется без восстановленного HP.");
            hurt.Find("client").health=100;Check(!Loot(hurt),"Restart не должен отбрасывать сохранённый offline damage.");
            if(log) Debug.Log($"Office recovery evidence: PASS {count} — самостоятельные before/after, отказ stale owner/gen/attack/ammo/death, активный хост, unique loot/aliases/magazine/journal и сериализация. Это checks доказательств; реальные процессы/контент/UI проверяет отдельный probe.");
        }
    }
}
