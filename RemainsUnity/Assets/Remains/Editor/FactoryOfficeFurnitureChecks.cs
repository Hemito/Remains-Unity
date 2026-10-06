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
    public static class FactoryOfficeFurnitureChecks
    {
        public static void Run()
        {
            PropCatalog.Load();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            FactoryOfficeFurnitureArtworkAuthoring.Check();Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        }
        public static bool CheckOfficeStage(RoomAuthoring room)
        {
            bool installed=CheckOfficeStage(RoomPhysicalContent.Read(room,"factory:0"));
            FactoryOfficeFurnitureAuthoring.ValidateSource(room.GetComponent<RoomImportAudit>().Report,installed);
            return installed;
        }
        // Fresh source import/NPC authoring still uses v1 before the four-container v2 milestone.
        internal static bool CheckOfficeStage(PhysicalLayout source)
        {
            string prefix=source.level+":";bool installed=source.patch==prefix+FactoryOfficeFurnitureAuthoring.OfficePatch;
            var before=BeforeFurniture(source);bool loot=before.patch==prefix+FactoryOfficeLootAuthoring.OfficePatch;
            Require(loot || before.patch==prefix+FactoryOfficeDraft.PhysicalPatch,"Неизвестная офисная версия мебели.");
            Require(before.props.Length==3 && before.containers.Length==(loot?16:12) && before.loot.Length==(loot?15:11),
                "Неполная прежняя офисная версия мебели.");
            if(!installed)
            {
                var ids=FactoryOfficeFurnitureAuthoring.Records.Select(r=>prefix+r.Id).ToArray();
                Require(!source.props.Cast<PropState>().Concat(source.containers).Any(p=>ids.Contains(p.id)) && !source.loot.Any(b=>ids.Contains(b.container)),
                    "Предыдущая версия скрывает частичную установку мебели.");
            }
            return installed;
        }
        // Exact stage projection for historical checks; rejects partial additions and never edits a save.
        public static PhysicalLayout BeforeFurniture(PhysicalLayout source)
        {
            string prefix=source.level+":";bool connected=source.patch==prefix+FactoryOfficeFurnitureAuthoring.ConnectedPatch;
            bool office=source.patch==prefix+FactoryOfficeFurnitureAuthoring.OfficePatch;
            if(!connected && !office) return source;
            CheckNewRecords(source,connected?76.8f:0);
            Require(source.props.Length==(connected?12:6) && source.containers.Length==(connected?36:21) && source.loot.Length==(connected?33:20),
                "Неполное v6/v3 дополнение не скрывается predecessor fixture.");
            var result=FactoryOfficeFurnitureAuthoring.RemoveNewRecords(source);
            var steps=source.expansion?.earlierVersions;
            Require(steps!=null && steps.Length==(connected?4:1),"Потеряна прежняя cumulative chain мебели.");
            result.patch=prefix+(connected?FactoryOfficeLootAuthoring.ConnectedPatch:FactoryOfficeLootAuthoring.OfficePatch);
            var ids=FactoryOfficeFurnitureAuthoring.Records.Select(r=>prefix+r.Id).ToArray();
            result.expansion=new PhysicalExpansion {previousPatch=steps[0].previousPatch,additions=steps[0].additions.Where(id=>!ids.Contains(id)).ToArray(),
                earlierVersions=steps.Skip(1).Select(s=>new ContentExpansionStep {previousPatch=s.previousPatch,additions=s.additions.Where(id=>!ids.Contains(id)).ToArray()}).ToArray()};
            var expected=FactoryOfficeFurnitureAuthoring.CreateExpansion(result,connected);
            var inLevel=ContentExpansionRules.InLevel(new PhysicalExpansion {previousPatch=expected.previousPatch,additions=expected.additions,earlierVersions=expected.earlierVersions},source.level);
            Require(StateJson.ToJson(source.expansion)==StateJson.ToJson(inLevel),"Изменены точные ID/порядок/преемники дополнения мебели.");
            return result;
        }
        public static void CheckOffice(RoomAuthoring room)
        {
            Require(CheckOfficeStage(room),"Восемь записей мебели ещё не установлены.");
            var layout=RoomPhysicalContent.Read(room,"factory:0");var batch=RoomContentBatch.Read(room,"factory:0");
            bool openings=FactoryOfficeOpeningsChecks.CheckOfficeStage(room);
            Require(room.GetComponent<RoomImportAudit>().Report.Records.Count(r=>!r.installed)==(openings?2:9) && batch.npcs.actors.Length==5 && batch.doors.doors.Length==(openings?9:3),
                "Число pending/NPC/дверей не соответствует проверенной стадии проёмов.");
            CheckPlacement(room,layout,0);CheckGeometry(room.CreateLayout(false),layout,batch.npcs.actors,batch.doors.doors);
            CheckPersistence(batch,RoomPhysicalContent.Read(room,"factory:6"),false);
        }
        public static void Check(RoomAuthoring room)
        {
            var office=room.GetComponentsInChildren<RoomAuthoring>(true).Single(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53");
            CheckOffice(office);var batch=RoomContentBatch.Read(room,"factory:0");BeforeFurniture(batch.physical);
            bool openings=FactoryOfficeOpeningsChecks.CheckOfficeStage(office);
            Require(batch.physical.patch=="factory:0:"+FactoryOfficeFurnitureAuthoring.ConnectedPatch && batch.npcs.actors.Length==13 && batch.doors.doors.Length==(openings?15:9) &&
                batch.mechanisms.movingPlatforms.Length==1 && batch.mechanisms.mechanismButtons.Length==2,"Мебель изменила прежние NPC/двери/механизмы.");
            CheckPlacement(room,batch.physical,76.8f);CheckGeometry(room.CreateLayout(false),batch.physical,batch.npcs.actors,batch.doors.doors);
            var catalog=EntityAssetCatalog.Load();var icons=new InventoryArtwork();
            foreach(var entry in FactoryOfficeFurnitureAuthoring.Records)
            {
                var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(entry.AssetPath);var data=asset.CreateDefinition();
                Require(catalog.CreateProps().Count(d=>d.id==entry.type)==1 && catalog.TryGetPropPrefab(data.id,data.resource,out var prefab) && prefab==asset.Prefab,
                    "Нет одного канонического SO/prefab мебели: "+entry.type);
                if(entry.container) foreach(var item in FactoryOfficeFurnitureAuthoring.CreateLoot(entry.type).entries)
                    Require(icons.Require(item.definition)!=null,"Нет точной иконки существующего предмета: "+item.definition);
            }
            CheckPersistence(batch,RoomPhysicalContent.Read(room,"factory:6"),true);
            Debug.Log($"Мебель офисов53: PASS — восемь точных source ID/anchors, 3 props + 5 общих контейнеров, {(openings?44:37)}/46 записей, {(openings?2:9)} pending; source тела/графика/SO отдельно от E, цепочки office v1/v2→v3 и root v1/v2/v3/v4/v5→v6 без refill/resurrection; depleted/dead/cargo/midsegment/модовые поля, claims/reload/dedup. Native маршрут/исходные skills/locks/open sprite этой проверкой не подтверждаются.");
        }
        private static void CheckNewRecords(PhysicalLayout layout,float offset)
        {
            var entries=FactoryOfficeFurnitureAuthoring.Records;var all=layout.props.Cast<PropState>().Concat(layout.containers).ToArray();
            var ids=entries.Select(r=>layout.level+":"+r.Id).ToArray();
            Require(all.Where(p=>ids.Contains(p.id)).Select(p=>p.id).Distinct().Count()==8 && all.Count(p=>ids.Contains(p.id))==8 &&
                layout.containers.Count(p=>ids.Contains(p.id))==5 && layout.props.Count(p=>ids.Contains(p.id))==3 && layout.loot.Count(b=>ids.Contains(b.container))==5,
                "Не подключены ровно восемь ID/три props/пять контейнеров и таблиц.");
            foreach(var entry in entries)
            {
                var state=all.Single(p=>p.id==layout.level+":"+entry.Id);var data=FactoryOfficeFurnitureArtworkAuthoring.Definition(entry.type);
                PropBodyRules.Validate(state);
                Require((state is ContainerState)==entry.container && state.definition==entry.type && state.anchorHeight==0 && state.level==layout.level &&
                    Vector2.Distance(state.Position,entry.Position+Vector2.right*offset)<.001f && state.width==data.width && state.height==data.height &&
                    state.mass==data.mass && state.movable==data.movable && state.hasPhysicalBody==data.hasPhysicalBody,
                    "Сохранённая расстановка не читает согласованные source/SO данные: "+state.id);
                if(entry.container) Require(StateJson.ToJson(layout.loot.Single(b=>b.container==state.id).table)==StateJson.ToJson(FactoryOfficeFurnitureAuthoring.CreateLoot(entry.type)) &&
                    ((ContainerState)state).items.Count==0,"Нет одной явной таблицы или два начальных источника лута: "+state.id);
            }
        }
        private static void CheckPlacement(RoomAuthoring room,PhysicalLayout layout,float offset)
        {
            CheckNewRecords(layout,offset);
            var markers=room.GetComponentsInChildren<RoomPhysicalPlacement>(true).Where(room.Included).ToArray();
            foreach(var entry in FactoryOfficeFurnitureAuthoring.Records)
            {
                var state=layout.props.Cast<PropState>().Concat(layout.containers).Single(p=>p.id==layout.level+":"+entry.Id);
                var marker=markers.Single(p=>p.Read(room).id==entry.Id);var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(entry.AssetPath);
                Require(asset!=null && asset.Prefab!=null && marker.Definition==asset && (marker is RoomContainerPlacement)==entry.container,
                    "Нет единственного сохранённого marker/SO мебели: "+entry.Id);
                if(marker is RoomContainerPlacement c) Require(c.ReadInitialItems().Length==0 &&
                    StateJson.ToJson(c.ReadLoot())==StateJson.ToJson(FactoryOfficeFurnitureAuthoring.CreateLoot(entry.type)),"Начальный инвентарь/таблица отличается: "+entry.Id);
                var preview=marker.GetComponentsInChildren<PropView>(true);
                Require(preview.Length==1 && PrefabUtility.GetCorrespondingObjectFromOriginalSource(preview[0].gameObject)==asset.Prefab.gameObject &&
                    Vector3.Distance(preview[0].transform.localPosition,state.hasPhysicalBody?Vector3.up*(state.height*.5f):Vector3.zero)<.001f &&
                    preview[0].transform.localRotation==asset.Prefab.transform.localRotation && preview[0].transform.localScale==asset.Prefab.transform.localScale &&
                    preview[0].Artwork.sprite==asset.Prefab.Artwork.sprite && preview[0].Artwork.sharedMaterial==asset.Prefab.Artwork.sharedMaterial &&
                    preview[0].VisualAnchor==asset.Prefab.VisualAnchor && preview[0].FitsStateWidth==asset.Prefab.FitsStateWidth,
                    "Не сохранён nested original prefab/anchor/material мебели: "+entry.Id);
                // Native registration differs by 0.01m for table1/trash; collision rect remains centered.
                var artwork=new PropArtwork();var view=entry.container?artwork.CreateContainerView((ContainerState)state):artwork.CreateView(state.definition,state.id,state.width);
                try
                {
                    view.DrawMotion(state,null,false,0);var bounds=view.Artwork.bounds;
                    float registration=entry.type switch {"table"=>.62f,"table1"=>.68f,"trash"=>.24f,"bookcase"=>.8f,_=>throw new InvalidOperationException(entry.type)};
                    Require(Mathf.Abs(bounds.min.x-(state.x-registration))<.001f && Mathf.Abs(bounds.min.y-state.y)<.001f &&
                        Mathf.Abs(bounds.size.x-state.width)<.001f && Mathf.Abs(bounds.size.y-state.height)<.001f,"Графическая регистрация подменена E/body rect: "+entry.Id);
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
        }
        // Pure checks against actual saved room geometry/content; no preview renderer source of truth.
        public static void CheckGeometry(RoomLayout room,PhysicalLayout physical,ActorState[] actors,DoorState[] doors)
        {
            var all=physical.props.Cast<PropState>().Concat(physical.containers).ToArray();
            var ids=FactoryOfficeFurnitureAuthoring.Records.Select(r=>physical.level+":"+r.Id).ToArray();
            foreach(var state in all.Where(p=>ids.Contains(p.id)))
            {
                var bounds=new Rect(state.x-state.width*.5f,state.y,state.width,state.height);
                Require(bounds.xMin>=room.left-.001f && bounds.xMax<=room.right+.001f && bounds.yMin>=room.bottom-.001f && bounds.yMax<=room.top+.001f,
                    "E rect мебели вне границ комнаты: "+state.id);
                if(state is ContainerState)
                {
                    InventorySource source=(ContainerState)state;
                    Require(source.Bounds==bounds && source.Mass==(state.hasPhysicalBody?(float?)state.mass:null),"E/mass не читают канонический prop: "+state.id);
                }
                if(!state.hasPhysicalBody)
                {
                    Require(state.definition=="bookcase" && !state.movable && state.mass==0 && !new TelekinesisTarget(state).Movable,"Книжный шкаф wall1 подменён физическим телом.");
                    var above=new PropState {id="above-bookcase",level=state.level,x=state.x,y=state.y+state.height,width=.2f,height=.2f,mass=1};
                    Require(!PropSupport.Supported(above,above.Position,new[]{state}),"NoBody книжный шкаф создаёт несуществующую опору.");continue;
                }
                var hull=new MovementHull {width=state.width,height=state.height};
                Require(ActorTerrainMotion.IsClear(room,hull,state.Position) && ActorTerrainMotion.Supported(room,hull,state.Position) && new TelekinesisTarget(state).Movable,
                    "Исходное тело мебели пересекает стены/потеряло опору/TK: "+state.id);
                foreach(var other in all.Where(p=>p.hasPhysicalBody && p.id!=state.id))
                    Require(!bounds.Overlaps(new Rect(other.x-other.width*.5f,other.y-other.anchorHeight,other.width,other.height)),"Мебель пересекает прежнее тело/мебель: "+state.id+" / "+other.id);
                foreach(var actor in actors)
                {
                    var movement=Anatomy.Movement(actor);var actorBounds=new Rect(actor.x-movement.width*.5f,actor.y,movement.width,movement.height);
                    Require(!bounds.Overlaps(actorBounds),"Исходная мебель перекрыла сохранённую начальную расстановку NPC: "+state.id+" / "+actor.id);
                }
                foreach(var door in doors) Require(!door.Bounds.Overlaps(bounds),"Мебель перекрыла существующую дверь: "+state.id+" / "+door.id);
            }
        }
        public static void CheckPersistence(RoomContentBatch batch,PhysicalLayout other,bool connected)
        {
            var layout=batch.physical;BeforeFurniture(layout);string prefix=layout.level+":";
            var ids=FactoryOfficeFurnitureAuthoring.Records.Select(r=>prefix+r.Id).ToArray();
            if(!connected)
            {
                var predecessor=BeforeFurniture(layout);Require(!CheckOfficeStage(predecessor),"Office v2 ошибочно отмечен как furniture stage.");
                var legacy=StateJson.Copy(predecessor);var oldIds=legacy.expansion.additions;
                legacy.patch=legacy.expansion.previousPatch;legacy.expansion=null;
                legacy.containers=legacy.containers.Where(c=>!oldIds.Contains(c.id)).ToArray();legacy.loot=legacy.loot.Where(b=>!oldIds.Contains(b.container)).ToArray();
                Require(!CheckOfficeStage(legacy),"Office v1 Import→NPC workflow отвергнут новым stage query.");
                var hidden=StateJson.Copy(legacy);hidden.containers[0]=StateJson.Copy(layout.containers.Single(c=>c.id==ids[0]));
                bool legacyRejected=false;try {CheckOfficeStage(hidden);} catch(InvalidOperationException) {legacyRejected=true;}
                Require(legacyRejected,"Прежний patch скрывает новый мебельный ID при прежнем count.");
            }
            var fresh=new WorldState {worldId="office-furniture-check",mechanismVersion=1};Require(RoomContentInstaller.Apply(fresh,batch),"Свежий мир не установился.");
            Require(fresh.props.Count==(connected?12:6) && fresh.containers.Count==(connected?36:21),"Неверное количество физических объектов/контейнеров.");
            var again=new WorldState {worldId=fresh.worldId,mechanismVersion=1};RoomContentInstaller.Apply(again,batch);
            Require(StateJson.ToJson(again.containers)==StateJson.ToJson(fresh.containers),"Начальный лут зависит от порядка входа/экземпляра.");
            var chest=fresh.containers.Single(c=>c.id==prefix+"original:plant:53:KGFzJ42eGXsViOgQ");
            Require(chest.items.Count==0 && layout.loot.All(b=>b.container!=chest.id),"Сундук record32 наполнен без отдельной реализации.");
            foreach(var entry in FactoryOfficeFurnitureAuthoring.Records.Where(r=>r.container))
            {
                var source=fresh.containers.Single(c=>c.id==prefix+entry.Id);var expected=FactoryOfficeFurnitureAuthoring.CreateLoot(entry.type).Generate(fresh.worldId,source.id);
                Require(StateJson.ToJson(source.items)==StateJson.ToJson(expected),"Нет единственного независимого loot balance: "+source.id);
            }
            foreach(var step in new[]{(ContentExpansionStep)layout.expansion}.Concat(layout.expansion.earlierVersions))
            {
                var world=StateJson.Copy(fresh);world.contentPatches.Remove(layout.patch);world.contentPatches.Add(step.previousPatch);
                world.props.RemoveAll(p=>step.additions.Contains(p.id));world.containers.RemoveAll(c=>step.additions.Contains(c.id));
                string missingProp=world.props[0].id,missingContainer=world.containers[0].id;world.props.RemoveAt(0);world.containers.RemoveAt(0);
                var cargo=world.props[0];cargo.x+=.19f;cargo.y+=.3f;cargo.held=true;cargo.owner="saved-cargo-owner";cargo.generation=7;cargo.sequence=9;cargo.extensions=Extensions("cargo");
                foreach(var container in world.containers) {container.items.Clear();container.extensions=Extensions("depleted");}
                var deposited=world.containers.First(c=>c.definition!="chest");
                var item=new ItemStack {id="saved-deposit:"+step.previousPatch,definition="pistol",count=1,extensions=Extensions("item"),weaponState=new WeaponItemState {rounds=1,revision=13,extensions=Extensions("weapon")}};
                deposited.items.Add(item);deposited.itemAliases["saved-alias"]=item.id;
                if(world.actors.Count>0) {var dead=world.actors[0];dead.health=0;dead.down=true;dead.inventory.Clear();dead.owner="dead-owner";dead.generation=11;dead.extensions=Extensions("dead");}
                if(world.movingPlatforms.Count>0)
                {
                    var platform=world.movingPlatforms[0];platform.endpoint=0;platform.revision=7;
                    platform.segment=new MovingPlatformSegment {sequence=7,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=3,durationSeconds=6,extensions=Extensions("midsegment")};
                    world.mechanismButtons[0].repairState=MechanismRepairStates.Ready;world.mechanismButtons[0].revision=9;
                }
                world.extensions=Extensions("world");world=StateJson.Copy(world);
                var oldProps=world.props.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();var oldContainers=world.containers.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                string unchanged=StateJson.ToJson(new {world.actors,world.doors,world.movingPlatforms,world.mechanismButtons,world.extensions,world.levelTimes});
                int count=world.props.Count+world.containers.Count;
                Require(RoomContentInstaller.Apply(world,batch) && world.props.Count+world.containers.Count==count+step.additions.Length &&
                    oldProps.All(p=>world.props.Any(q=>q.id==p.id && StateJson.ToJson(q)==p.json)) && oldContainers.All(p=>world.containers.Any(q=>q.id==p.id && StateJson.ToJson(q)==p.json)) &&
                    world.props.All(p=>p.id!=missingProp) && world.containers.All(p=>p.id!=missingContainer) &&
                    StateJson.ToJson(new {world.actors,world.doors,world.movingPlatforms,world.mechanismButtons,world.extensions,world.levelTimes})==unchanged,
                    "Upgrade изменил depleted/dead/cargo/door/clock/platform/mod fields либо возродил отсутствие: "+step.previousPatch);
                world.props.RemoveAll(p=>p.id==ids[3]);world.containers.RemoveAll(c=>c.id==ids[0]);
                var emptied=world.containers.Single(c=>c.id==ids[1]);emptied.items.Clear();emptied.extensions=Extensions("empty-new");
                world=StateJson.Copy(world);string saved=StateJson.ToJson(world);
                Require(!RoomContentInstaller.Apply(world,batch) && StateJson.ToJson(world)==saved && world.props.All(p=>p.id!=ids[3]) && world.containers.All(c=>c.id!=ids[0]) && emptied.items.Count==0,
                    "Reload повторно установил удалённую мебель/контейнер либо наполнил пустой стол.");
                var viewer=new ActorState {id="empty-viewer",player=true,connected=true,level=emptied.level,Position=emptied.Position};world.actors.Add(viewer);
                Require(!new SharedInventory(world).PrepareClaim("empty-claim",emptied.id,"missing-item",viewer),"Пустой источник выдал отсутствующий предмет.");
                CheckClaim(world,ids[2]);
            }
            PhysicalContentInstaller.Apply(fresh,other);var allItems=fresh.containers.SelectMany(c=>c.items).Concat(fresh.actors.SelectMany(a=>a.inventory)).Select(i=>i.id).ToArray();
            Require(other.level=="factory:6" && fresh.containers.Count==layout.containers.Length*2 && fresh.containers.Select(c=>c.id).Distinct().Count()==fresh.containers.Count &&
                allItems.Distinct(StringComparer.Ordinal).Count()==allItems.Length,"Фабрики0/6 разделяют ID объекта/предмета.");
            var partial=StateJson.Copy(layout);partial.containers=partial.containers.Where(c=>c.id!=ids[0]).ToArray();
            bool rejected=false;try {BeforeFurniture(partial);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Predecessor fixture скрыла частичное дополнение восьми записей.");
        }
        private static void CheckClaim(WorldState world,string containerId)
        {
            var source=world.containers.Single(c=>c.id==containerId);var item=source.items.Single();item.extensions=Extensions("claimed-item");string expected=StateJson.ToJson(item);
            var a=new ActorState {id="furniture-A",player=true,connected=true,level=source.level,Position=source.Position};
            var b=new ActorState {id="furniture-B",player=true,connected=true,level=source.level,Position=source.Position};world.actors.Add(a);world.actors.Add(b);
            var shared=new SharedInventory(world);
            Require(shared.PrepareClaim("furniture-claim",source.id,item.id,a) && a.inventory.Count==0 &&
                !shared.PrepareClaim("furniture-claim",source.id,item.id,a) && !shared.PrepareClaim("furniture-competing",source.id,item.id,b),"Claim выдан спекулятивно/дважды двум просмотрам.");
            world=StateJson.Copy(world);shared=new SharedInventory(world);var grant=world.pendingGrants.Single();shared.CommitGrant(grant);shared.CommitGrant(grant);
            Require(world.Find(a.id).inventory.Count==1 && world.Find(b.id).inventory.Count==0 && StateJson.ToJson(world.Find(a.id).inventory[0])==expected &&
                world.containers.Single(c=>c.id==containerId).items.Count==0,"Grant/reload удвоил предмет или потерял mod extension.");
        }
        private static Dictionary<string,JToken> Extensions(string key)=>new() {{key,JToken.Parse("{\"nested\":[13,\"сохранённое поле мода\"]}")}};
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Мебель офисов53: "+reason);}
    }
}
