using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class CosmeticEquipmentChecks
    {
        private const string First="check:outfit-a",Second="check:outfit-b",Slot="check:body";
        private const string Pack="{\"schema\":1,\"slots\":[\"check:body\"],\"items\":[{\"id\":\"check:outfit-a\",\"slot\":\"check:body\",\"visual\":\"check:visual-a\",\"anatomy\":\"pony\"},{\"id\":\"check:outfit-b\",\"slot\":\"check:body\",\"visual\":\"check:visual-b\",\"anatomy\":\"pony\"}]}";
        public static void Run()
        {
            CheckCatalog();CheckEquipAndStacks();CheckTransfers();CheckMergeAndOwnerSelection();CheckUnknownAndMalformed();CheckEntry();CheckPersistence();
            Debug.Log("CosmeticEquipmentChecks PASS: обычные объединяемые стопки без расхода при надевании, alias/часть/последняя единица, shared reserve/отмена/повтор, wardrobe-only при поздней выдаче, lifecycle, detached unknown/future JSON, профиль/мир/восстановительная пара. Защита и физика не изменяются.");
        }
        private static CosmeticCatalog Catalog(Action<string> warning=null)=>new(Pack,warning??(_=>{}));
        private static ActorState Actor(string id="cosmetic-check",int count=3)=>new()
        {
            id="player:"+id,character=id,owner="owner:a",player=true,connected=true,generation=2,lifecycleSequence=3,
            level="hub",revision=10,health=73,mana=61,x=17,y=10.4f,vx=-2,vy=1,pose="climb",poseTime=-.25f,climbing=true,
            weapon="pistol",equippedWeaponItem="pistol-check",weaponSelectionRevision=4,heldItemId="held-check",
            loadoutVersion=1,weaponInventoryVersion=1,inventory={new ItemStack {id="outfit-stack",definition=First,count=count}}
        };
        private static CosmeticSelection Selection(ActorState actor)=>actor.cosmeticEquipment.entries.Single();
        private static void Equip(ActorState actor,CosmeticCatalog catalog,string id="outfit-stack")=>
            Require(CosmeticEquipment.TryEquip(actor,id,false,out string reason,catalog),"Надевание отклонено: "+reason);
        private static string WithoutEquipment(ActorState actor)
        {
            var copy=actor.Copy();copy.cosmeticEquipment=null;copy.revision=0;return StateJson.ToJson(copy);
        }
        private static void CheckCatalog()
        {
            var catalog=Catalog();catalog.RegisterSlot("check:accessory");
            catalog.RegisterPack("{\"schema\":1,\"slots\":[],\"items\":[{\"id\":\"check:extra\",\"slot\":\"check:accessory\",\"visual\":\"check:extra-art\",\"anatomy\":\"pony\"}]}","пакет проверки");
            catalog.Register(new CosmeticDefinition(First,Slot,"check:replacement","pony"),true);
            Reject(()=>catalog.RegisterPack("{\"schema\":1,\"slots\":[\"check:should-not-leak\"],\"items\":[{\"id\":\"check:bad\",\"slot\":\"missing\",\"visual\":\"art\",\"anatomy\":\"pony\"}]}","ошибочный пакет"),"source=ошибочный пакет");
            catalog.RegisterSlot("check:should-not-leak");
            Reject(()=>catalog.RegisterPack(Pack,"дубликат"),"source=дубликат");
            Require(catalog.Get(First).Visual=="check:replacement" && catalog.Slots.Count==3 && catalog.Definitions.Count==3 &&
                catalog.Fingerprint.Length==64,"Регистрация/атомарный отказ/порядок каталога");
            Reject(()=>catalog.RegisterSlot("late"),"уже используется");
            Reject(()=>((IList<string>)catalog.Slots).Add("mutable"),null,typeof(NotSupportedException));
            foreach(string json in new[] {"{\"schema\":2,\"slots\":[],\"items\":[]}",
                "{\"schema\":1,\"slots\":[\"x\",\"x\"],\"items\":[]}",
                "{\"schema\":1,\"slots\":[\"x\"],\"items\":[{\"id\":\"a\",\"slot\":\"x\",\"visual\":\" \" ,\"anatomy\":\"pony\"}]}",
                Pack.Replace("check:outfit-b","check:outfit-a")}) Reject(()=>new CosmeticCatalog(json),"визуального снаряжения");
            int warnings=0;var missing=Catalog(message=>{warnings++;Require(message.Contains("actor=test"),"Предупреждение без контекста");});
            missing.WarnUnavailable("missing","actor=test");missing.WarnUnavailable("missing","actor=test");
            Require(warnings==1,"Повторное предупреждение отсутствующего определения");
            Require(Catalog().Fingerprint==Catalog().Fingerprint,"Fingerprint зависит от экземпляра каталога");
        }
        private static void CheckEquipAndStacks()
        {
            var catalog=Catalog();var actor=Actor();string untouched=WithoutEquipment(actor),inventory=StateJson.ToJson(actor.inventory);
            Equip(actor,catalog);Require(Selection(actor).item=="outfit-stack" && CosmeticEquipment.IsEquipped(actor,"outfit-stack") &&
                StateJson.ToJson(actor.inventory)==inventory && actor.inventoryUses.Count==0 && WithoutEquipment(actor)==untouched && actor.revision==11,
                "Надевание расходует вещь или меняет здоровье/движение/оружие");
            Require(CosmeticEquipment.TryResolve(actor,Selection(actor),out var definition,out _,catalog) && definition.Id==First,
                "Общий resolver не видит живой комплект");
            string equipped=StateJson.ToJson(actor);Equip(actor,catalog);Require(StateJson.ToJson(actor)==equipped,"Повторное надевание расходует revision");
            Selection(actor).extensions=Extension();
            Require(CosmeticEquipment.TryEquip(actor,"outfit-stack",true,out _,catalog) && Selection(actor).item==null &&
                Selection(actor).definition==First && Selection(actor).extensions.ContainsKey("mod:cosmetic"),"Снятие потеряло неизвестные поля слота");
            Equip(actor,catalog);actor.inventoryReservations["outfit-stack"]=3;equipped=StateJson.ToJson(actor);
            Require(!CosmeticEquipment.TryEquip(actor,"outfit-stack",true,out string reason,catalog) && reason.Contains("зарезервирован") &&
                StateJson.ToJson(actor)==equipped,"Зарезервированное снаряжение изменено");actor.inventoryReservations.Clear();
            actor.inventory.Add(new ItemStack {id="absorbed",definition=First,count=2});
            Require(CosmeticEquipment.TryEquip(actor,"absorbed",false,out _,catalog),"Выбор отдельной старой стопки");
            Require(InventoryStacks.Consolidate(actor.inventory,actor.inventoryAliases)==1 && actor.inventory.Single().count==5 &&
                CosmeticEquipment.ReconcileInventory(actor) && Selection(actor).item=="outfit-stack" && CosmeticEquipment.IsEquipped(actor,"absorbed"),
                "Объединение одинаковой одежды не сохранило canonical selection/alias");
            Require(InventoryJournal.Debit(actor,"partial","absorbed",4) && actor.inventory.Single().count==1 && CosmeticEquipment.IsEquipped(actor,"outfit-stack"),
                "Частичный расход снял комплект");
            Require(InventoryJournal.Debit(actor,"last","outfit-stack",1) && Selection(actor).item==null && actor.inventory.Count==0,
                "Последняя единица оставила фантом одежды");equipped=StateJson.ToJson(actor);
            Require(!InventoryJournal.Debit(actor,"last","outfit-stack",1) && StateJson.ToJson(actor)==equipped,"Повтор расхода изменил одежду/журнал");
            InventoryStacks.Add(actor,new ItemStack {id="outfit-stack",definition=First,count=1});
            CosmeticEquipment.ReconcileInventory(actor);Require(!CosmeticEquipment.IsEquipped(actor,"outfit-stack"),"Возврат вещи автоматически надел снятый комплект");
            var consumed=Actor();Equip(consumed,catalog);
            Require(InventoryJournal.Consume(consumed,First,2) && CosmeticEquipment.IsEquipped(consumed,"outfit-stack") &&
                SharedInventory.Consume(consumed,First,"outfit-stack") && Selection(consumed).item==null,"Consume не очистил последнюю ссылку");
        }
        private static void CheckTransfers()
        {
            var catalog=Catalog();var world=new WorldState();var actor=Actor();actor.x=actor.y=0;actor.climbing=false;Equip(actor,catalog);world.actors.Add(actor);
            var box=new ContainerState {id="cosmetic-box",level="hub",x=0,y=0};world.containers.Add(box);
            var deposits=new SharedDeposits(world);
            Require(deposits.Prepare("deposit-part",actor,box.id,"outfit-stack",2,out _) && CosmeticEquipment.IsEquipped(actor,"outfit-stack") &&
                deposits.Commit("deposit-part") && !deposits.Commit("deposit-part"),"Shared partial deposit/повтор");
            Require(deposits.Prepare("deposit-last",actor,box.id,"outfit-stack",1,out _) && Selection(actor).item==null && deposits.Cancel("deposit-last") &&
                actor.inventory.Single().count==1 && !CosmeticEquipment.IsEquipped(actor,"outfit-stack"),"Резерв последней единицы/отмена вернули фантом");
            // Actor type registration only supplies the real body-inventory capability for this isolated fixture.
            ActorTypes.Register(new ActorTypeDefinition {id="check:cosmetic-body",presentation="pony",bodyInventory=true},true);
            actor.actorType="check:cosmetic-body";actor.down=true;Equip(actor,catalog);
            var recipient=Actor("recipient",1);recipient.inventory.Clear();recipient.x=recipient.y=0;world.actors.Add(recipient);
            var claims=new SharedInventory(world);var body=InventorySource.FromBody(actor);
            Require(claims.PrepareClaim("body-clothing",body.id,"outfit-stack",recipient) && actor.inventory.Count==0 && Selection(actor).item==null,
                "Подбор из тела не снял последнюю единицу");
            var grant=world.pendingGrants.Single();claims.CommitGrant(grant);claims.CommitGrant(grant);
            Require(recipient.inventory.Single().count==1 && recipient.cosmeticEquipment==null && world.completedTransactions.Count==3,
                "Подбор одежды повторился либо автоматически надел её получателю");
        }
        private static void CheckMergeAndOwnerSelection()
        {
            var catalog=Catalog();var canonical=Actor();Equip(canonical,catalog);var sent=canonical.Copy();
            sent.inventory.Add(new ItemStack {id="second-outfit",definition=Second,count=2});canonical.inventory.Add(sent.inventory[1].Copy());
            Require(CosmeticEquipment.TryEquip(sent,"second-outfit",false,out _,catalog),"Выбор второго комплекта");
            // A coordinated grant advances the actor counter after the outgoing owner's selection.
            InventoryStacks.Add(canonical,new ItemStack {id="canonical-grant",definition="caps",count=4});canonical.revision=100;
            canonical.damageSequence=7;canonical.health=31;canonical.x=24;canonical.heldItemId="canonical-held";
            string untouched=WithoutEquipment(canonical);long revision=canonical.revision;
            Require(!ActorVersion.CanPublishOwnerState(sent,canonical) && CosmeticEquipment.TryApplyOwnerSelection(canonical,sent,catalog) &&
                Selection(canonical).item=="second-outfit" && canonical.revision==revision+1 && WithoutEquipment(canonical)==untouched,
                "Поздний выбор после grant изменил canonical HP/motion/weapon/inventory либо потерял одежду");
            string accepted=StateJson.ToJson(canonical);
            Require(!CosmeticEquipment.TryApplyOwnerSelection(canonical,sent,catalog) && StateJson.ToJson(canonical)==accepted,"Повтор wardrobe stamp применился");
            foreach(Action<ActorState> boundary in new Action<ActorState>[] {a=>a.id="other",a=>a.character="other",a=>a.owner="other",
                a=>a.generation++,a=>a.lifecycleSequence++,a=>a.level="factory:0",a=>a.connected=false,a=>a.player=false})
            {
                var changed=sent.Copy();changed.cosmeticEquipment.revision=10;boundary(changed);
                Require(!CosmeticEquipment.TryApplyOwnerSelection(canonical,changed,catalog) && StateJson.ToJson(canonical)==accepted,
                    "Выбор принят через identity/owner/lifecycle/level boundary");
            }
            var old=canonical.Copy();InventoryJournal.Debit(canonical,"canonical-last","second-outfit",2);
            old.cosmeticEquipment.revision=20;old.revision=0;
            untouched=WithoutEquipment(canonical);
            Require(CosmeticEquipment.TryApplyOwnerSelection(canonical,old,catalog) && Selection(canonical).item==null &&
                WithoutEquipment(canonical)==untouched && canonical.inventory.All(i=>i.id!="second-outfit"),"Позднее надевание восстановило переданную вещь");
            var current=Actor();Equip(current,catalog);var incoming=current.Copy();incoming.cosmeticEquipment=null;
            CosmeticEquipment.Reconcile(current,incoming);Require(Selection(incoming).item=="outfit-stack","Missing component откатил более новый выбор");
            incoming=current.Copy();incoming.inventory.Clear();incoming.cosmeticEquipment.revision=0;
            CosmeticEquipment.Reconcile(current,incoming);Require(Selection(incoming).item==null && incoming.inventory.Count==0,"Newer local selection воскресил canonical inventory");
            incoming=current.Copy();incoming.lifecycleSequence++;incoming.cosmeticEquipment=null;
            CosmeticEquipment.Reconcile(current,incoming);Require(incoming.cosmeticEquipment==null,"Выбор перенесён через life boundary");
            var forged=sent.Copy();forged.cosmeticEquipment.revision=30;Selection(forged).definition=First;
            accepted=StateJson.ToJson(canonical);Reject(()=>CosmeticEquipment.TryApplyOwnerSelection(canonical,forged,catalog),"визуальное снаряжение");
            Require(StateJson.ToJson(canonical)==accepted,"Malformed selection частично изменила canonical actor");
        }
        private static void CheckUnknownAndMalformed()
        {
            var catalog=Catalog();var actor=Actor();Equip(actor,catalog);
            actor.extensions=Extension();actor.cosmeticEquipment.extensions=Extension();Selection(actor).extensions=Extension();
            string original=StateJson.ToJson(actor);var copy=actor.Copy();
            Require(StateJson.ToJson(copy)==original && !ReferenceEquals(copy.cosmeticEquipment,actor.cosmeticEquipment) &&
                !ReferenceEquals(Selection(copy).extensions,Selection(actor).extensions),"ActorState.Copy потерял unknown payload или alias");
            Selection(copy).extensions["mod:cosmetic"]["exact"]=0;Require(StateJson.ToJson(actor)==original,"Изменение detached copy затронуло источник");
            var future=actor.Copy();future.cosmeticEquipment.version=4;future.cosmeticEquipment.entries=null;
            string unknown=StateJson.ToJson(future);var futureCopy=future.Copy();CosmeticEquipment.ReconcileInventory(futureCopy);
            Require(!CosmeticEquipment.TryEquip(futureCopy,"outfit-stack",true,out string reason,catalog) && reason.Contains("не поддерживается") &&
                StateJson.ToJson(futureCopy)==unknown,"Будущий компонент изменён вместо сохранения");
            var downgraded=actor.Copy();CosmeticEquipment.Reconcile(futureCopy,downgraded);
            Require(StateJson.ToJson(downgraded.cosmeticEquipment)==StateJson.ToJson(futureCopy.cosmeticEquipment),"Known owner snapshot silently заменил неподдержанный future компонент");
            var missing=actor.Copy();missing.inventory[0].definition=Selection(missing).definition="mod:unknown-outfit";
            Require(!CosmeticEquipment.TryResolve(missing,Selection(missing),out _,out reason,catalog) && reason!=null &&
                CosmeticEquipment.IsEquipped(missing,"outfit-stack"),"Unknown definition отброшен/подменён");
            foreach(Action<ActorState> change in new Action<ActorState>[] {a=>a.cosmeticEquipment.version=0,a=>a.cosmeticEquipment.revision=-1,
                a=>a.cosmeticEquipment.entries=null,a=>a.cosmeticEquipment.entries.Add(new CosmeticSelection {slot=Slot}),
                a=>Selection(a).item=" ",a=>Selection(a).definition=Second})
            {var invalid=actor.Copy();change(invalid);Reject(()=>CosmeticEquipment.Validate(invalid),"визуальное снаряжение");}
            foreach(Action<ActorState> change in new Action<ActorState>[] {a=>Selection(a).slot="wrong-slot",a=>a.anatomy="radroach"})
            {var invalid=actor.Copy();change(invalid);Reject(()=>CosmeticEquipment.TryResolve(invalid,Selection(invalid),out _,out _,catalog),"визуальное снаряжение");}
            var malformed=actor.Copy();malformed.cosmeticEquipment.entries.Add(new CosmeticSelection {slot=Slot});
            string before=StateJson.ToJson(malformed);Reject(()=>InventoryJournal.Debit(malformed,"bad-debit","outfit-stack",3),"визуальное снаряжение");
            Require(StateJson.ToJson(malformed)==before,"Malformed known state допущен к частичному расходу");
            Require(StateJson.FromJson<ActorState>("{\"id\":\"legacy\"}").cosmeticEquipment==null,"Legacy null заменён компонентом");
        }
        private static IDictionary<string,JToken> Extension()=>new Dictionary<string,JToken>
        { ["mod:cosmetic"]=StateJson.FromJson<CosmeticSelection>("{\"mod:cosmetic\":{\"exact\":0.1234567890123456789012345678,\"label\":\"сохранить\",\"nested\":[true,\"1e100\"]}}").extensions["mod:cosmetic"] };
        private static void CheckEntry()
        {
            var catalog=Catalog();var profile=Actor();Equip(profile,catalog);var saved=profile.Copy();saved.connected=false;saved.level="factory:0";
            saved.cosmeticEquipment=null;var world=new WorldState();world.actors.Add(saved);
            Require(ReferenceEquals(PlayerWorldEntry.Reconcile(world,saved,profile,out _),saved) && saved.cosmeticEquipment==null,
                "Outside-hub профиль заменил wardrobe оставленного тела");
            saved.level="hub";saved.inventory.Clear();saved.inventoryUses.Add(new InventoryUse {id="confirmed-last",debits={new ItemDebit {item="outfit-stack",count=3}}});
            world.completedTransactions.Add("confirmed-last");string source=StateJson.ToJson(profile);
            var entered=PlayerWorldEntry.Reconcile(world,saved,profile,out _);
            Require(entered.inventory.Count==0 && Selection(entered).item==null && StateJson.ToJson(profile)==source,
                "Вход из хаба воскресил подтверждённый расход или изменил личный исходник");
        }
        private static void CheckPersistence()
        {
            WeaponCatalog.Load();var catalog=Catalog();var actor=Actor();Equip(actor,catalog);
            actor.extensions=Extension();actor.cosmeticEquipment.extensions=Extension();Selection(actor).extensions=Extension();
            var world=new WorldState();world.actors.Add(actor);string source=StateJson.ToJson(world);
            var snapshot=new RecoverySnapshot(source);var restored=snapshot.CreateLiveWorld();
            Require(StateJson.ToJson(restored.actors[0].cosmeticEquipment)==StateJson.ToJson(actor.cosmeticEquipment),"RecoverySnapshot потерял выбор/unknown");
            Selection(restored.actors[0]).item=null;Require(Selection(snapshot.CreateLiveWorld().actors[0]).item=="outfit-stack","Snapshot retained mutable wardrobe graph");
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/cosmetic-equipment-"+Guid.NewGuid().ToString("N")));
            var store=new SaveStore(Path.Combine(path,"Profile"),Path.Combine(path,"World"));store.SaveRecoveryWithCharacter(snapshot,actor);
            var loaded=store.LoadCharacter();var loadedWorld=store.LoadWorld();
            Require(StateJson.ToJson(loaded.cosmeticEquipment)==StateJson.ToJson(actor.cosmeticEquipment) &&
                StateJson.ToJson(loadedWorld.actors[0].cosmeticEquipment)==StateJson.ToJson(actor.cosmeticEquipment) && store.SelectedSnapshot().Id==snapshot.Id,
                "Durable pair/load изменили wardrobe/world identity");
            var stale=actor.Copy();stale.inventory.Clear();store.SaveCharacter(stale);
            string before=store.ReadCharacterSource();loaded=store.LoadCharacter();
            Require(Selection(loaded).item==null && store.ReadCharacterSource()==before,"Load preparation не очистила ссылку или переписала источник");
            var invalid=StateJson.Copy(world);invalid.actors[0].cosmeticEquipment.revision=-1;
            Reject(()=>new RecoverySnapshot(StateJson.ToJson(invalid)),"визуальное снаряжение",typeof(InvalidDataException));
            var future=actor.Copy();future.cosmeticEquipment.version=4;future.cosmeticEquipment.entries=null;store.SaveCharacter(future);
            loaded=store.LoadCharacter();Require(StateJson.ToJson(loaded.cosmeticEquipment)==StateJson.ToJson(future.cosmeticEquipment),"Load потеряла будущий компонент");
        }
        private static void Reject(Action action,string diagnostic,Type type=null)
        {
            try {action();}
            catch(Exception error) when((type==null?error is InvalidOperationException || error is InvalidDataException:type.IsInstanceOfType(error)) &&
                (diagnostic==null || error.Message.Contains(diagnostic))) {return;}
            throw new InvalidOperationException("CosmeticEquipmentChecks: ожидаемый отказ отсутствует: "+diagnostic);
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("CosmeticEquipmentChecks: "+reason);}
    }
}
