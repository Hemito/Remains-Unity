using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryOfficeNpcChecks
    {
        public static void Run()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        }
        public static void Check(RoomAuthoring room)
        {
            var office=room.GetComponentsInChildren<RoomAuthoring>(true).Single(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53");
            CheckOffice(office,true);
            var layout=RoomNpcContent.Read(room,"factory:0");var geometry=room.CreateLayout(false);
            geometry.platforms=DoorGeometry.Compose(geometry.platforms,RoomDoorContent.Read(room,"factory:0").doors,"factory:0");
            Require(layout.patch=="factory:0:"+FactoryOfficeNpcAuthoring.ConnectedPatch && layout.actors.Length==13 && layout.loot.Length==13,
                "Связный завод не содержит ровно 13 NPC/инвентарей в версии v4.");
            foreach(var entry in FactoryOfficeNpcAuthoring.Spawns)
            {
                var actor=layout.actors.Single(a=>a.id=="factory:0:"+entry.Id);
                Require(Vector2.Distance(actor.Position,entry.Position+Vector2.right*76.8f)<.001f && actor.actorType==entry.type,
                    "У NPC потеряны source ID или единственное смещение модуля: "+actor.id);
                CheckGround(geometry,actor,actor.Position,"NPC "+actor.id);
            }
            var raider=layout.actors.Single(a=>a.id=="factory:0:original:plant:53:ql47iXGSZW2VGGdF");
            var player=new ActorState {anatomy="pony",Position=raider.Position+Vector2.left*1.6f};
            CheckGround(geometry,player,player.Position,"начало огневой проверки ql47iX");
            Require(RoomVisibility.Clear(geometry,player.Position+Vector2.up*.55f,raider.Position+Vector2.up*.55f),
                "Огневая проверка ql47iX закрыта стеной/дверью.");
            for(int i=0;i<=10;i++)
            {
                var point=player.Position+Vector2.right*(i*.075f);CheckGround(geometry,player,point,"подход к телу ql47iX");
                CheckPhysicalClear(room,player,point);
            }
            bool officeLoot=FactoryOfficeLootChecks.CheckOfficeStage(office);
            // instr1 now occupies the historical first-crawler player fixture. Source bodies are unchanged.
            var crawler=layout.actors.Single(a=>a.id=="factory:0:original:plant:53:"+(officeLoot?"lAIXKaUQC2e4JtW5":"asgXcpTEfby23bgi"));
            var crawlerStart=crawler.Position+(officeLoot?Vector2.left:Vector2.right)*1.1f;
            CheckGround(geometry,player,crawlerStart,"начало проверки укуса "+crawler.id);
            CheckPhysicalClear(room,player,crawlerStart);
            Require(RoomVisibility.Clear(geometry,crawler.Position+Vector2.up*.16f,crawlerStart+Vector2.up*.25f),"Стена закрывает проверку естественной атаки.");
            CheckPersistence(layout,RoomNpcContent.Read(room,"factory:6"));
            Debug.Log("Встреча офисов53: PASS — 5 исходных ID, 2 радтаракана/3 рейдера из сохранённых SO, опора/свободные тела, v1/v2/v3→v4, 13 новых NPC, сохранение отсутствий/смерти/оружия/владельца/модовых полей, единый лут и разные фабрики.");
        }
        internal static void CheckOffice(RoomAuthoring room,bool installed)
        {
            FactoryOfficeNpcAuthoring.ValidateSource(room.GetComponent<RoomImportAudit>().Report,installed);
            bool officeLoot=FactoryOfficeLootChecks.CheckOfficeStage(room);
            Require(room.GetComponent<RoomImportAudit>().Report.Records.Count(r=>!r.installed)==(installed?21:26)-(officeLoot?4:0),"Скрыты другие неподключённые объекты офиса.");
            var layout=RoomNpcContent.Read(room,"factory:0");var geometry=room.CreateLayout(false);
            var markers=room.GetComponentsInChildren<RoomActorPlacement>(true).Where(room.Included).ToArray();
            Require(layout?.patch=="factory:0:"+FactoryOfficeNpcAuthoring.OfficePatch && layout.actors.Length==5 && layout.loot.Length==5 &&
                layout.expansion==null && markers.Length==5,"Офис не содержит самостоятельную начальную версию из пяти NPC.");
            foreach(var entry in FactoryOfficeNpcAuthoring.Spawns)
            {
                var marker=markers.Single(p=>p.Read(room).id==entry.Id);var actor=layout.actors.Single(a=>a.id=="factory:0:"+entry.Id);
                Require(!marker.FixedPlacement && !actor.player && !actor.connected && actor.owner==null && actor.target==null && !actor.down &&
                    !actor.attackBusy && actor.facing==-1 && actor.actorType==entry.type && actor.anatomy==entry.type && Vector2.Distance(actor.Position,entry.Position)<.001f,
                    "NPC подменён закреплённой мишенью/владельцем/типом/позиционной эвристикой: "+entry.RecordId);
                var expected=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(entry.SpawnPath);
                Require(new SerializedObject(marker).FindProperty("spawn").objectReferenceValue==expected && expected!=null &&
                    StateJson.ToJson(marker.ReadInventory())==StateJson.ToJson(expected.ReadInventory()),"Используется другой SO/начальный инвентарь: "+entry.RecordId);
                Require(entry.type=="radroach"?actor.health==8 && string.IsNullOrEmpty(actor.weapon):actor.health==140 && actor.weapon=="pistol",
                    "Начальное состояние не соответствует выбранной существующей встрече: "+entry.RecordId);
                var type=ActorTypes.Get(entry.type);Require(type.combatAi && type.bodyInventory &&
                    (entry.type=="radroach"?!type.hasWeapon && type.naturalAttack!=null:type.hasWeapon && type.canUseDoors),"NPC потерял боевое поведение/инвентарь: "+entry.RecordId);
                CheckGround(geometry,actor,actor.Position,"локальный NPC "+entry.RecordId);CheckPhysicalClear(room,actor,actor.Position);
            }
            var first=layout.actors.Single(a=>a.id.EndsWith(":asgXcpTEfby23bgi",StringComparison.Ordinal));
            Require(!ActorTerrainMotion.Supported(geometry,Anatomy.Movement(first),new Vector2(14,15.2f)) &&
                Vector2.Distance(first.Position,new Vector2(14.8f,15.2f))<.001f,"Не подтверждён единственный authored offset над исходным разрывом пола.");
        }
        private static void CheckGround(RoomLayout room,ActorState actor,Vector2 point,string context)
        {Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),point) && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),point),"Нет свободного тела/опоры: "+context+", position="+point);}
        private static void CheckPhysicalClear(RoomAuthoring room,ActorState actor,Vector2 point)
        {
            var physical=RoomPhysicalContent.Read(room,"factory:0");var hull=Anatomy.Movement(actor);var body=new Rect(point.x-hull.width*.5f,point.y,hull.width,hull.height);
            foreach(var prop in physical.props.Cast<PropState>().Concat(physical.containers))
                if(prop.hasPhysicalBody) Require(!body.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)),
                    "Проверочная позиция пересекает физический объект: actor="+actor.id+", prop="+prop.id+", point="+point);
        }
        // Pure persistence checks are also runnable with explicit layouts outside the Unity editor.
        public static void CheckPersistence(NpcLayout layout,NpcLayout other)
        {
            string prefix=layout.level+":";
            var officeIds=FactoryOfficeNpcAuthoring.Spawns.Select(s=>prefix+s.Id).ToArray();
            Require(layout.patch==prefix+FactoryOfficeNpcAuthoring.ConnectedPatch && layout.actors.Length==13 && layout.loot.Length==13 &&
                layout.actors.Count(a=>a.actorType=="radroach")==4 && layout.actors.Count(a=>a.actorType=="raider")==9 &&
                layout.actors.Select(a=>a.id).Distinct().Count()==13 && layout.loot.Select(b=>b.actor).Distinct().Count()==13,
                "Нет ровно 13 уникальных NPC/инвентарей v4.");
            var crawlerIds=layout.actors.Where(a=>a.actorType=="radroach" && a.id.StartsWith(prefix+"original:plant:18:",StringComparison.Ordinal)).Select(a=>a.id).ToArray();
            var workshopIds=layout.actors.Where(a=>a.id.StartsWith(prefix+"original:plant:18:",StringComparison.Ordinal)).Select(a=>a.id).ToArray();
            var steps=new[]{(ContentExpansionStep)layout.expansion}.Concat(layout.expansion.earlierVersions).ToArray();
            Require(steps.Length==3 && steps[0].previousPatch==prefix+FactoryCrawlerAuthoring.Patch && Same(steps[0].additions,officeIds) &&
                steps[1].previousPatch==prefix+FactoryWorkshopNpcAuthoring.Patch && Same(steps[1].additions,crawlerIds.Concat(officeIds)) &&
                steps[2].previousPatch==prefix+FactoryWorkshopNpcAuthoring.PreviousPatch && Same(steps[2].additions,workshopIds.Concat(officeIds)) &&
                crawlerIds.Length==2 && workshopIds.Length==7,"Потеряна точная цепочка v3→5, v2→7, v1→12 добавочных ID.");
            var fresh=new WorldState {worldId="office-encounter-new-world"};Require(NpcContentInstaller.Apply(fresh,layout),"Новая встреча не установлена.");
            Require(fresh.actors.Count==13 && officeIds.All(id=>fresh.Find(id)!=null),"Новый мир не получил все 13 NPC.");
            var office=fresh.actors.Where(a=>officeIds.Contains(a.id)).ToArray();
            Require(office.Where(a=>a.actorType=="raider").All(a=>a.inventory.Count==3 && WeaponInventory.Selected(a)?.definition=="pistol" &&
                WeaponInventory.Selected(a).weaponState.rounds==6 && InventoryJournal.Count(a,"p10") is >=4 and <=12) &&
                office.Where(a=>a.actorType=="radroach").All(a=>a.inventory.Count==1 && a.inventory[0].definition=="caps" && a.inventory[0].count is >=1 and <=2),
                "Офис не получил выбранные единые инвентари/заряженные пистолеты.");
            var same=new WorldState {worldId=fresh.worldId};NpcContentInstaller.Apply(same,layout);
            Require(StateJson.ToJson(same.actors)==StateJson.ToJson(fresh.actors),"Те же WorldId/ActorId изменили начальный лут.");
            foreach(var step in steps) foreach(string mode in new[]{"dead","active","absent"})
            {
                var world=StateJson.Copy(fresh);world.contentPatches.Remove(layout.patch);world.contentPatches.Add(step.previousPatch);
                world.actors.RemoveAll(a=>step.additions.Contains(a.id));
                var absent=mode=="absent"?world.actors[0].id:null;if(absent!=null) world.actors.RemoveAt(0);
                world.extensions=Extensions("world-mod-progress");
                foreach(var actor in world.actors)
                {
                    actor.owner="saved-owner";actor.generation=8;actor.lifecycleSequence=3;actor.revision=29;actor.x+=.2f;
                    actor.extensions=Extensions("actor-mod-progress");actor.resolvedAttacks.Add("saved-attack");
                    if(mode=="dead") {actor.health=0;actor.down=true;actor.inventory.Clear();}
                    else
                    {
                        actor.health=71;
                        foreach(var item in actor.inventory)
                        {
                            item.extensions=Extensions("item-mod-progress");
                            if(item.weaponState!=null) {item.weaponState.rounds=1;item.weaponState.revision=19;item.weaponState.extensions=Extensions("weapon-mod-progress");}
                            else if(item.definition=="p10") item.count=2;
                        }
                    }
                }
                world=StateJson.Copy(world);var retained=world.actors.Select(a=>(a.id,json:StateJson.ToJson(a))).ToArray();
                string worldExtensions=StateJson.ToJson(world.extensions);int count=world.actors.Count;
                Require(NpcContentInstaller.Apply(world,layout) && world.actors.Count==count+step.additions.Length &&
                    retained.All(a=>world.Find(a.id)!=null && StateJson.ToJson(world.Find(a.id))==a.json) &&
                    (absent==null || world.Find(absent)==null) && StateJson.ToJson(world.extensions)==worldExtensions,
                    "Обновление изменило сохранённое тело/оружие/владельца/модовые поля или вернуло отсутствующий NPC: "+step.previousPatch+", "+mode);
                var removed=world.actors.Last(a=>officeIds.Contains(a.id));world.actors.Remove(removed);
                world=StateJson.Copy(world);string before=StateJson.ToJson(world);
                Require(!NpcContentInstaller.Apply(world,layout) && StateJson.ToJson(world)==before && world.Find(removed.id)==null,
                    "Повторная установка/загрузка вернула NPC или пополнила его лут: "+step.previousPatch+", "+mode);
            }
            NpcContentInstaller.Apply(fresh,other);
            var allIds=fresh.actors.Select(a=>a.id).ToArray();var itemIds=fresh.actors.SelectMany(a=>a.inventory).Select(i=>i.id).ToArray();
            Require(other.level=="factory:6" && fresh.actors.Count==26 && allIds.Distinct().Count()==26 && itemIds.Distinct().Count()==itemIds.Length,
                "Фабрики0/6 разделяют ID NPC или начальных предметов.");
            CheckClaim(same,officeIds);
        }
        private static void CheckClaim(WorldState world,string[] officeIds)
        {
            var body=world.actors.First(a=>officeIds.Contains(a.id) && a.actorType=="raider");body.health=0;body.down=true;
            var item=WeaponInventory.Selected(body);item.weaponState.rounds=2;item.weaponState.extensions=Extensions("corpse-weapon");
            string weaponBefore=StateJson.ToJson(item.weaponState);
            var player=new ActorState {id="office-loot-recipient",player=true,connected=true,level=body.level,Position=body.Position};world.actors.Add(player);
            var inventory=new SharedInventory(world);string source=InventorySource.BodyId(body.id);
            Require(ReferenceEquals(InventorySource.Resolve(world,source).items,body.inventory) && inventory.PrepareClaim("office-weapon-claim",source,item.id,player) &&
                player.inventory.Count==0 && !inventory.PrepareClaim("office-second-claim",source,item.id,player),"Тело имеет отдельную копию лута либо предмет выдан до подтверждения/дважды.");
            world=StateJson.Copy(world);inventory=new SharedInventory(world);var grant=world.pendingGrants.Single();inventory.CommitGrant(grant);inventory.CommitGrant(grant);
            Require(world.Find(player.id).inventory.Single().id==item.id && StateJson.ToJson(world.Find(player.id).inventory.Single().weaponState)==weaponBefore &&
                !world.Find(body.id).inventory.Any(i=>i.id==item.id),"Повторное подтверждение потеряло/удвоило пистолет или изменило патроны/модовые поля.");
        }
        private static Dictionary<string,JToken> Extensions(string key)=>new() {{key,JToken.Parse("{\"nested\":[17,\"сохранённое поле мода\"]}")}};
        private static bool Same(IEnumerable<string> left,IEnumerable<string> right)=>left.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(right.OrderBy(id=>id,StringComparer.Ordinal));
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("NPC офисов53: "+reason);}
    }
}
