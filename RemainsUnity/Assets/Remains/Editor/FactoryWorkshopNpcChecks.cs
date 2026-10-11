using System;
using System.Linq;
using Remains.Content;
using Remains.Runtime;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWorkshopNpcChecks
    {
        public static void Run()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var room=RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId);
            var current=FactoryContentCheckFixtures.BeforeOffice(RoomNpcContent.Read(room,"factory:0"));var geometry=room.CreateLayout();
            int crawlers=current.actors.Count(a=>a.actorType=="radroach");
            Require(crawlers==0 || crawlers==2,"Радтараканы установлены частично.");
            // Retain the preceding raider expansion's focused fixture after the crawler addition.
            var layout=StateJson.Copy(current);layout.actors=layout.actors.Where(a=>a.actorType!="radroach").ToArray();
            layout.loot=layout.loot.Where(b=>layout.actors.Any(a=>a.id==b.actor)).ToArray();
            layout.patch="factory:0:"+FactoryWorkshopNpcAuthoring.Patch;
            layout.expansion=new ContentExpansion {previousPatch="factory:0:"+FactoryWorkshopNpcAuthoring.PreviousPatch,
                additions=layout.actors.Where(a=>a.id.StartsWith("factory:0:original:plant:18:",StringComparison.Ordinal)).Select(a=>a.id).ToArray()};
            var records=room.GetComponentsInChildren<RoomImportAudit>(true).Single(a=>a.Report.SourceId=="original:plant:18").Report.Records;
            var normal=records.Where(r=>r.kind=="enl2").ToArray();
            Require(normal.Length==5 && normal.All(r=>r.installed) && records.Count(r=>r.kind=="enl1" && r.installed)==crawlers,
                "Метки обычных и мелких ползучих врагов потеряны или смешаны.");
            Require(layout.actors.Length==6 && layout.loot.Length==6 && layout.expansion?.additions.Length==5,"Не объявлены инвентари входа/цеха и пять добавочных NPC.");
            foreach(var record in normal)
            {
                string id="factory:0:original:plant:18:"+record.code;
                var npc=layout.actors.Single(a=>a.id==id);
                var expected=FactorySourceCoordinates.Placement(record.sourceX,record.sourceY,2)+Vector2.right*38.4f;
                Require(Vector2.Distance(npc.Position,expected)<.001f && npc.actorType=="raider" && npc.anatomy=="raider" && npc.owner==null,
                    "Источник позиции/типа/владельца расходится с явной встречей: "+id);
                Require(ActorTerrainMotion.IsClear(geometry,Anatomy.Movement(npc),npc.Position),"Начальный NPC расположен внутри стены: "+id);
            }
            var combatNpc=layout.actors.Single(a=>a.id.EndsWith(":bU92TKI1pfiaqh6L",StringComparison.Ordinal));
            var playerHull=Anatomy.Movement(new ActorState {anatomy="pony"});var fixtureStart=combatNpc.Position+Vector2.left*1.6f;
            Require(ActorTerrainMotion.IsClear(geometry,playerHull,fixtureStart) && ActorTerrainMotion.Supported(geometry,playerHull,fixtureStart),
                "Проверочная позиция боевого стенда не подходит телу игрока.");
            var world=new WorldState {worldId="factory-npc-inventory-check"};NpcContentInstaller.Apply(world,layout);
            var populated=world.actors.Where(a=>layout.expansion.additions.Contains(a.id)).ToArray();
            Require(populated.Length==5 && populated.All(a=>a.inventory.Count==3 && WeaponInventory.Selected(a)?.definition=="pistol" &&
                WeaponInventory.Selected(a).weaponState.rounds==6 && InventoryJournal.Count(a,"p10")>=4 && InventoryJournal.Count(a,"p10")<=12),
                "Встреча не создала полный единый инвентарь или выбранный предмет оружия.");
            string inventory=StateJson.ToJson(populated.Select(a=>a.inventory).ToArray());
            var same=new WorldState {worldId=world.worldId};NpcContentInstaller.Apply(same,layout);
            Require(StateJson.ToJson(same.actors.Where(a=>layout.expansion.additions.Contains(a.id)).Select(a=>a.inventory).ToArray())==inventory,
                "Один WorldId/ActorId даёт другой начальный лут.");
            var second=StateJson.Copy(layout);second.level="factory:6";second.patch=second.patch.Replace("factory:0:","factory:6:");
            second.expansion.previousPatch=second.expansion.previousPatch.Replace("factory:0:","factory:6:");
            second.expansion.additions=second.expansion.additions.Select(id=>id.Replace("factory:0:","factory:6:")).ToArray();
            foreach(var actor in second.actors) {actor.id=actor.id.Replace("factory:0:","factory:6:");actor.level=second.level;}
            foreach(var binding in second.loot) binding.actor=binding.actor.Replace("factory:0:","factory:6:");
            NpcContentInstaller.Apply(world,second);
            var items=world.actors.SelectMany(a=>a.inventory).ToArray();
            Require(world.actors.Count==12 && items.Length==36 && items.Select(i=>i.id).Distinct().Count()==36,
                "Экземпляры заводов делят врагов или предметы.");
            CheckUpgrade(layout);CheckClaims(layout);CheckInvalid(layout);
            Debug.Log("Враги цеха: PASS — пять исходных мест, отдельные фабрики, единый инвентарь, добавление в старый мир без возрождения/пополнения, первый подтверждённый запрос и атомарные отказы.");
        }
        private static void CheckUpgrade(NpcLayout current)
        {
            var old=StateJson.Copy(current);old.patch=current.expansion.previousPatch;old.expansion=null;old.loot=Array.Empty<NpcLootBinding>();
            old.actors=old.actors.Where(a=>!current.expansion.additions.Contains(a.id)).ToArray();
            var world=new WorldState {worldId="factory-npc-upgrade-check"};NpcContentInstaller.Apply(world,old);
            var original=world.actors.Single();original.health=0;original.down=true;original.x=23;original.owner="saved-owner";original.generation=7;
            original.inventory.Add(new ItemStack {id="saved-caps",definition="caps",count=17});string preserved=StateJson.ToJson(original);
            world=StateJson.Copy(world);Require(NpcContentInstaller.Apply(world,current),"Дополнение старого мира не установлено.");
            Require(world.actors.Count==6 && StateJson.ToJson(world.Find(original.id))==preserved,"Изменено прежнее тело или его инвентарь.");
            var body=world.actors.First(a=>current.expansion.additions.Contains(a.id));body.down=true;body.health=0;body.inventory.Clear();
            var removed=world.actors.Last();world.actors.Remove(removed);world=StateJson.Copy(world);string before=StateJson.ToJson(world);
            Require(!NpcContentInstaller.Apply(world,current) && StateJson.ToJson(world)==before,"Вернулся удалённый враг или забранный лут.");
            var absent=new WorldState {worldId="factory-npc-removed-old-check"};NpcContentInstaller.Apply(absent,old);absent.actors.Clear();
            NpcContentInstaller.Apply(absent,current);
            Require(absent.actors.Count==5 && absent.Find(original.id)==null,"Добавление вернуло удалённого прежнего рейдера.");
        }
        private static void CheckClaims(NpcLayout layout)
        {
            var world=new WorldState {worldId="factory-npc-claim-check"};NpcContentInstaller.Apply(world,layout);
            var body=world.actors.First(a=>a.inventory.Count==3);body.down=true;body.health=0;
            var first=new ActorState {id="recipient-one",player=true,connected=true,level=body.level,Position=body.Position};
            var second=first.Copy();second.id="recipient-two";world.actors.Add(first);world.actors.Add(second);
            string source=InventorySource.BodyId(body.id);var weapon=WeaponInventory.Selected(body);int containers=world.containers.Count;
            var transfers=new SharedInventory(world);
            Require(ReferenceEquals(InventorySource.Resolve(world,source).items,body.inventory) &&
                transfers.PrepareClaim("npc-claim-first",source,weapon.id,first) && first.inventory.Count==0 && body.weapon==null &&
                !transfers.PrepareClaim("npc-claim-second",source,weapon.id,second),"Тело скопировано, награда выдана до подтверждения или повторно зарезервирована.");
            world=StateJson.Copy(world);transfers=new SharedInventory(world);var pending=world.pendingGrants.Single();
            transfers.CommitGrant(pending);transfers.CommitGrant(pending);
            Require(world.Find(first.id).inventory.Single().id==weapon.id && world.Find(first.id).inventory.Single().weaponState.rounds==6 &&
                world.Find(second.id).inventory.Count==0 && world.Find(body.id).inventory.Count==2 && world.containers.Count==containers,
                "Восстановление/повтор подтвердили предмет дважды, потеряли магазин или создали контейнерную копию.");
        }
        private static void CheckInvalid(NpcLayout layout)
        {
            void Reject(NpcLayout value)
            {
                var world=new WorldState {worldId="factory-npc-invalid-check"};string before=StateJson.ToJson(world);bool rejected=false;
                try {NpcContentInstaller.Apply(world,value);} catch(InvalidOperationException) {rejected=true;}
                Require(rejected && StateJson.ToJson(world)==before,"Ошибочное содержимое частично изменило мир.");
            }
            var bad=StateJson.Copy(layout);bad.expansion.additions=new[]{"unknown-npc"};Reject(bad);
            bad=StateJson.Copy(layout);bad.loot=new[]{bad.loot[0],bad.loot[0]};Reject(bad);
            bad=StateJson.Copy(layout);bad.loot[0].table.entries[0].definition="crowbar";Reject(bad);
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Враги цеха: "+reason);}
    }
}
