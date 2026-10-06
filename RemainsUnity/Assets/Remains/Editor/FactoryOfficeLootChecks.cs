using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryOfficeLootChecks
    {
        public static void Run()
        {
            PropCatalog.Load();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            FactoryOfficeLootArtworkAuthoring.Check();
            Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        }
        // Strict stage discriminator also used by earlier checks: partial four-record installs are rejected.
        internal static bool CheckOfficeStage(RoomAuthoring room)
        {
            var layout=FactoryOfficeFurnitureChecks.BeforeFurniture(RoomPhysicalContent.Read(room,"factory:0"));
            bool installed=layout?.patch=="factory:0:"+FactoryOfficeLootAuthoring.OfficePatch;
            Require(installed || layout?.patch=="factory:0:"+FactoryOfficeDraft.PhysicalPatch,"Неизвестная физическая версия офиса.");
            var ids=FactoryOfficeLootAuthoring.Containers.Select(c=>"factory:0:"+c.Id).ToArray();
            Require(layout.props.Length==3 && layout.containers.Length==(installed?16:12) && layout.loot.Length==(installed?15:11) &&
                layout.containers.Select(c=>c.id).Distinct().Count()==layout.containers.Length &&
                Same(layout.containers.Where(c=>ids.Contains(c.id)).Select(c=>c.id),installed?ids:Array.Empty<string>()),
                "Не сохранены ровно 12 прежних и 4 добавочных контейнера/таблицы.");
            FactoryOfficeLootAuthoring.ValidateSource(room.GetComponent<RoomImportAudit>().Report,installed);
            if(installed) Require(layout.expansion?.previousPatch=="factory:0:"+FactoryOfficeDraft.PhysicalPatch &&
                Same(layout.expansion.additions,ids) && layout.expansion.earlierVersions.Length==0,"Потеряно дополнение офис v1→v2, ровно четыре ID.");
            return installed;
        }
        internal static void CheckOffice(RoomAuthoring room)
        {
            Require(CheckOfficeStage(room),"Четыре контейнера офиса ещё не установлены.");
            var layout=RoomPhysicalContent.Read(room,"factory:0");CheckPlacement(room,layout,0);
            int npcCount=RoomNpcContent.Read(room,"factory:0")?.actors.Length??0;
            bool furniture=FactoryOfficeFurnitureChecks.CheckOfficeStage(room);
            bool openings=FactoryOfficeOpeningsChecks.CheckOfficeStage(room);
            Require(npcCount==5 && room.GetComponent<RoomImportAudit>().Report.Records.Count(r=>!r.installed)==17-(furniture?8:0)-(openings?7:0),
                "Потеряна встреча из пяти NPC или скрыты другие неподключённые записи.");
            CheckPersistence(new RoomContentBatch {physical=layout},RoomPhysicalContent.Read(room,"factory:6"),false);
        }
        public static void Check(RoomAuthoring room)
        {
            var office=room.GetComponentsInChildren<RoomAuthoring>(true).Single(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53");
            CheckOffice(office);
            var batch=FactoryWarehouseHistoryProjection.BeforeWarehouse(RoomContentBatch.Read(room,"factory:0"));
            batch.physical=FactoryOfficeFurnitureChecks.BeforeFurniture(batch.physical);
            Require(batch.physical.patch=="factory:0:"+FactoryOfficeLootAuthoring.ConnectedPatch && batch.physical.props.Length==9 &&
                batch.physical.containers.Length==31 && batch.physical.loot.Length==28,"Связный завод потерял точные 9 props/31 containers/28 loot bindings v5.");
            var additions=FactoryOfficeLootAuthoring.Containers.Select(c=>"factory:0:"+c.Id).ToArray();
            var oldOffice=batch.physical.props.Cast<PropState>().Concat(batch.physical.containers).Where(p=>p.id.StartsWith("factory:0:original:plant:53:",StringComparison.Ordinal) &&
                !additions.Contains(p.id)).Select(p=>p.id).ToArray();
            var steps=new[]{(ContentExpansionStep)batch.physical.expansion}.Concat(batch.physical.expansion.earlierVersions).ToArray();
            Require(steps.Length==4 && steps[0].previousPatch=="factory:0:"+FactoryOfficeConnectionAuthoring.PhysicalPatch && Same(steps[0].additions,additions) &&
                steps[1].previousPatch=="factory:0:"+FactoryToolStandAuthoring.ConnectedPatch && Same(steps[1].additions,oldOffice.Concat(additions)) &&
                steps[2].previousPatch=="factory:0:base:factory-connected-46-18:physical-v2" && Same(steps[2].additions,oldOffice.Concat(additions).Concat(new[]{"factory:0:"+FactoryToolStandAuthoring.SourceId})) &&
                steps[3].previousPatch=="factory:0:base:factory-connected-46-18:physical-v1" && Same(steps[3].additions,oldOffice.Concat(additions).Concat(new[]{"factory:0:"+FactoryToolStandAuthoring.SourceId,
                    "factory:0:original:plant:18:ZoIAvJZMFks7XYbh","factory:0:original:plant:18:Ainu8fDKejKYdTIz"})),
                "Потеряны точные cumulative v4→4, v3→19, v2→20, v1→22 новые ID.");
            CheckPlacement(room,batch.physical,76.8f);
            var catalog=EntityAssetCatalog.Load();var artwork=new PropArtwork();var icons=new InventoryArtwork();
            foreach(var entry in FactoryOfficeLootAuthoring.Containers)
            {
                var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(entry.AssetPath);var data=asset.CreateDefinition();
                Require(catalog.CreateProps().Count(d=>d.id==entry.type)==1 && catalog.TryGetPropPrefab(data.id,data.resource,out var prefab) && prefab==asset.Prefab,
                    "Нет единственного канонического ассета в каталоге: "+entry.type);
                foreach(var item in FactoryOfficeLootAuthoring.CreateLoot(entry.type).entries) Require(icons.Require(item.definition)!=null,"Нет исходной иконки существующего предмета: "+item.definition);
                var container=batch.physical.containers.Single(c=>c.id=="factory:0:"+entry.Id);var view=artwork.CreateContainerView(container);
                try
                {
                    view.DrawMotion(container,null,false,0);var bounds=view.Artwork.bounds;
                    float minX=container.x-(entry.type=="instr1"?.74f:container.width*.5f);
                    Require(Mathf.Abs(bounds.min.x-minX)<.001f && Mathf.Abs(bounds.min.y-container.y)<.001f &&
                        Mathf.Abs(bounds.size.x-container.width)<.001f && Mathf.Abs(bounds.size.y-container.height)<.001f,
                        "Графическая регистрация приняла E rect/body offset: "+container.id+", bounds="+bounds);
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
            CheckPersistence(batch,RoomPhysicalContent.Read(room,"factory:6"),true);
            Debug.Log("Офисный лут: PASS — четыре source ID/anchor/типа, исходная графическая регистрация отдельно от E, 3 тела + 1 настенный NoBody, 31 общий контейнер; v1/v2/v3/v4→v5 без возврата отсутствий/пустого лута, смерти NPC, cargo/платформы и модовых полей; claims/reload/dedup и разные фабрики. Замки/навыки отложены.");
        }
        private static void CheckPlacement(RoomAuthoring room,PhysicalLayout layout,float offset)
        {
            var geometry=room.CreateLayout(false);var markers=room.GetComponentsInChildren<RoomContainerPlacement>(true).Where(room.Included).ToArray();
            foreach(var entry in FactoryOfficeLootAuthoring.Containers)
            {
                var state=layout.containers.Single(c=>c.id==layout.level+":"+entry.Id);
                var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(entry.AssetPath);Require(asset!=null && asset.Prefab!=null,"Нет ассета/сохранённой графики: "+entry.type);
                var definition=asset.CreateDefinition();PropBodyRules.Validate(state);
                Require(state.definition==entry.type && state.anchorHeight==0 && Vector2.Distance(state.Position,entry.Position+Vector2.right*offset)<.001f &&
                    state.width==definition.width && state.height==definition.height && state.mass==definition.mass && state.hasPhysicalBody==definition.hasPhysicalBody && state.movable==definition.movable,
                    "Данные тела/типа/anchor отличаются от SO: "+state.id);
                Require(state.x-state.width*.5f>=geometry.left && state.x+state.width*.5f<=geometry.right && state.y>=geometry.bottom && state.y+state.height<=geometry.top,
                    "Курсорное тело вне реальных границ комнаты: "+state.id);
                InventorySource source=state;
                Require(source.Bounds==new Rect(state.x-state.width*.5f,state.y,state.width,state.height) && source.Mass==(state.hasPhysicalBody?(float?)state.mass:null),
                    "E bounds/mass не читают канонические данные: "+state.id);
                if(state.hasPhysicalBody)
                {
                    var hull=new MovementHull {width=state.width,height=state.height};
                    Require(ActorTerrainMotion.IsClear(geometry,hull,state.Position) && ActorTerrainMotion.Supported(geometry,hull,state.Position),
                        "Новое тело пересекает стены/потеряло исходную опору: "+state.id);
                    Require(new TelekinesisTarget(state).Movable,"Переносимый исходный контейнер потерял TK: "+state.id);
                }
                else
                {
                    Require(entry.type=="medbox" && !state.movable && state.mass==0 && !new TelekinesisTarget(state).Movable,"Настенная аптечка подменена физическим ящиком.");
                    var test=new PropState {id="no-wall-support",level=state.level,x=state.x,y=state.y+state.height,width=.2f,height=.2f,mass=1,anchorHeight=0};
                    Require(!PropSupport.Supported(test,test.Position,new[]{state}),"Настенная аптечка создаёт несуществующую опору.");
                }
                var marker=markers.Single(p=>p.Read(room).id==entry.Id);
                Require(marker.Definition==asset && marker.ReadInitialItems().Length==0 && StateJson.ToJson(marker.ReadLoot())==StateJson.ToJson(FactoryOfficeLootAuthoring.CreateLoot(entry.type)),
                    "Не сохранена единственная явная таблица/SO; начальный предмет дублирован: "+entry.Id);
                var preview=marker.GetComponentsInChildren<PropView>(true);
                Require(preview.Length==1 && PrefabUtility.GetCorrespondingObjectFromOriginalSource(preview[0].gameObject)==asset.Prefab.gameObject &&
                    Vector3.Distance(preview[0].transform.localPosition,state.hasPhysicalBody?Vector3.up*(state.height*.5f):Vector3.zero)<.001f &&
                    preview[0].transform.localScale==asset.Prefab.transform.localScale && preview[0].transform.localRotation==asset.Prefab.transform.localRotation &&
                    preview[0].Artwork!=null && preview[0].Artwork.sprite==asset.Prefab.Artwork.sprite &&
                    preview[0].Artwork.sharedMaterial==asset.Prefab.Artwork.sharedMaterial && preview[0].VisualAnchor==asset.Prefab.VisualAnchor &&
                    preview[0].FitsStateWidth==asset.Prefab.FitsStateWidth,
                    "Нет сохранённого prefab artwork с правильным body/placement anchor: "+entry.Id);
            }
        }
        // Pure content/persistence consumers: also callable with explicit layouts in a CLI harness.
        public static void CheckPersistence(RoomContentBatch batch,PhysicalLayout other,bool connected)
        {
            batch=FactoryWarehouseHistoryProjection.BeforeWarehouse(batch);batch.physical=FactoryOfficeFurnitureChecks.BeforeFurniture(batch.physical);
            other=FactoryOfficeFurnitureChecks.BeforeFurniture(other);
            var layout=batch.physical;string prefix=layout.level+":";
            var ids=FactoryOfficeLootAuthoring.Containers.Select(c=>prefix+c.Id).ToArray();
            Require(ids.All(id=>layout.containers.Count(c=>c.id==id)==1 && layout.loot.Count(b=>b.container==id)==1),"Четыре ID/таблицы подключены частично.");
            var fresh=new WorldState {worldId="office-loot-check",mechanismVersion=1};Require(RoomContentInstaller.Apply(fresh,batch),"Новый мир не установился.");
            Require(fresh.containers.Count==(connected?31:16),"Неверное полное количество контейнеров.");CheckInitialLoot(fresh,ids);
            var repeat=new WorldState {worldId=fresh.worldId,mechanismVersion=1};RoomContentInstaller.Apply(repeat,batch);
            Require(StateJson.ToJson(repeat.containers)==StateJson.ToJson(fresh.containers),"Идентичные WorldId/container IDs изменили начальный лут.");
            var steps=new[]{(ContentExpansionStep)layout.expansion}.Concat(layout.expansion.earlierVersions).ToArray();
            foreach(var step in steps)
            {
                var world=StateJson.Copy(fresh);world.contentPatches.Remove(layout.patch);world.contentPatches.Add(step.previousPatch);
                world.props.RemoveAll(p=>step.additions.Contains(p.id));world.containers.RemoveAll(c=>step.additions.Contains(c.id));
                string missingProp=world.props.First().id,missingContainer=world.containers.First().id;
                world.props.RemoveAt(0);world.containers.RemoveAt(0);
                var cargo=world.props.First();cargo.x+=.19f;cargo.y+=.3f;cargo.held=true;cargo.owner="saved-cargo-owner";cargo.generation=7;cargo.sequence=9;cargo.extensions=Extensions("cargo");
                foreach(var container in world.containers) {container.items.Clear();container.extensions=Extensions("depleted-container");}
                // A saved deposit is distinct from authored loot: preserve its ID, ammo, alias and mod data.
                var deposited=world.containers.First(c=>c.definition!="chest");
                var savedItem=new ItemStack {id="saved-deposit:"+step.previousPatch,definition="pistol",count=1,extensions=Extensions("deposited-item"),
                    weaponState=new WeaponItemState {rounds=1,revision=13,extensions=Extensions("deposited-weapon")}};
                deposited.items.Add(savedItem);deposited.itemAliases["saved-alias"]=savedItem.id;
                // Existing empty authored chests and the other depleted containers remain empty alike.
                if(world.actors.Count>0) {var dead=world.actors.First();dead.health=0;dead.down=true;dead.inventory.Clear();dead.owner="saved-dead-owner";dead.generation=11;dead.extensions=Extensions("dead-npc");}
                if(world.movingPlatforms.Count>0)
                {
                    var platform=world.movingPlatforms[0];platform.endpoint=0;platform.revision=7;
                    platform.segment=new MovingPlatformSegment {sequence=7,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=3,durationSeconds=6,extensions=Extensions("midsegment")};
                    world.mechanismButtons[0].repairState=MechanismRepairStates.Ready;world.mechanismButtons[0].revision=9;
                }
                world.extensions=Extensions("world");world=StateJson.Copy(world);
                var oldProps=world.props.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                var oldContainers=world.containers.Select(c=>(c.id,json:StateJson.ToJson(c))).ToArray();
                string others=StateJson.ToJson(new {world.actors,world.doors,world.movingPlatforms,world.mechanismButtons,world.extensions,world.levelTimes});
                int oldCount=world.props.Count+world.containers.Count;
                Require(RoomContentInstaller.Apply(world,batch) && world.props.Count+world.containers.Count==oldCount+step.additions.Length &&
                    oldProps.All(p=>world.props.Any(q=>q.id==p.id && StateJson.ToJson(q)==p.json)) &&
                    oldContainers.All(p=>world.containers.Any(q=>q.id==p.id && StateJson.ToJson(q)==p.json)) &&
                    world.props.All(p=>p.id!=missingProp) && world.containers.All(c=>c.id!=missingContainer) &&
                    StateJson.ToJson(new {world.actors,world.doors,world.movingPlatforms,world.mechanismButtons,world.extensions,world.levelTimes})==others,
                    "Upgrade возродил старый объект/лут или изменил depleted/dead/cargo/платформу/mod fields: "+step.previousPatch);
                var saved=world.containers.Single(c=>c.id==ids[1]);saved.items[0].extensions=Extensions("item");
                saved.items.First(i=>i.weaponState!=null).weaponState.extensions=Extensions("weapon");saved.items.First(i=>i.weaponState!=null).weaponState.rounds=2;
                saved.itemAliases["old-pistol"]=saved.items.First(i=>i.weaponState!=null).id;
                world.containers.Single(c=>c.id==ids[2]).items.Clear();world.containers.RemoveAll(c=>c.id==ids[0]);
                world=StateJson.Copy(world);string before=StateJson.ToJson(world);
                Require(!RoomContentInstaller.Apply(world,batch) && StateJson.ToJson(world)==before && world.containers.All(c=>c.id!=ids[0]) &&
                    world.containers.Single(c=>c.id==ids[2]).items.Count==0,"Повторный вход/reload возродил новый отсутствующий контейнер или пополнил аптечку.");
                CheckClaim(world,ids[1]);
            }
            PhysicalContentInstaller.Apply(fresh,other);
            var itemIds=fresh.containers.SelectMany(c=>c.items).Concat(fresh.actors.SelectMany(a=>a.inventory)).Select(i=>i.id).ToArray();
            Require(other.level=="factory:6" && fresh.containers.Count==layout.containers.Length*2 && fresh.containers.Select(c=>c.id).Distinct().Count()==fresh.containers.Count &&
                itemIds.Distinct().Count()==itemIds.Length,"Фабрики0/6 разделяют container/item ID.");
        }
        private static void CheckInitialLoot(WorldState world,string[] ids)
        {
            var c=ids.Select(id=>world.containers.Single(s=>s.id==id)).ToArray();
            Require(c[0].items.Count==1 && c[0].items.Single().definition=="grenade" && c[0].items.Single().count==1 &&
                c[1].items.Count==2 && c[1].items.Single(i=>i.definition=="pistol").weaponState.rounds==6 && c[1].items.Single(i=>i.definition=="p10").count==10 &&
                c[2].items.Count==1 && c[2].items.Single().definition=="heal" && c[2].items.Single().count==2 && c[3].items.Count==3 &&
                c[3].items.Single(i=>i.definition=="pin").count==2 && c[3].items.Single(i=>i.definition=="rep").count==1 && c[3].items.Single(i=>i.definition=="metal_comp").count==2,
                "Начальное содержимое отличается от явного самостоятельного баланса Unity.");
        }
        private static void CheckClaim(WorldState world,string id)
        {
            var source=world.containers.Single(c=>c.id==id);var item=source.items.Single(i=>i.weaponState!=null);
            string itemState=StateJson.ToJson(item);
            var a=new ActorState {id="office-claim-A",player=true,connected=true,level=source.level,Position=source.Position};
            var b=new ActorState {id="office-claim-B",player=true,connected=true,level=source.level,Position=source.Position};world.actors.Add(a);world.actors.Add(b);
            var shared=new SharedInventory(world);
            Require(shared.PrepareClaim("office-loot-transaction",id,item.id,a) && a.inventory.Count==0 &&
                !shared.PrepareClaim("office-loot-transaction",id,item.id,a) && !shared.PrepareClaim("office-second-client",id,item.id,b),
                "Предмет выдан до подтверждения или два просмотра получили одну копию.");
            world=StateJson.Copy(world);shared=new SharedInventory(world);var grant=world.pendingGrants.Single();shared.CommitGrant(grant);shared.CommitGrant(grant);
            Require(world.Find(a.id).inventory.Count==1 && world.Find(b.id).inventory.Count==0 && StateJson.ToJson(world.Find(a.id).inventory.Single())==itemState &&
                world.containers.Single(c=>c.id==id).items.All(i=>i.id!=item.id),"Подтверждение после reload удвоило/потеряло пистолет/патроны/mod fields.");
        }
        private static Dictionary<string,JToken> Extensions(string key)=>new() {{key,JToken.Parse("{\"nested\":[13,\"сохранённое поле мода\"]}")}};
        private static bool Same(IEnumerable<string> a,IEnumerable<string> b)=>a.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(b.OrderBy(id=>id,StringComparer.Ordinal));
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Офисный лут: "+reason);}
    }
}
