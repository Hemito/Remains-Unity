using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWarehouseHistoryProjectionChecks
    {
        public static void Run()
        {
            var room=RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId);int checks=0;
            foreach(string level in new[]{"factory:0","factory:6","factory:-27"})
                checks+=Check(RoomContentBatch.Read(room,level));
            Debug.Log($"Исторические проверки склада40: PASS — {checks} проверок; source27 v9/v6/v6→v8/v5/v5 только точные новые ID, затем прежние мебель/metal/склад цепочки, независимые копии, неизвестные поля и отрицательные случаи. Нативный маршрут этим сообщением не подтверждается.");
        }
        // Explicit layouts make the same negatives runnable without Unity import or game processes.
        public static int Check(RoomContentBatch source,string projectRoot=null)
        {
            int checks=CheckContinuation(source,projectRoot);
            source=FactoryWarehouseHistoryProjection.BeforeContinuation(source,projectRoot);
            source=FactoryWarehouseFurnitureAuthoring.BeforeFurniture(source,projectRoot);
            void Require(bool value,string reason) {checks++;if(!value) throw new InvalidOperationException("Проекция склада40: "+reason);}
            void Reject(Action action,string reason)
            {bool rejected=false;try {action();} catch(InvalidOperationException) {rejected=true;}Require(rejected,"Не отклонено: "+reason);}
            if(source.doors.patch==source.doors.level+":"+FactoryWarehouseMetalDoorsPlacement.ConnectedPatch)
            {
                string input=StateJson.ToJson(source);var normalized=FactoryWarehouseMetalDoorsPlacement.BeforeMetalDoors(source.doors);
                var added=FactoryWarehouseMetalDoorsPlacement.NewIds(source.doors.level);
                Require(normalized.doors.Length==17 && StateJson.ToJson(normalized.doors)==StateJson.ToJson(source.doors.doors.Where(d=>!added.Contains(d.id)).ToArray()),
                    "Metal projection изменила одну из прежних17дверей.");
                Require(StateJson.ToJson(source)==input,"Новая проекция изменила входной пакет.");
                var invalid=StateJson.Copy(source.doors);invalid.doors=invalid.doors.Where(d=>d.id!=added[0]).ToArray();
                Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(invalid,projectRoot),"неполная metal группа");
                invalid=StateJson.Copy(source.doors);invalid.doors.Single(d=>d.id==added[0]).id=source.doors.level+":unknown-metal";
                Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(invalid,projectRoot),"подменённый metal ID при19дверях");
                invalid=StateJson.Copy(source.doors);invalid.doors.Single(d=>d.id==added[0]).definition="door2";
                Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(invalid,projectRoot),"legacy профиль вместо нового metal");
                invalid=StateJson.Copy(source.doors);invalid.expansion.earlierVersions[0].additions=invalid.expansion.earlierVersions[0].additions.Where(id=>id!=added[0]).ToArray();
                Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(invalid,projectRoot),"metal ID потерян в cumulative history");
                invalid=StateJson.Copy(source.doors);invalid.patch=invalid.level+":"+FactoryWarehouseConnectionAuthoring.DoorPatch;
                Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(invalid,projectRoot),"metal ID скрыт в прежнем root patch");
                source=StateJson.Copy(source);source.doors=normalized;
                Require(StateJson.ToJson(FactoryWarehouseMetalDoorsPlacement.BeforeMetalDoors(normalized))==StateJson.ToJson(normalized),"Повтор metal projection изменила v4.");
            }
            string before=StateJson.ToJson(source);
            var result=FactoryWarehouseHistoryProjection.BeforeWarehouse(source,projectRoot);
            string prefix=source.physical.level+":original:plant:40:";
            bool Retained(string id)=>!id.StartsWith(prefix,StringComparison.Ordinal);
            Require(source.physical.patch==source.physical.level+":"+FactoryWarehouseConnectionAuthoring.PhysicalPatch &&
                source.doors.patch==source.doors.level+":"+FactoryWarehouseConnectionAuthoring.DoorPatch &&
                source.npcs.patch==source.npcs.level+":"+FactoryWarehouseConnectionAuthoring.NpcPatch,"Проверяется не полная новая стадия.");
            Require(StateJson.ToJson(source)==before,"Входные данные изменены.");
            Require(StateJson.ToJson(result.physical.props)==StateJson.ToJson(source.physical.props.Where(p=>Retained(p.id)).ToArray()) &&
                StateJson.ToJson(result.physical.containers)==StateJson.ToJson(source.physical.containers.Where(p=>Retained(p.id)).ToArray()) &&
                StateJson.ToJson(result.physical.loot)==StateJson.ToJson(source.physical.loot.Where(b=>Retained(b.container)).ToArray()),"Прежняя физическая расстановка/таблицы изменены.");
            Require(StateJson.ToJson(result.doors.doors)==StateJson.ToJson(source.doors.doors.Where(d=>Retained(d.id)).ToArray()) &&
                StateJson.ToJson(result.npcs.actors)==StateJson.ToJson(source.npcs.actors.Where(a=>Retained(a.id)).ToArray()) &&
                StateJson.ToJson(result.npcs.loot)==StateJson.ToJson(source.npcs.loot.Where(b=>Retained(b.actor)).ToArray()),"Прежние двери/NPC/инвентари изменены.");
            Require(StateJson.ToJson(result.mechanisms)==StateJson.ToJson(source.mechanisms) && StateJson.ToJson(result.travelPoints)==StateJson.ToJson(source.travelPoints),"Механизмы/переходы изменены.");
            Require(StateJson.ToJson(FactoryWarehouseHistoryProjection.BeforeWarehouse(result,projectRoot))==StateJson.ToJson(result),"Повторная проекция изменила прежнюю стадию.");
            result.physical.containers[0].items.Add(new ItemStack {id="detached-check",definition="caps",count=1});
            result.doors.doors[0].revision++;result.npcs.actors[0].generation++;
            Require(StateJson.ToJson(source)==before,"Выходная копия разделяет ссылки с источником.");
            var physical=StateJson.Copy(source.physical);
            physical.containers=physical.containers.Where(c=>!c.id.StartsWith(prefix,StringComparison.Ordinal)).Concat(
                source.physical.containers.Where(c=>c.id.StartsWith(prefix,StringComparison.Ordinal)).Skip(1)).ToArray();
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"неполное дополнение контейнеров");
            physical=StateJson.Copy(source.physical);physical.props.First(p=>p.id.StartsWith(prefix,StringComparison.Ordinal)).id=prefix+"unknown";
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"неизвестный ID при прежнем count");
            physical=StateJson.Copy(source.physical);physical.loot[0]=StateJson.Copy(physical.loot[1]);
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"повтор таблицы при прежнем count");
            physical=StateJson.Copy(source.physical);var prop=physical.props.First(p=>p.id.StartsWith(prefix,StringComparison.Ordinal));
            var container=physical.containers.First(c=>c.id.StartsWith(prefix,StringComparison.Ordinal));string id=prop.id;prop.id=container.id;container.id=id;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"перестановка категорий prop/container");
            physical=StateJson.Copy(source.physical);physical.patch=physical.level+":"+FactoryOfficeFurnitureAuthoring.ConnectedPatch;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"скрытая частичная установка в старом patch");
            physical=StateJson.Copy(source.physical);physical.expansion.earlierVersions[0].additions[0]=physical.props.First(p=>Retained(p.id)).id;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"подменённый cumulative ID при прежнем count");
            physical=StateJson.Copy(source.physical);physical.expansion.earlierVersions.Last().additions=physical.expansion.earlierVersions.Last().additions.Skip(1).ToArray();
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"неполный старейший v1 шаг");
            physical=StateJson.Copy(source.physical);physical.expansion.earlierVersions[1].previousPatch=physical.expansion.earlierVersions[0].previousPatch;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"повтор/перестановка версии");
            physical=StateJson.Copy(source.physical);physical.props.First(p=>p.id.StartsWith(prefix,StringComparison.Ordinal)).definition="other-type";
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(physical,projectRoot),"подменённый source type");
            var doors=StateJson.Copy(source.doors);doors.doors.First(d=>d.id.StartsWith(prefix,StringComparison.Ordinal)).id=prefix+"unknown";
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(doors,projectRoot),"подменённая дверь при прежнем count");
            doors=StateJson.Copy(source.doors);doors.expansion.earlierVersions.Last().additions=doors.expansion.earlierVersions.Last().additions.Skip(1).ToArray();
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(doors,projectRoot),"неполная старая door chain");
            doors=StateJson.Copy(source.doors);doors.patch=doors.level+":"+FactoryOfficeOpeningsAuthoring.ConnectedPatch;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(doors,projectRoot),"складские двери в прежнем patch");
            var npcs=StateJson.Copy(source.npcs);npcs.actors.First(a=>a.id.StartsWith(prefix,StringComparison.Ordinal)).id=prefix+"unknown";
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(npcs,projectRoot),"подменённый NPC при прежнем count");
            npcs=StateJson.Copy(source.npcs);npcs.loot[0]=StateJson.Copy(npcs.loot[1]);
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(npcs,projectRoot),"повтор NPC inventory binding");
            npcs=StateJson.Copy(source.npcs);npcs.expansion.earlierVersions.Last().additions=npcs.expansion.earlierVersions.Last().additions.Skip(1).ToArray();
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(npcs,projectRoot),"неполная старая NPC chain");
            npcs=StateJson.Copy(source.npcs);npcs.patch=npcs.level+":"+FactoryOfficeNpcAuthoring.ConnectedPatch;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeWarehouse(npcs,projectRoot),"складские NPC в прежнем patch");
            var progressed=StateJson.Copy(source);
            var saved=progressed.physical.containers.First(c=>Retained(c.id));saved.items.Clear();saved.appearance=new ContainerAppearanceState {version=1,opened=true,revision=1};
            saved.held=true;saved.owner="saved-owner";saved.generation=7;saved.x+=.17f;
            saved.items.Add(new ItemStack {id="saved-deposit",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=1,revision=13},
                extensions=new System.Collections.Generic.Dictionary<string,JToken>{{"mod:item",JToken.Parse("{\"nested\":[1,2,3]}")}}});
            saved.itemAliases["saved-alias"]="saved-deposit";saved.extensions=new System.Collections.Generic.Dictionary<string,JToken>{{"mod:container",new JValue("retained")}};
            var body=progressed.npcs.actors.First(a=>Retained(a.id));body.health=0;body.down=true;body.owner="saved-body-owner";body.generation=11;body.lifecycleSequence=8;body.inventory.Clear();
            string savedContainer=StateJson.ToJson(saved),savedBody=StateJson.ToJson(body);
            var projected=FactoryWarehouseHistoryProjection.BeforeWarehouse(progressed,projectRoot);
            Require(StateJson.ToJson(projected.physical.containers.Single(c=>c.id==saved.id))==savedContainer &&
                StateJson.ToJson(projected.npcs.actors.Single(a=>a.id==body.id))==savedBody,"Проекция пополнила/закрыла/переместила/воскресила сохранённый объект или потеряла unknown fields.");
            return checks;
        }
        private static int CheckContinuation(RoomContentBatch source,string projectRoot)
        {
            if(source.physical.patch!=source.physical.level+":"+FactoryContinuationConnectionAuthoring.PhysicalPatch)
            {FactoryWarehouseHistoryProjection.BeforeContinuation(source,projectRoot);return 0;}
            int checks=0;string before=StateJson.ToJson(source),prefix=source.physical.level+":original:plant:27:";
            bool Retained(string id)=>!id.StartsWith(prefix,StringComparison.Ordinal);
            void Require(bool value,string reason) {checks++;if(!value)throw new InvalidOperationException("Проекция27: "+reason);}
            void Reject(Action action,string reason)
            {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,"Не отклонено: "+reason);}
            var result=FactoryWarehouseHistoryProjection.BeforeContinuation(source,projectRoot);
            Require(StateJson.ToJson(source)==before,"Проекция изменила исходный полный пакет.");
            Require(result.physical.props.Length==30 && result.physical.containers.Length==55 && result.physical.loot.Length==53 &&
                result.doors.doors.Length==19 && result.npcs.actors.Length==22 && result.npcs.loot.Length==22,"Не восстановлена точная прежняя стадия r58.");
            Require(StateJson.ToJson(result.physical.props)==StateJson.ToJson(source.physical.props.Where(p=>Retained(p.id)).ToArray()) &&
                StateJson.ToJson(result.physical.containers)==StateJson.ToJson(source.physical.containers.Where(c=>Retained(c.id)).ToArray()) &&
                StateJson.ToJson(result.physical.loot)==StateJson.ToJson(source.physical.loot.Where(b=>Retained(b.container)).ToArray()) &&
                StateJson.ToJson(result.doors.doors)==StateJson.ToJson(source.doors.doors.Where(d=>Retained(d.id)).ToArray()) &&
                StateJson.ToJson(result.npcs.actors)==StateJson.ToJson(source.npcs.actors.Where(a=>Retained(a.id)).ToArray()) &&
                StateJson.ToJson(result.npcs.loot)==StateJson.ToJson(source.npcs.loot.Where(b=>Retained(b.actor)).ToArray()),"Удаление source27 изменило прежние объекты/таблицы/порядок.");
            Require(StateJson.ToJson(result.mechanisms)==StateJson.ToJson(source.mechanisms) && StateJson.ToJson(result.travelPoints)==StateJson.ToJson(source.travelPoints),"Проекция27 изменила механизм/выход.");
            Require(StateJson.ToJson(FactoryWarehouseHistoryProjection.BeforeContinuation(result,projectRoot))==StateJson.ToJson(result),"Повторная проекция27 изменила predecessor.");
            result.physical.props[0].x+=.1f;result.physical.containers[0].items.Add(new ItemStack {id="projection27-copy",definition="caps",count=1});
            result.doors.doors[0].revision++;result.npcs.actors[0].generation++;
            Require(StateJson.ToJson(source)==before,"Source27 projection оставила общие изменяемые ссылки.");
            var invalid=StateJson.Copy(source);invalid.physical.props=invalid.physical.props.Where(p=>Retained(p.id)).Concat(invalid.physical.props.Where(p=>!Retained(p.id)).Skip(1)).ToArray();
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"неполная source27 группа");
            invalid=StateJson.Copy(source);invalid.doors.doors.First(d=>!Retained(d.id)).id=prefix+"unknown";
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"чужой door ID при24дверях");
            invalid=StateJson.Copy(source);invalid.npcs.actors.First(a=>!Retained(a.id)).id=prefix+"unknown";
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"чужой NPC ID при35телах");
            invalid=StateJson.Copy(source);var replaced=invalid.npcs.actors.First(a=>!Retained(a.id));string originalId=replaced.id;
            replaced.id=prefix+"icyxG67X0pCmenkm";invalid.npcs.loot.Single(b=>b.actor==originalId).actor=replaced.id;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"source64 скрыт вместо одного из13разрешённыхNPC при прежнем count");
            invalid=StateJson.Copy(source);var prop=invalid.physical.props.First(p=>!Retained(p.id));var container=invalid.physical.containers.First(c=>!Retained(c.id));
            string id=prop.id;prop.id=container.id;container.id=id;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"подмена prop/container при прежних counts");
            invalid=StateJson.Copy(source);invalid.physical.loot[0]=StateJson.Copy(invalid.physical.loot[1]);
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"повтор loot binding");
            invalid=StateJson.Copy(source);invalid.physical.expansion.additions[0]=invalid.physical.props.First(p=>Retained(p.id)).id;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"чужой latest addition вместо source27");
            invalid=StateJson.Copy(source);invalid.npcs.expansion.earlierVersions[0].additions=invalid.npcs.expansion.earlierVersions[0].additions.Where(a=>a!=source.npcs.actors.First(a=>!Retained(a.id)).id).ToArray();
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"утерян source27 в cumulative шагах");
            invalid=StateJson.Copy(source);invalid.doors.expansion.earlierVersions.Last().previousPatch=invalid.doors.expansion.earlierVersions[0].previousPatch;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"повтор старой версии после retire27");
            invalid=StateJson.Copy(source);invalid.physical.patch=result.physical.patch;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"полная source27 группа под старой версией");
            invalid=StateJson.Copy(source);invalid.doors=null;
            Reject(()=>FactoryWarehouseHistoryProjection.BeforeContinuation(invalid,projectRoot),"частичный полный набор без семейства дверей");
            var progressed=StateJson.Copy(source);
            var cargo=progressed.physical.props.First(p=>Retained(p.id));cargo.x+=.27f;cargo.held=true;cargo.owner="saved-owner";cargo.generation=9;cargo.extensions=Extensions("cargo");
            var box=progressed.physical.containers.First(c=>Retained(c.id));box.items.Clear();box.appearance=new ContainerAppearanceState {version=1,opened=true,revision=8};box.extensions=Extensions("empty");
            box.items.Add(new ItemStack {id="mod:deposit27",definition="caps",count=2,extensions=Extensions("item")});box.itemAliases["old:deposit27"]="mod:deposit27";
            var door=progressed.doors.doors.First(d=>Retained(d.id));door.open=true;door.revision=19;door.extensions=Extensions("door");
            var body=progressed.npcs.actors.First(a=>Retained(a.id));body.health=0;body.down=true;body.lifecycleSequence=7;body.generation=11;body.owner="saved-owner";body.inventory.Clear();body.extensions=Extensions("body");
            var saved=new[]{StateJson.ToJson(cargo),StateJson.ToJson(box),StateJson.ToJson(door),StateJson.ToJson(body)};
            var projected=FactoryWarehouseHistoryProjection.BeforeContinuation(progressed,projectRoot);
            Require(StateJson.ToJson(projected.physical.props.Single(p=>p.id==cargo.id))==saved[0] &&
                StateJson.ToJson(projected.physical.containers.Single(c=>c.id==box.id))==saved[1] &&
                StateJson.ToJson(projected.doors.doors.Single(d=>d.id==door.id))==saved[2] && StateJson.ToJson(projected.npcs.actors.Single(a=>a.id==body.id))==saved[3],
                "Проекция27 изменила saved cargo/empty/deposit/alias/open/dead/ownership/lifecycle/unknown.");
            return checks;
        }
        private static System.Collections.Generic.Dictionary<string,JToken> Extensions(string tag)=>new()
            {{"mod:projection27",JObject.Parse("{\"tag\":\""+tag+"\",\"nested\":[17,true,1.2345678901234567890123456789]}")}};
    }
}
