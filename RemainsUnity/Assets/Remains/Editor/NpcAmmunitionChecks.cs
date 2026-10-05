using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class NpcAmmunitionChecks
    {
        public static void Run()
        {
            var target=new ActorState {id="target",player=true,connected=true,x=4,owner="a"};
            var npc=Create();var item=WeaponInventory.Selected(npc);
            Require(NpcAttackRules.TryBegin(npc,target) && npc.npcWeaponAction.kind==NpcWeaponActions.Shot && item.weaponState.rounds==1,"Подготовка выстрела списала патрон");
            Require(NpcAttackRules.Complete(npc,target)==null && npc.attackBusy,"Выстрел завершился до таймера");
            var shot=NpcWeaponTestItems.Finish(npc,target);
            Require(shot?.weapon=="pistol" && item.weaponState.rounds==0 && item.weaponState.revision==1,"Не использован реальный пистолет или не списан патрон");
            Require(NpcAttackRules.Complete(npc,target)==null && item.weaponState.revision==1,"Повтор завершения повторил выстрел/расход");
            npc.attackCooldown=0;Require(NpcAttackRules.TryBegin(npc,target) && npc.npcWeaponAction.kind==NpcWeaponActions.Reload &&
                npc.attackRemaining==WeaponCatalog.Get("pistol").reloadSeconds && InventoryJournal.Count(npc,"p10")==2,"Нет отдельной перезарядки с реальным резервом");
            var world=new WorldState();var other=new ActorState {id="b",player=true,connected=true,owner="b",x=1};
            world.actors.AddRange(new[]{target,other,npc});npc.target=other.id;
            var ownership=new OwnershipService();ownership.Reassign(world,new[]{"a","b"});
            Require(npc.owner=="a" && npc.attackBusy,"Перезарядка передана другому владельцу посередине");
            var copied=StateJson.Copy(npc);Require(NpcWeaponActions.SameBinding(npc.npcWeaponAction,copied.npcWeaponAction) && copied.attackRemaining==npc.attackRemaining,"Сохранение потеряло привязку/таймер");
            Require(NpcWeaponTestItems.Finish(npc,target)==null && item.weaponState.rounds==2 && InventoryJournal.Count(npc,"p10")==0 &&
                npc.inventoryUses.Count==1 && item.weaponState.revision==2,"Перезарядка создала выстрел или не перенесла резерв");
            Require(NpcAttackRules.Complete(npc,target)==null && npc.inventoryUses.Count==1 && item.weaponState.rounds==2,"Повтор перезарядки расходует резерв");
            ownership.Reassign(world,new[]{"a","b"});Require(npc.owner=="b" && WeaponInventory.Selected(npc).id==item.id && item.weaponState.rounds==2,"Передача потеряла магазин/предмет");
            for(int i=0;i<2;i++) {npc.attackCooldown=0;Require(NpcAttackRules.TryBegin(npc,target),"Нет выстрела после перезарядки");Require(NpcWeaponTestItems.Finish(npc,target)!=null,"Нет реального выстрела");}
            npc.attackCooldown=0;Require(!NpcAttackRules.TryBegin(npc,target) && !npc.attackBusy && item.weaponState.rounds==0,"Пустой резерв стал бесконечным боезапасом");
            CheckCancellation(target);CheckMigration();CheckTiming(target);
            Debug.Log("NpcAmmunitionChecks PASS: предмет/выстрел/резерв/таймер, однократность, отмена, сохранение/передача, миграция без пополнения тел.");
        }
        private static ActorState Create(int rounds=1,int reserve=2)
        {var actor=new ActorState {id="ammo-npc",actorType="raider",anatomy="raider",owner="a"};NpcWeaponTestItems.Equip(actor,rounds:rounds,reserve:reserve);return actor;}
        private static void CheckCancellation(ActorState target)
        {
            foreach(bool reload in new[]{false,true})
            {
                var npc=Create(reload?0:1);var before=StateJson.ToJson(npc.inventory);
                NpcAttackRules.TryBegin(npc,target);NpcAttackRules.Cancel(npc);
                Require(NpcWeaponTestItems.Finish(npc,target)==null && StateJson.ToJson(npc.inventory)==before && npc.inventoryUses.Count==0,"Отмена расходует боезапас");
                npc.attackCooldown=0;NpcAttackRules.TryBegin(npc,target);npc.down=true;
                Require(NpcWeaponTestItems.Finish(npc,target)==null && !npc.attackBusy && StateJson.ToJson(npc.inventory)==before,"Нокаут расходует боезапас");
                npc.down=false;npc.attackCooldown=0;NpcAttackRules.TryBegin(npc,target);
                var world=new WorldState();world.actors.Add(npc);new OwnershipService().BeginSession(world);
                Require(!npc.attackBusy && npc.npcWeaponAction==null && StateJson.ToJson(npc.inventory)==before,"Перезапуск исполнил старое действие");
                npc.owner="a";npc.attackCooldown=0;NpcAttackRules.TryBegin(npc,target);
                var successor=new ActorState {id="successor",player=true,connected=true,owner="b",x=1};world.actors.Add(successor);
                new OwnershipService().Reassign(world,new[]{"b"});
                Require(npc.owner=="b" && !npc.attackBusy && npc.npcWeaponAction==null && StateJson.ToJson(npc.inventory)==before,
                    "Обрыв владельца исполнил выстрел/перезарядку вместо отмены");
                npc.attackCooldown=0;NpcAttackRules.TryBegin(npc,target);WeaponInventory.Select(npc,null,out _);
                Require(NpcWeaponTestItems.Finish(npc,target)==null && npc.inventoryUses.Count==0 && StateJson.ToJson(npc.inventory)==before,"Потеря выбранного предмета не отменяет действие");
            }
            var donor=Create(0);NpcAttackRules.TryBegin(donor,target);var incoming=donor.Copy();
            incoming.npcWeaponAction.item="other-item";incoming.attackRemaining=.1f;
            float remaining=donor.attackRemaining;NpcAttackRules.CopyProgress(incoming,donor);
            Require(donor.attackRemaining==remaining,"Поздний прогресс другого предмета изменил действие");
            var wall=new RoomLayout {platforms=new[]{new RoomSurface {x=2,y=0,width=.2f,height=3}}};
            var blocked=Create();NpcAttackRules.TryBegin(blocked,target);
            Require(NpcWeaponTestItems.Finish(blocked,target,wall)==null && WeaponInventory.Selected(blocked).weaponState.rounds==1,"Стена не остановила выстрел до расхода");
        }
        private sealed class CustomTiming : INpcWeaponTimingRule
        {
            public float Preparation(ActorState actor,WeaponDefinition weapon)=>.2f;
            public float Recovery(ActorState actor,WeaponDefinition weapon)=>.4f;
        }
        private static void CheckTiming(ActorState target)
        {
            var previous=GameplayRuleSet.CreateDefault();var timing=new CustomTiming();
            var rules=new GameplayRuleSet(previous.Damage,previous.Knockdown,previous.PartyDefeat,previous.Loot,previous.Pause,
                previous.Deposit,previous.Telekinesis,previous.ProximityOwnership,previous.DoorAccess,previous.ItemActions,timing);
            var npc=Create();Require(ReferenceEquals(rules.NpcWeaponTiming,timing) && NpcAttackRules.TryBegin(npc,target,rules.NpcWeaponTiming) &&
                npc.attackRemaining==.2f,"Композиция правил потеряла заменяемое время NPC");
            npc.attackRemaining=0;Require(NpcAttackRules.Complete(npc,target,null,rules.NpcWeaponTiming)!=null && npc.attackCooldown==.4f,
                "Завершение NPC не использует выбранное правило времени");
        }
        private static void CheckMigration()
        {
            var world=new WorldState {worldId="npc-magazine-migration"};
            var known=new ActorState {id="known",actorType="raider",owner="a",health=27,x=3};
            known.magazines.Add(new WeaponMagazine {weapon="pistol",rounds=3,revision=7});known.inventory.Add(SharedInventory.Create("caps",19));
            var unknown=new ActorState {id="unknown",actorType="raider"};
            var body=new ActorState {id="looted-body",actorType="raider",down=true,health=0};
            world.actors.AddRange(new[]{known,unknown,body});int backups=0;
            Require(NpcWeaponInventoryMigration.Apply(world,()=>backups++) && backups==1,"Нет единственного сохранения источника");
            known=world.Find(known.id);unknown=world.Find(unknown.id);body=world.Find(body.id);
            Require(known.health==27 && known.x==3 && known.owner=="a" && known.inventory.Count==2 && InventoryJournal.Count(known,"caps")==19 &&
                WeaponInventory.Selected(known).weaponState.rounds==3 && WeaponInventory.Selected(known).weaponState.revision==7 &&
                WeaponInventory.Selected(known).id==NpcWeaponInventoryMigration.ItemId(world.worldId,known.id,"pistol"),"Старое состояние не сохранено");
            Require(WeaponInventory.Selected(unknown).weaponState.rounds==WeaponCatalog.Get("pistol").magazineSize && InventoryJournal.Count(unknown,"p10")==0,"Явное первоначальное состояние дало новый резерв");
            Require(body.inventory.Count==0 && body.weapon==null && body.weaponInventoryVersion==1,"Разграбленное тело пополнено");
            string saved=StateJson.ToJson(world);Require(!NpcWeaponInventoryMigration.Apply(world) && StateJson.ToJson(world)==saved,"Повторная загрузка пополнила магазин/инвентарь");
            var bad=new WorldState();bad.actors.Add(new ActorState {id="valid-first",actorType="raider"});bad.actors.Add(new ActorState {id="bad",actorType="raider",weaponInventoryVersion=9});
            string original=StateJson.ToJson(bad);bool rejected=false;
            try {NpcWeaponInventoryMigration.Apply(bad,()=>backups++);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && StateJson.ToJson(bad)==original && backups==1,"Ошибка частично преобразовала мир или записала его источник");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("NpcAmmunitionChecks: "+reason);}
    }
}
