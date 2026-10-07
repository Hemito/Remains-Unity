using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWarehouseConnectionChecks
    {
        public static void Run()=>Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        public static void Check(RoomAuthoring room)
        {
            var modules=room.GetComponentsInChildren<RoomAuthoring>(true);
            var warehouse=modules.Single(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:40");
            var paths=modules.Where(r=>r!=room).Select(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
            Require(modules.Length==5 && paths.SequenceEqual(new[]{FactoryLadderLandingAuthoring.EntryPath,FactoryWorkshopDraft.Path,FactoryOfficeDraft.Path,FactoryWarehouseDraft.Path}.OrderBy(id=>id,StringComparer.Ordinal)),
                "Изменён точный список четырёх сохранённых модулей: "+string.Join(", ",paths));
            var layout=room.CreateLayout();var data=FactoryWarehouseConnectionAuthoring.Data();
            Require(Mathf.Abs(layout.right-153.6f)<.001f && layout.top==20 && warehouse.transform.localPosition==new Vector3(115.2f,0,0) &&
                warehouse.transform.localScale==Vector3.one && warehouse.transform.localRotation==Quaternion.identity,"Изменены размещение/границы склада.");
            FactoryWarehouseChecks.Check(warehouse);FactoryWarehouseNpcChecks.Check(warehouse);
            foreach(var pair in new[]{(FactoryWarehouseConnectionAuthoring.Connector,new[]{data.connector}),(FactoryWarehouseConnectionAuthoring.Frame,data.seamClosures)})
            {
                var group=room.transform.Find(pair.Item1);Require(group!=null,"Нет сохранённой группы стыка: "+pair.Item1);
                var surfaces=group.GetComponentsInChildren<RoomSurfaceAuthoring>();
                Require(surfaces.Length==pair.Item2.Length && surfaces.All(s=>pair.Item2.Count(e=>FactoryLadderLandingChecks.SameSurface(s.Read(room),e))==1),"Изменена рамка выбранного стыка.");
            }
            Require(data.connector.x==115.2f && data.connector.y==6.4f && data.connector.width==.8f && data.connector.height==.8f &&
                data.seamClosures.Any(s=>s.y==.8f && s.height==5.6f) && data.seamClosures.Any(s=>s.y==9.6f && s.height==9.6f),"JSON стыка расходится с проверенной исходной опорой/проходом.");
            FactoryBoundaryAuthoring.ValidateDerivedContour(room);
            foreach(float width in new[]{.2f,1.6f}) foreach(int direction in new[]{-1,1})
            {
                var hull=new MovementHull {width=width,height=2};var p=new Vector2(direction>0?113.8f:118,7.2f);
                for(int i=0;i<84;i++)
                {
                    p=ActorTerrainMotion.Move(layout,hull,p,new Vector2(direction*.05f,-.01f),false);
                    Require(Mathf.Abs(p.y-7.2f)<.001f && ActorTerrainMotion.Supported(layout,hull,p) && ActorTerrainMotion.IsClear(layout,hull,p),"Стык офис→склад блокирует тело или теряет опору.");
                }
                Require(direction>0?p.x>117.9f:p.x<113.9f,"Тело не пересекло стык в обе стороны.");
            }
            var batch=RoomContentBatch.Read(room,"factory:0");
            bool furniture=batch.physical.patch=="factory:0:"+FactoryWarehouseFurnitureAuthoring.ConnectedPatch;
            FactoryWarehouseFurnitureAuthoring.BeforeFurniture(batch.physical);
            bool metal=batch.doors.patch=="factory:0:"+FactoryWarehouseMetalDoorsPlacement.ConnectedPatch;
            Require(batch.doors.patch=="factory:0:"+(metal?FactoryWarehouseMetalDoorsPlacement.ConnectedPatch:FactoryWarehouseConnectionAuthoring.DoorPatch),"Неизвестная стадия дверей связного склада.");
            if(metal) FactoryWarehouseMetalDoorsPlacement.VerifyRoom(room);
            Require(batch.physical.props.Length==(furniture?30:29) && batch.physical.containers.Length==(furniture?55:52) && batch.physical.loot.Length==(furniture?53:50) &&
                batch.doors.doors.Length==(metal?19:17) && batch.npcs.actors.Length==22 && batch.npcs.loot.Length==22 && batch.travelPoints.Length==1 &&
                batch.mechanisms.movingPlatforms.Length==1 && batch.mechanisms.mechanismButtons.Length==2,"Потерян точный полный контент четырёх модулей.");
            FactoryWarehouseHistoryProjection.BeforeWarehouse(batch.physical);FactoryWarehouseHistoryProjection.BeforeWarehouse(batch.doors);
            FactoryWarehouseHistoryProjection.BeforeWarehouse(batch.npcs);
            CheckPreservation(batch);
            var separate=new WorldState {worldId="warehouse-signed-instances",mechanismVersion=1};
            foreach(string level in new[]{"factory:0","factory:6","factory:-27"}) RoomContentInstaller.Apply(separate,RoomContentBatch.Read(room,level));
            var ids=separate.props.Select(p=>p.id).Concat(separate.containers.Select(c=>c.id)).Concat(separate.doors.Select(d=>d.id)).Concat(separate.actors.Select(a=>a.id)).ToArray();
            int expectedIds=(metal?366:360)+(furniture?12:0);
            Require(ids.Length==expectedIds && ids.Distinct(StringComparer.Ordinal).Count()==expectedIds && separate.containers.Count==(furniture?165:156) && separate.actors.Count==66,"Фабрики0/6/−27 разделяют ID или не получают точный полный набор.");
            Debug.Log($"Склад40: PASS — четыре модуля, стык в обе стороны, замкнутый контур, {batch.physical.props.Length} props/{batch.physical.containers.Length} containers/{(metal?19:17)} doors/22 NPC; исторические версии без сброса и три независимых индекса.");
        }
        private static void CheckPreservation(RoomContentBatch batch)
        {
            ContentExpansionStep[] Steps(ContentExpansion expansion)=>new[]{(ContentExpansionStep)expansion}.Concat(expansion.earlierVersions).ToArray();
            var physical=Steps(batch.physical.expansion);var doors=Steps(batch.doors.expansion);var npcs=Steps(batch.npcs.expansion);
            // Each family's complete history is tested independently; other families already have their current patch.
            foreach(int family in new[]{0,1,2}) foreach(var step in family==0?physical:family==1?doors:npcs)
            {
                var world=new WorldState {worldId="warehouse-history-"+step.previousPatch,mechanismVersion=1};RoomContentInstaller.Apply(world,batch);
                string patch=family==0?batch.physical.patch:family==1?batch.doors.patch:batch.npcs.patch;
                world.contentPatches.Remove(patch);world.contentPatches.Add(step.previousPatch);
                var added=step.additions.ToHashSet(StringComparer.Ordinal);
                if(family==0) {world.props.RemoveAll(p=>added.Contains(p.id));world.containers.RemoveAll(p=>added.Contains(p.id));}
                if(family==1) world.doors.RemoveAll(p=>added.Contains(p.id));
                if(family==2) world.actors.RemoveAll(p=>added.Contains(p.id));
                world.props.RemoveAt(0);world.containers.RemoveAt(0);world.doors.RemoveAt(0);if(world.actors.Count>1) world.actors.RemoveAt(0);
                world.props[0].x+=.25f;world.props[0].held=true;world.props[0].owner="saved-cargo-owner";world.props[0].generation=8;
                foreach(var container in world.containers) {container.items.Clear();container.appearance.opened=true;container.appearance.revision=1;}
                world.doors[0].open=true;world.doors[0].revision=17;
                var dead=world.actors[0];dead.health=0;dead.down=true;dead.inventory.Clear();dead.owner="saved-npc-owner";dead.generation=9;
                dead.extensions=new System.Collections.Generic.Dictionary<string,JToken> {{"modProgress",JToken.Parse("{\"nested\":[19,\"смерть сохранена\"]}")}};
                var oldProps=world.props.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                var oldContainers=world.containers.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                var oldDoors=world.doors.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                var oldActors=world.actors.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                string mechanisms=StateJson.ToJson(world.movingPlatforms);
                int count=world.props.Count+world.containers.Count+world.doors.Count+world.actors.Count;
                world=StateJson.Copy(world);Require(RoomContentInstaller.Apply(world,batch),"Старая версия не получила склад: "+step.previousPatch);
                Require(world.props.Count+world.containers.Count+world.doors.Count+world.actors.Count==count+added.Count &&
                    oldProps.All(p=>world.props.Any(s=>s.id==p.id && StateJson.ToJson(s)==p.json)) &&
                    oldContainers.All(p=>world.containers.Any(s=>s.id==p.id && StateJson.ToJson(s)==p.json)) &&
                    oldDoors.All(p=>world.doors.Any(s=>s.id==p.id && StateJson.ToJson(s)==p.json)) &&
                    oldActors.All(p=>world.actors.Any(s=>s.id==p.id && StateJson.ToJson(s)==p.json)) && StateJson.ToJson(world.movingPlatforms)==mechanisms,
                    "Дополнение сбросило лут/груз/открытый вид/смерть/ownership/unknown или воскресило удалённый объект: "+step.previousPatch);
                string before=StateJson.ToJson(world);Require(!RoomContentInstaller.Apply(world,batch) && StateJson.ToJson(world)==before,"Повторный вход изменил сохранённый мир.");
            }
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Подключение склада40: "+reason);}
    }
}
