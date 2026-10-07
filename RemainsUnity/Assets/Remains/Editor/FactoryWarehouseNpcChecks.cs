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
    public static class FactoryWarehouseNpcChecks
    {
        public static void Run()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWarehouseNpcAuthoring.Path);
            Require(prefab!=null,"Отсутствует самостоятельный prefab склада40: "+FactoryWarehouseNpcAuthoring.Path);
            var room=prefab.GetComponent<RoomAuthoring>();Check(room);
            var layout=RoomNpcContent.Read(room,"factory:0");
            CheckPersistence(layout,RoomNpcContent.Read(room,"factory:6"));
            CheckGeometryFailures(room.CreateLayout(false),RoomPhysicalContent.Read(room,"factory:0"),RoomDoorContent.Read(room,"factory:0").doors,layout);
            CheckSnapshotFields(room.CreateLayout(),RoomContentBatch.Read(room,"factory:0"),room.GetComponent<RoomImportAudit>().Report.Records);
            Debug.Log("NPC склада40: PASS — 9 source ID без offset, 7 рейдеров/2 радтаракана из существующих SO; actual anatomy + terrain/33 bodies/точная стадия2/4 дверей; начальный лут один раз, сохранение владельца/смерти/движения/оружия/модовых полей, разные фабрики и подтверждённый единый лут тела.");
        }
        // False is only the wholly absent stage. Partial or disabled content is a visible error.
        public static bool IsInstalled(RoomAuthoring room)
        {
            Require(room!=null,"Нет RoomAuthoring склада40.");
            var content=room.GetComponentsInChildren<RoomNpcContent>(true);
            var markers=room.GetComponentsInChildren<RoomActorPlacement>(true);
            if(content.Length==0 && markers.Length==0)
            {FactoryWarehouseNpcAuthoring.ValidateSource(room.GetComponent<RoomImportAudit>()?.Report,false);return false;}
            Require(content.Length==1 && content[0].gameObject==room.gameObject && room.Included(content[0]),
                "Частичная/отключённая версия NPC; авторские данные не заменены.");
            Check(room);return true;
        }
        public static void Check(RoomAuthoring room)=>CheckInstalled(room,true);
        internal static void CheckInstalled(RoomAuthoring room,bool decisionsInstalled)
        {
            Require(room!=null,"Нет RoomAuthoring склада40.");
            FactoryWarehouseNpcAuthoring.ValidateSource(room.GetComponent<RoomImportAudit>()?.Report,decisionsInstalled);
            var content=room.GetComponentsInChildren<RoomNpcContent>(true);
            Require(content.Length==1 && content[0].gameObject==room.gameObject && room.Included(content[0]),"Версия NPC отсутствует/выключена/вложена в другой модуль.");
            var layout=RoomNpcContent.Read(room,"factory:0");CheckLayout(layout);
            var markers=room.GetComponentsInChildren<RoomActorPlacement>(true);
            Require(markers.Length==9 && markers.All(room.Included),"Нужны ровно 9 включённых NPC без скрытых лишних markers.");
            foreach(var entry in FactoryWarehouseNpcAuthoring.Spawns)
            {
                var matches=markers.Where(p=>p.Read(room).id==entry.Id).ToArray();
                Require(matches.Length==1,"Нет единственного marker: "+entry.RecordId);
                var marker=matches[0];var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(entry.SpawnPath);
                Require(spawn!=null && new SerializedObject(marker).FindProperty("spawn").objectReferenceValue==spawn && !marker.FixedPlacement,
                    "NPC заменён мишенью или другим Spawn SO: "+entry.RecordId);
                var actor=layout.actors.Single(a=>a.id==layout.level+":"+entry.Id);
                var expected=spawn.Create(new TrainingPlacement {id=entry.Id,type=entry.type,x=entry.Position.x,y=entry.Position.y,facing=-1},layout.level);
                Require(Vector2.Distance(actor.Position,expected.Position)<.0001f,"NPC source anchor изменён: "+entry.RecordId+", actual="+actor.Position.ToString("R")+", expected="+expected.Position.ToString("R"));
                // A nested prefab's local matrix loses a few float bits at x=115.2.
                // Verify the position separately, then compare every other state field exactly.
                var comparable=StateJson.Copy(actor);comparable.Position=expected.Position;
                Require(StateJson.ToJson(comparable)==StateJson.ToJson(expected) && StateJson.ToJson(marker.ReadInventory())==StateJson.ToJson(spawn.ReadInventory()),
                    "NPC/лут отличаются от выбранного существующего SO: "+entry.RecordId);
                var type=ActorTypes.Get(entry.type);
                Require(type.combatAi && type.bodyInventory && (entry.type=="raider"?type.hasWeapon && type.canUseDoors:!type.hasWeapon && type.naturalAttack!=null),
                    "Тип NPC потерял боевое поведение/инвентарь: "+entry.RecordId);
            }
            CheckGeometry(room.CreateLayout(false),RoomPhysicalContent.Read(room,"factory:0"),RoomDoorContent.Read(room,"factory:0")?.doors,layout);
        }
        private static void CheckLayout(NpcLayout layout)
        {
            Require(layout!=null && !string.IsNullOrWhiteSpace(layout.level) && layout.patch==layout.level+":"+FactoryWarehouseNpcAuthoring.Patch &&
                layout.expansion==null && layout.actors?.Length==9 && layout.loot?.Length==9 &&
                layout.actors.Select(a=>a.id).Distinct().Count()==9 && layout.loot.Select(b=>b.actor).Distinct().Count()==9,
                "Нет самостоятельной версии из 9 уникальных NPC и 9 инвентарей без прежней cumulative chain.");
            foreach(var entry in FactoryWarehouseNpcAuthoring.Spawns)
            {
                var matches=layout.actors.Where(a=>a.id==layout.level+":"+entry.Id).ToArray();
                Require(matches.Length==1,"Не подтверждён source actor ID: "+entry.RecordId);
                var actor=matches[0];
                Require(actor.actorType==entry.type && actor.anatomy==entry.type && Vector2.Distance(actor.Position,entry.Position)<.0001f &&
                    !actor.player && !actor.connected && actor.owner==null && actor.target==null && !actor.down && !actor.attackBusy && actor.facing==-1 &&
                    (entry.type=="raider"?actor.health==140 && actor.weapon=="pistol":actor.health==8 && string.IsNullOrEmpty(actor.weapon)),
                    "Изменены тип/анатомия/anchor/начальное состояние выбранной встречи: "+entry.RecordId);
                var binding=layout.loot.SingleOrDefault(b=>b.actor==actor.id);Require(binding?.table!=null,"Нет начального инвентаря: "+actor.id);binding.table.Validate();
            }
        }
        // Reads the actual hull and authored physical dimensions. No debug output or helper floor is used.
        public static void CheckGeometry(RoomLayout geometry,PhysicalLayout physical,DoorState[] doors,NpcLayout layout)
        {
            bool metal=doors?.Length==4;
            var prior=FactoryWarehouseFurnitureAuthoring.BeforeFurniture(physical);
            Require(geometry!=null && prior?.props?.Length==17 && prior.containers?.Length==16 && doors?.Length==(metal?4:2) && layout?.actors?.Length==9,
                "Для геометрической проверки нужны terrain, 33 физических объекта, точная стадия2/4 дверей и9NPC склада40.");
            if(metal) FactoryWarehouseMetalDoorsPlacement.CheckNewDoors(new DoorLayout {level=physical.level,doors=doors},0);
            var room=StateJson.Copy(geometry);room.platforms=DoorGeometry.Compose(room.platforms,doors,physical.level);
            foreach(var actor in layout.actors)
            {
                var hull=Anatomy.Movement(actor);var body=new Rect(actor.x-hull.width*.5f,actor.y,hull.width,hull.height);
                Require(ActorTerrainMotion.IsClear(room,hull,actor.Position) && ActorTerrainMotion.Supported(room,hull,actor.Position),
                    "Нет свободного тела/настоящей опоры: actor="+actor.id+", anatomy="+actor.anatomy+", bounds="+body);
                foreach(var prop in physical.props.Cast<PropState>().Concat(physical.containers))
                {
                    PropBodyRules.Validate(prop);
                    Require(!prop.hasPhysicalBody || !body.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)),
                        "NPC пересекает authored physical body: actor="+actor.id+", prop="+prop.id+", bounds="+body);
                }
            }
        }
        public static void CheckGeometryFailures(RoomLayout geometry,PhysicalLayout physical,DoorState[] doors,NpcLayout layout)
        {
            CheckGeometry(geometry,physical,doors,layout);
            var actor=layout.actors[0];var missing=StateJson.Copy(geometry);
            missing.platforms=missing.platforms.Where(s=>Math.Abs(s.y+s.height-actor.y)>.001f).ToArray();
            Reject(()=>CheckGeometry(missing,physical,doors,layout),"Убранная опора не обнаружена.");
            var blocked=StateJson.Copy(physical);var prop=blocked.props[0];
            prop.x=actor.x;prop.y=actor.y;prop.anchorHeight=0;prop.width=.6f;prop.height=.5f;
            prop.mass=1;prop.hasPhysicalBody=true;prop.movable=true;
            Reject(()=>CheckGeometry(geometry,blocked,doors,layout),"Пересечение с реальным физическим объектом не обнаружено.");
            var closed=StateJson.Copy(doors);closed[0].x=actor.x-.1f;closed[0].y=actor.y;closed[0].width=.2f;closed[0].height=.5f;closed[0].open=false;
            Reject(()=>CheckGeometry(geometry,physical,closed,layout),"Закрытая дверь в теле NPC не обнаружена.");
            closed[0].open=true;CheckGeometry(geometry,physical,closed,layout);
        }
        public static void CheckPersistence(NpcLayout layout,NpcLayout other)
        {
            CheckLayout(layout);CheckLayout(other);
            Require(layout.level=="factory:0" && other.level=="factory:6","Нужны отдельные фабрики0/6.");
            var fresh=new WorldState {worldId="warehouse-encounter-world"};
            Require(NpcContentInstaller.Apply(fresh,layout) && fresh.actors.Count==9,"Новый мир не получил девять NPC.");
            foreach(var actor in fresh.actors)
            {
                if(actor.actorType=="raider")
                    Require(actor.inventory.Count==3 && WeaponInventory.Selected(actor)?.definition=="pistol" && WeaponInventory.Selected(actor).weaponState.rounds==6 &&
                        InventoryJournal.Count(actor,"p10") is >=4 and <=12 && InventoryJournal.Count(actor,"caps") is >=2 and <=10,"Рейдер потерял выбранный заряженный пистолет/инвентарь.");
                else Require(actor.inventory.Count==1 && actor.inventory[0].definition=="caps" && actor.inventory[0].count is >=1 and <=2,"Радтаракан потерял выбранный инвентарь.");
            }
            string initial=StateJson.ToJson(fresh);Require(!NpcContentInstaller.Apply(fresh,layout) && StateJson.ToJson(fresh)==initial,"Повторная установка изменила свежий мир.");
            var same=new WorldState {worldId=fresh.worldId};NpcContentInstaller.Apply(same,layout);
            Require(StateJson.ToJson(same.actors)==StateJson.ToJson(fresh.actors),"Те же WorldId/ActorId изменили начальное содержимое.");

            // A known existing actor is preserved even if this initial patch was not yet recorded.
            var retained=StateJson.Copy(fresh.actors[0]);ChangeSavedActor(retained,false);
            var previous=new WorldState {worldId=fresh.worldId,extensions=Extensions("world-progress")};previous.actors.Add(retained);
            var unrelated=new ActorState {id="warehouse-unrelated-player",player=true,connected=true,level="hub",health=73,extensions=Extensions("player-progress")};previous.actors.Add(unrelated);
            var expected=StateJson.ToJson(previous);long revision=previous.revision;
            Require(NpcContentInstaller.Apply(previous,layout) && previous.actors.Count==10,"Первое добавление не сохранило существующего NPC.");
            var projected=StateJson.Copy(previous);projected.actors.RemoveAll(a=>a.id!=retained.id && a.id!=unrelated.id);projected.contentPatches.Remove(layout.patch);projected.revision=revision;
            Require(StateJson.ToJson(projected)==expected,"Первое добавление заменило старую позу/владельца/инвентарь/другого игрока/модовые поля.");

            foreach(string mode in new[]{"active","dead"})
            {
                var saved=StateJson.Copy(fresh);saved.extensions=Extensions("world-progress");
                foreach(var actor in saved.actors) ChangeSavedActor(actor,mode=="dead");
                string absent=saved.actors[^1].id;saved.actors.RemoveAt(saved.actors.Count-1);
                saved=StateJson.Copy(saved);string before=StateJson.ToJson(saved);
                Require(!NpcContentInstaller.Apply(saved,layout) && StateJson.ToJson(saved)==before && saved.Find(absent)==null,
                    "Загрузка/повторная установка вернула удалённого NPC, пополнила лут или изменила сохранённое тело: "+mode);
            }
            NpcContentInstaller.Apply(fresh,other);
            var actorIds=fresh.actors.Select(a=>a.id).ToArray();var itemIds=fresh.actors.SelectMany(a=>a.inventory).Select(i=>i.id).ToArray();
            Require(fresh.actors.Count==18 && actorIds.Distinct().Count()==18 && itemIds.Distinct().Count()==itemIds.Length,"Фабрики0/6 разделяют NPC или предметы.");
            CheckSharedBody(same);
        }
        public static void CheckSnapshotFields(RoomLayout geometry,RoomContentBatch batch,RoomSourceRecord[] records)
        {
            string Snapshot(RoomLayout g,RoomContentBatch b,RoomSourceRecord[] r)=>FactoryWarehouseNpcAuthoring.Snapshot(g,b,"original:plant:40",FactoryWarehouseNpcAuthoring.SourceHash,r);
            string before=Snapshot(geometry,batch,records);
            Require(before!="{}" && before.Contains("geometry") && before.Contains("otherRecords"),"Снимок сравнения не сериализует реальные public fields.");
            var moved=StateJson.Copy(geometry);moved.platforms[0].x+=.2f;
            Require(Snapshot(moved,batch,records)!=before,"Снимок пропустил изменение поверхности.");
            var changed=StateJson.Copy(batch);changed.doors.doors[0].open=!changed.doors.doors[0].open;
            Require(Snapshot(geometry,changed,records)!=before,"Снимок пропустил изменение двери.");
            changed=StateJson.Copy(batch);changed.physical.props[0].x+=.2f;
            Require(Snapshot(geometry,changed,records)!=before,"Снимок пропустил изменение грузового ящика.");
            var other=StateJson.Copy(records);other.First(r=>FactoryWarehouseNpcAuthoring.Spawns.All(s=>s.RecordId!=r.recordId)).reason+=" · ручное изменение";
            Require(Snapshot(geometry,batch,other)!=before,"Снимок пропустил ручные данные другого source record.");
        }
        private static void ChangeSavedActor(ActorState actor,bool dead)
        {
            actor.owner="saved-owner";actor.generation=8;actor.lifecycleSequence=3;actor.revision=29;actor.poseSequence=7;actor.motionSequence=11;
            actor.x+=.2f;actor.vx=.4f;actor.vy=-.2f;actor.pose="jump";actor.poseTime=.17f;actor.climbing=true;
            actor.extensions=Extensions("actor-progress");actor.resolvedAttacks.Add("saved-attack");
            if(dead) {actor.health=0;actor.down=true;actor.inventory.Clear();return;}
            actor.health=71;
            foreach(var item in actor.inventory)
            {
                item.extensions=Extensions("item-progress");actor.inventoryAliases["saved-alias:"+item.id]=item.id;
                if(item.weaponState!=null) {item.weaponState.rounds=1;item.weaponState.revision=19;item.weaponState.extensions=Extensions("weapon-progress");}
                else item.count=1;
            }
        }
        private static void CheckSharedBody(WorldState world)
        {
            var body=world.actors.First(a=>a.actorType=="raider");body.health=0;body.down=true;
            var item=WeaponInventory.Selected(body);item.weaponState.rounds=2;item.weaponState.extensions=Extensions("body-weapon");
            string weapon=StateJson.ToJson(item.weaponState);
            var player=new ActorState {id="warehouse-body-recipient",player=true,connected=true,level=body.level,Position=body.Position};world.actors.Add(player);
            var inventory=new SharedInventory(world);string source=InventorySource.BodyId(body.id);
            Require(ReferenceEquals(InventorySource.Resolve(world,source).items,body.inventory) &&
                inventory.PrepareClaim("warehouse-body-claim",source,item.id,player) && player.inventory.Count==0 &&
                !inventory.PrepareClaim("warehouse-body-competing",source,item.id,player),"Лут тела копируется/выдаётся до подтверждения либо зарезервирован дважды.");
            world=StateJson.Copy(world);inventory=new SharedInventory(world);var grant=world.pendingGrants.Single();inventory.CommitGrant(grant);inventory.CommitGrant(grant);
            Require(world.Find(player.id).inventory.Single().id==item.id && StateJson.ToJson(world.Find(player.id).inventory.Single().weaponState)==weapon &&
                world.Find(body.id).inventory.All(i=>i.id!=item.id),"Подтверждение после загрузки удвоило предмет либо изменило патроны/модовые поля.");
        }
        private static Dictionary<string,JToken> Extensions(string key)=>new() {{key,JToken.Parse("{\"nested\":[17,\"сохранённое поле мода\"]}")}};
        private static void Reject(Action action,string reason)
        {bool rejected=false;try {action();}catch(InvalidOperationException){rejected=true;}Require(rejected,reason);}
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("NPC склада40: "+reason);}
    }
}
