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
            Debug.Log($"Исторические проверки склада40: PASS — {checks} проверок точных ID, v7/v4/v5→v6/v3/v4, полных cumulative chains, независимых копий и отрицательных случаев. Нативный маршрут и новые игровые события этим сообщением не подтверждаются.");
        }
        // Explicit layouts make the same negatives runnable without Unity import or game processes.
        public static int Check(RoomContentBatch source,string projectRoot=null)
        {
            int checks=0;
            void Require(bool value,string reason) {checks++;if(!value) throw new InvalidOperationException("Проекция склада40: "+reason);}
            void Reject(Action action,string reason)
            {bool rejected=false;try {action();} catch(InvalidOperationException) {rejected=true;}Require(rejected,"Не отклонено: "+reason);}
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
    }
}
