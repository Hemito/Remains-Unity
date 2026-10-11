using System;
using System.Linq;
using Remains.Content;
using Remains.Runtime;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ConnectedFactoryChecks
    {
        public static void Run()=>Check(RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId));
        public static void Check(RoomAuthoring room)
        {
            var layout=room.CreateLayout();var batch=RoomContentBatch.Read(room,"factory:0");
            var modules=room.GetComponentsInChildren<RoomAuthoring>(true);
            var office=modules.SingleOrDefault(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53");
            var warehouse=modules.SingleOrDefault(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:40");
            var continuation=modules.SingleOrDefault(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:27");
            Require((warehouse==null || office!=null) && (continuation==null || warehouse!=null),"Потерян предыдущий модуль последовательного завода.");
            var paths=modules.Where(r=>r!=room).Select(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)).OrderBy(p=>p,StringComparer.Ordinal);
            var expectedPaths=new[]{FactoryLadderLandingAuthoring.EntryPath,FactoryWorkshopDraft.Path}
                .Concat(office==null?Array.Empty<string>():new[]{FactoryOfficeDraft.Path})
                .Concat(warehouse==null?Array.Empty<string>():new[]{FactoryWarehouseDraft.Path})
                .Concat(continuation==null?Array.Empty<string>():new[]{FactoryContinuationDraft.Path}).OrderBy(p=>p,StringComparer.Ordinal);
            Require(paths.SequenceEqual(expectedPaths),"Изменён точный каталог вложенных source-модулей.");
            var continuationLayout=continuation?.CreateLayout();var continuationBatch=continuation==null?null:RoomContentBatch.Read(continuation,"factory:0");
            if(continuation!=null)
            {FactoryContinuationChecks.Check(continuation);FactoryContinuationObjectsChecks.Check(continuation);FactoryContinuationNpcChecks.Check(continuation);FactoryWarehouseHistoryProjection.BeforeContinuation(batch);}
            var warehouseLayout=warehouse?.CreateLayout();var warehouseBatch=warehouse==null?null:RoomContentBatch.Read(warehouse,"factory:0");
            if(warehouse!=null) {FactoryWarehouseChecks.Check(warehouse);FactoryWarehouseNpcChecks.Check(warehouse);}
            var officeLayout=office?.CreateLayout();var officeBatch=office==null?null:RoomContentBatch.Read(office,"factory:0");
            if(office!=null) FactoryOfficeLootChecks.CheckOfficeStage(office);
            int officeSurfaces=officeLayout?.platforms.Length??0,officeArt=officeLayout?.artwork.Length??0,officeLadders=officeLayout?.ladders.Length??0;
            int connector=(office==null?0:3)+(warehouse==null?0:3)+(continuation==null?0:4);
            int exterior=FactoryBoundaryChecks.Check(room);
            int crawlers=FactoryContentCheckFixtures.BeforeOffice(batch.npcs).actors.Count(a=>a.actorType=="radroach");
            int officeNpcs=officeBatch?.npcs?.actors.Length??0;int warehouseNpcs=warehouseBatch?.npcs?.actors.Length??0;
            Require(officeNpcs is 0 or 5,"Встреча офиса установлена частично.");
            int mechanisms=(batch.mechanisms?.movingPlatforms.Length??0)+(batch.mechanisms?.mechanismButtons.Length??0);
            Require(mechanisms is 0 or 3,"Расстановка платформы/кнопок установлена частично.");
            Require(crawlers==0 || crawlers==2,"Расстановка радтараканов установлена частично.");
            int landings=FactoryLadderLandingChecks.CheckConnected(room,false);
            int officeLandings=office?.GetComponentsInChildren<RoomLadderLandingAuthoring>(true).Length??0;
            int warehouseLandings=warehouse?.GetComponentsInChildren<RoomLadderLandingAuthoring>(true).Length??0;
            int continuationLandings=continuation?.GetComponentsInChildren<RoomLadderLandingAuthoring>(true).Length??0;
            Require(Mathf.Abs(layout.right-(continuation!=null?FactoryContinuationConnectionAuthoring.Right:warehouse!=null?FactoryWarehouseConnectionAuthoring.Right:office==null?76.8f:FactoryOfficeConnectionAuthoring.Right))<.001f && layout.top==20 &&
                layout.platforms.Length==127+exterior+landings+officeSurfaces-officeLandings+(warehouseLayout?.platforms.Length??0)-warehouseLandings+(continuationLayout?.platforms.Length??0)-continuationLandings+connector && layout.ladders.Length==8+officeLadders+(warehouseLayout?.ladders.Length??0)+(continuationLayout?.ladders.Length??0) && layout.artwork.Length==325+exterior+landings+officeArt-officeLandings+(warehouseLayout?.artwork.Length??0)-warehouseLandings+(continuationLayout?.artwork.Length??0)-continuationLandings+connector,
                "Потеряны границы или геометрия модулей.");
            int continuationNpcs=continuationBatch?.npcs?.actors.Length??0;
            Require(batch.doors.doors.Length==6+(officeBatch?.doors?.doors.Length??0)+(warehouseBatch?.doors?.doors.Length??0)+(continuationBatch?.doors?.doors.Length??0) && batch.physical.props.Length==6+(officeBatch?.physical?.props.Length??0)+(warehouseBatch?.physical?.props.Length??0)+(continuationBatch?.physical?.props.Length??0) && batch.physical.containers.Length==15+(officeBatch?.physical?.containers.Length??0)+(warehouseBatch?.physical?.containers.Length??0)+(continuationBatch?.physical?.containers.Length??0) &&
                batch.physical.loot.Length==13+(officeBatch?.physical?.loot.Length??0)+(warehouseBatch?.physical?.loot.Length??0)+(continuationBatch?.physical?.loot.Length??0) && batch.npcs.actors.Length==6+crawlers+officeNpcs+warehouseNpcs+continuationNpcs && batch.travelPoints.Length==1,
                "Потеряно содержимое модулей или размножена выходная дверь.");
            var lights=room.GetComponent<RoomLightingAuthoring>().CreateProfile(room);
            int officeLights=office?.GetComponent<RoomLightingAuthoring>()?.CreateProfile(office).lights.Length??0;
            int warehouseLights=warehouse?.GetComponent<RoomLightingAuthoring>()?.CreateProfile(warehouse).lights.Length??0;
            int continuationLights=continuation?.GetComponent<RoomLightingAuthoring>()?.CreateProfile(continuation).lights.Length??0;
            Require(lights.lights.Length==15+officeLights+warehouseLights+continuationLights && lights.lights.Select(l=>l.id).Distinct().Count()==15+officeLights+warehouseLights+continuationLights &&
                lights.lights.Count(l=>l.x>38.4f && l.x<76.8f)==2,"Потеряны или не смещены источники света цеха.");
            var record=room.GetComponentsInChildren<RoomImportAudit>().Single(a=>a.Report.SourceId=="original:plant:18").Report;
            Require(record.Records.Length==24 && record.Records.Count(r=>!r.installed)==6-crawlers-mechanisms,"Пропуски исходного содержимого скрыты или потеряны.");
            var ammo=batch.physical.containers.Single(c=>c.id.EndsWith(":sjN3zN7dP8aCZGe6",StringComparison.Ordinal));
            Require(Mathf.Abs(ammo.x-40.4f)<.001f && Mathf.Abs(ammo.y-10.4f)<.001f,"Контейнер соседней комнаты не получил смещение модуля.");
            foreach(float width in new[]{.2f,1.6f})
            foreach(int direction in new[]{-1,1})
            {
                var hull=new MovementHull {width=width,height=2};var start=new Vector2(direction>0?37:40,10.4f);
                for(int i=0;i<60;i++)
                {
                    start=ActorTerrainMotion.Move(layout,hull,start,new Vector2(direction*.05f,-.01f),false);
                    Require(Mathf.Abs(start.y-10.4f)<.001f && ActorTerrainMotion.IsClear(layout,hull,start) && ActorTerrainMotion.Supported(layout,hull,start),
                        "Стык комнат содержит стену, провал или теряет опору.");
                }
                Require(direction>0?start.x>39.9f:start.x<37.1f,"Пересечение стыка заблокировано.");
            }
            var world=new WorldState {worldId="connected-factory-content-check"};MechanismPersistence.Prepare(world,()=>{});RoomContentInstaller.Apply(world,batch);
            Require(world.containers.Count==batch.physical.containers.Length && world.doors.Count==batch.doors.doors.Length && world.actors.Count==6+crawlers+officeNpcs+warehouseNpcs+continuationNpcs,"Расстановка связного мира потеряла объекты.");
            var container=world.containers.Single(c=>c.id==ammo.id);container.items.Clear();container.x+=.2f;
            var restored=StateJson.Copy(world);var before=StateJson.ToJson(restored);
            Require(!RoomContentInstaller.Apply(restored,batch) && StateJson.ToJson(restored)==before,"Повторный вход пополнил лут или сбросил положение.");
            Debug.Log("Связный завод: PASS — сохранённые модули/контент/свет, проход стыка в обе стороны с разной шириной тела, устойчивые ID и повторная загрузка без сброса лута.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Связный завод: "+reason);}
    }
}
