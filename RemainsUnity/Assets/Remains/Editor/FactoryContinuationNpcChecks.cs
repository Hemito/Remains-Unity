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
    public static class FactoryContinuationNpcChecks
    {
        public static void Run()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var room=FactoryContinuationObjectsAuthoring.Load<GameObject>(FactoryContinuationNpcAuthoring.Path).GetComponent<RoomAuthoring>();Check(room);
            var layout=RoomNpcContent.Read(room,"factory:0");CheckPersistence(layout,RoomNpcContent.Read(room,"factory:6"));CheckGeometryNegatives(room,layout);CheckDeferredConflict(room);
            Debug.Log("Продолжение27 NPC: PASS — 13 исходных позиций с фактическими anatomy/terrain/cargo/barriers, без смещения/придуманного пола; сохранение тела/инвентаря/владельца/неизвестных полей, независимые фабрики и общая выдача лута. Генератор enl1 не воспроизведён; rat/term/work и enl1:64 явно pending.");
        }
        public static void Check(RoomAuthoring room)=>Check(room,true);
        internal static void Check(RoomAuthoring room,bool decisionsInstalled)
        {
            FactoryContinuationObjectsChecks.CheckLocal(room);FactoryContinuationNpcAuthoring.ValidateReport(room,decisionsInstalled);
            var components=room.GetComponentsInChildren<RoomNpcContent>(true);var markers=room.GetComponentsInChildren<RoomActorPlacement>(true);
            Require(components.Length==1 && components[0].gameObject==room.gameObject && room.Included(components[0]) && markers.Length==13 && markers.All(room.Included),"NPC: неполный набор, отключённый компонент или неверное вложение.");
            var layout=RoomNpcContent.Read(room,"factory:0");Require(layout.patch=="factory:0:"+FactoryContinuationNpcAuthoring.Patch && layout.expansion==null && layout.actors.Length==13 && layout.loot.Length==13,"Не точная локальная стадия13 NPC.");
            Require(!layout.actors.Any(a=>a.id==layout.level+":"+FactoryContinuationObjectsAuthoring.Prefix+FactoryContinuationNpcAuthoring.DeferredCode),"NPC64 установлен несмотря на подтверждённое пересечение с диваном33.");
            var spawn=FactoryContinuationObjectsAuthoring.Load<NpcSpawnAsset>(FactoryContinuationNpcAuthoring.SpawnPath);
            foreach(var r in FactoryContinuationNpcAuthoring.Records)
            {
                var marker=markers.Single(m=>m.Read(room).id==r.Id);var actor=layout.actors.Single(a=>a.id==layout.level+":"+r.Id);
                var expected=spawn.Create(new TrainingPlacement {id=r.Id,type="radroach",x=r.Anchor.x,y=r.Anchor.y,facing=-1},layout.level);
                Require(Vector2.Distance(actor.Position,expected.Position)<.0001f && !marker.FixedPlacement && new SerializedObject(marker).FindProperty("spawn").objectReferenceValue==spawn,
                    "Source anchor/mode/actual Spawn SO изменён: "+r.RecordId);
                var comparable=StateJson.Copy(actor);comparable.Position=expected.Position;
                Require(StateJson.ToJson(comparable)==StateJson.ToJson(expected) && StateJson.ToJson(marker.ReadInventory())==StateJson.ToJson(spawn.ReadInventory()) &&
                    actor.actorType=="radroach" && actor.anatomy=="radroach" && actor.health==8 && !actor.player && actor.owner==null && !actor.down &&
                    string.IsNullOrEmpty(actor.weapon),"NPC/начальный инвентарь изменены: "+r.RecordId);
            }
            var type=ActorTypes.Get("radroach");Require(type.combatAi && type.bodyInventory && !type.hasWeapon && type.naturalAttack!=null,"Существующий тип потерял естественную атаку/инвентарь тела.");
            FixedEncounterGeometryChecks.Validate(FactoryContinuationConnectionGeometry.InModule(room),RoomPhysicalContent.Read(room,layout.level),RoomDoorContent.Read(room,layout.level).doors,layout.actors,"продолжение27/связный уровень");
            if(decisionsInstalled)
            {
                var report=room.GetComponent<RoomImportAudit>().Report;
                Require(report.Records.Count(r=>r.installed)==40 && report.Records.Where(r=>!r.installed).Select(r=>r.kind).OrderBy(x=>x).SequenceEqual(new[]{"enl1","rat","term3","work"}),"Не точная исходная стадия40installed/4pending.");
            }
        }
        private static void CheckPersistence(NpcLayout layout,NpcLayout other)
        {
            var world=new WorldState {worldId="continuation-npc-proof"};Require(NpcContentInstaller.Apply(world,layout) && world.actors.Count==13,"Не установлены13 NPC нового мира.");
            Require(world.actors.All(a=>a.inventory.Count==1 && a.inventory[0].definition=="caps" && a.inventory[0].count is >=1 and <=2),"Изменена существующая таблица инвентаря SO.");
            string initial=StateJson.ToJson(world);Require(!NpcContentInstaller.Apply(world,layout) && StateJson.ToJson(world)==initial,"Повторная установка NPC изменила мир.");
            var retained=StateJson.Copy(world.actors[0]);retained.owner="saved-owner";retained.generation=5;retained.lifecycleSequence=3;retained.x+=.31f;retained.vx=.1f;retained.pose="jump";retained.poseTime=.2f;
            retained.health=0;retained.down=true;retained.inventory.Clear();retained.resolvedAttacks.Add("saved-attack");
            retained.extensions??=new Dictionary<string,JToken>();retained.extensions["unknown"]=JToken.Parse("{\"nested\":[1,2]}");
            var prior=new WorldState {worldId=world.worldId};prior.actors.Add(retained);string retainedJson=StateJson.ToJson(retained);
            Require(NpcContentInstaller.Apply(prior,layout) && ReferenceEquals(retained,prior.Find(retained.id)) && StateJson.ToJson(retained)==retainedJson,"Установка заменила сохранённое тело/владельца/лут/неизвестные поля.");
            string removed=prior.actors[^1].id;prior.actors.RemoveAt(prior.actors.Count-1);prior=StateJson.Copy(prior);initial=StateJson.ToJson(prior);
            Require(!NpcContentInstaller.Apply(prior,layout) && prior.Find(removed)==null && StateJson.ToJson(prior)==initial,"Повторная загрузка восстановила удалённого NPC/пополнила инвентарь тела.");
            NpcContentInstaller.Apply(world,other);Require(world.actors.Count==26 && world.actors.Select(a=>a.id).Distinct().Count()==26 &&
                world.actors.SelectMany(a=>a.inventory).Select(i=>i.id).Distinct().Count()==26,"Фабрики используют общие ID актёров/лута.");
            var body=world.actors[0];body.health=0;body.down=true;var item=body.inventory.Single();var player=new ActorState {id="corpse-recipient",player=true,connected=true,level=body.level,Position=body.Position};world.actors.Add(player);
            var shared=new SharedInventory(world);string bodyId=InventorySource.BodyId(body.id);
            Require(ReferenceEquals(InventorySource.Resolve(world,bodyId).items,body.inventory) && shared.PrepareClaim("corpse-proof",bodyId,item.id,player) && player.inventory.Count==0 &&
                !shared.PrepareClaim("corpse-competing",bodyId,item.id,player),"Инвентарь тела дублирован или выдан до подтверждения.");
            world=StateJson.Copy(world);shared=new SharedInventory(world);var grant=world.pendingGrants.Single();shared.CommitGrant(grant);shared.CommitGrant(grant);
            Require(world.Find(player.id).inventory.Single().id==item.id && world.Find(body.id).inventory.Count==0,"Выдача из тела не сохранила дедупликацию.");
        }
        private static void CheckDeferredConflict(RoomAuthoring room)
        {
            var spawn=FactoryContinuationObjectsAuthoring.Load<NpcSpawnAsset>(FactoryContinuationNpcAuthoring.SpawnPath);
            var anchor=FactorySourceCoordinates.Placement(20,15,1);
            var actor=spawn.Create(new TrainingPlacement {id=FactoryContinuationObjectsAuthoring.Prefix+FactoryContinuationNpcAuthoring.DeferredCode,type="radroach",x=anchor.x,y=anchor.y,facing=-1},"factory:0");
            var physical=RoomPhysicalContent.Read(room,actor.level);var couch=physical.props.Single(p=>p.definition=="couch");var hull=Anatomy.Movement(actor);
            Require(Vector2.Distance(anchor,new Vector2(16.4f,7.2f))<.00001f && Vector2.Distance(couch.Position,new Vector2(16,7.2f))<.00001f && couch.width==1.6f && couch.height==.4f && couch.anchorHeight==0 &&
                new Rect(actor.x-hull.width*.5f,actor.y,hull.width,hull.height).Overlaps(new Rect(couch.x-couch.width*.5f,couch.y-couch.anchorHeight,couch.width,couch.height)),
                "Изменились подтверждённые исходные позиции/тела конфликта NPC64 и дивана33.");
            Reject(()=>FixedEncounterGeometryChecks.Validate(FactoryContinuationConnectionGeometry.InModule(room),physical,RoomDoorContent.Read(room,actor.level).doors,new[]{actor},"отложенная запись64"),"Конфликт NPC64 с диваном33 оказался скрыт.");
            Require(FactoryContinuationNpcAuthoring.Snapshot(room).Contains(FactoryContinuationNpcAuthoring.DeferredCode),"Отложенные metadata64 исчезли из полного snapshot.");
        }
        private static void CheckGeometryNegatives(RoomAuthoring room,NpcLayout layout)
        {
            var geometry=FactoryContinuationConnectionGeometry.InModule(room);var physical=RoomPhysicalContent.Read(room,layout.level);var doors=RoomDoorContent.Read(room,layout.level).doors;var actor=layout.actors[0];
            var blocked=StateJson.Copy(physical);var prop=blocked.props[0];prop.x=actor.x;prop.y=actor.y;prop.anchorHeight=0;prop.width=.4f;prop.height=.4f;prop.mass=1;prop.hasPhysicalBody=true;prop.movable=true;
            Reject(()=>FixedEncounterGeometryChecks.Validate(geometry,blocked,doors,layout.actors,"negative cargo"),"Пересечение NPC с грузом не отвергнуто.");
            var absent=StateJson.Copy(geometry);absent.platforms=Array.Empty<RoomSurface>();
            Reject(()=>FixedEncounterGeometryChecks.Validate(absent,physical,doors,layout.actors,"negative support"),"Отсутствие фактической опоры не отвергнуто.");
            var obstruction=StateJson.Copy(doors);obstruction[0].x=actor.x-.1f;obstruction[0].y=actor.y;obstruction[0].width=.2f;obstruction[0].height=.4f;obstruction[0].open=false;
            Reject(()=>FixedEncounterGeometryChecks.Validate(geometry,physical,obstruction,layout.actors,"negative closed door"),"Пересечение закрытой преграды не отвергнуто.");
        }
        private static void Reject(Action action,string reason) {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,reason);}
        private static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException("Проверка NPC27: "+reason);}
    }
}
