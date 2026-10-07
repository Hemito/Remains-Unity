using System;
using System.Collections.Generic;
using System.IO;
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
    public static class FactoryWarehouseFurnitureChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();PropCatalog.Load();ActorTypes.Load();Anatomy.Load();
            FactoryWarehouseFurnitureAuthoring.Verify();
            var local=FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseDraft.Path).GetComponent<RoomAuthoring>();
            var connected=FactoryWarehouseSource.Asset<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>();
            CheckPersistence(RoomContentBatch.Read(local,"factory:0"),false);
            CheckPersistence(RoomContentBatch.Read(connected,"factory:0"),true);
            CheckNegatives(RoomContentBatch.Read(connected,"factory:0"),connected.CreateLayout());
            Debug.Log("Мебель склада40: PASS четыре source ID/anchors,18props/19containers/19loot,53/57 records, исходная геометрия, fresh/repeated/cumulative installs без refill/resurrection и точные исторические проекции.");
        }
        public static void CheckLocal(RoomAuthoring room)=>CheckRoom(room,false);
        public static void CheckConnected(RoomAuthoring room)=>CheckRoom(room,true);
        private static void CheckRoom(RoomAuthoring room,bool connected)
        {
            Require(room!=null,"Нет авторской комнаты.");
            var batch=RoomContentBatch.Read(room,"factory:0");var physical=batch.physical;
            Require(physical.patch==physical.level+":"+(connected?FactoryWarehouseFurnitureAuthoring.ConnectedPatch:FactoryWarehouseFurnitureAuthoring.LocalPatch),"Неверная текущая физическая версия.");
            var prior=FactoryWarehouseFurnitureAuthoring.BeforeFurniture(physical);
            CheckPredecessor(prior,connected);CheckNewRecords(physical,connected?115.2f:0);
            if(connected)FactoryWarehouseHistoryProjection.BeforeWarehouse(prior);
            if(!connected)FactoryWarehouseFurnitureAuthoring.ValidateReport(room,true,FactoryWarehouseSource.Load());
            else
            {
                var modules=room.GetComponentsInChildren<RoomAuthoring>(true);
                var warehouse=modules.Single(r=>r.GetComponent<RoomImportAudit>()?.Report?.SourceId==FactoryWarehouseSource.SourceId);
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(warehouse.gameObject)==FactoryWarehouseDraft.Path &&
                    Vector3.Distance(warehouse.transform.localPosition,new Vector3(115.2f,0,0))<.00001f,"Nested склад потерял prefab/offset.");
                FactoryWarehouseFurnitureAuthoring.ValidateReport(warehouse,true,FactoryWarehouseSource.Load());
            }
            foreach(var record in FactoryWarehouseFurnitureAuthoring.Records)
            {
                var markers=room.GetComponentsInChildren<RoomPhysicalPlacement>(true).Where(room.Included).Where(p=>p.Read(room).id==record.Id).ToArray();
                Require(markers.Length==1,"Нет одного marker: "+record.Id);var marker=markers[0];
                var asset=FactoryWarehouseSource.Asset<PropAsset>(FactoryWarehouseFurnitureArtworkAuthoring.AssetPath(record.Type));
                Require(marker.Definition==asset && (marker is RoomContainerPlacement)==record.Container,"Marker читает чужой SO/type: "+record.Id);
                if(marker is RoomContainerPlacement container)Require(container.ReadInitialItems().Length==0 && StateJson.ToJson(container.ReadLoot())==StateJson.ToJson(FactoryWarehouseFurnitureAuthoring.CreateLoot(record.Type)),"Два источника/чужой initial loot: "+record.Id);
                var previews=marker.GetComponentsInChildren<PropView>(true);var definition=asset.CreateDefinition();
                Require(previews.Length==1 && PrefabUtility.GetCorrespondingObjectFromOriginalSource(previews[0].gameObject)==asset.Prefab.gameObject &&
                    Vector3.Distance(previews[0].transform.localPosition,asset.Prefab.VisualAnchor==PropVisualAnchor.BodyCenter?Vector3.up*(definition.height*.5f):Vector3.zero)<.00001f &&
                    previews[0].transform.localRotation==asset.Prefab.transform.localRotation && previews[0].transform.localScale==asset.Prefab.transform.localScale &&
                    previews[0].Artwork.sprite==asset.Prefab.Artwork.sprite && previews[0].Artwork.sharedMaterial==asset.Prefab.Artwork.sharedMaterial &&
                    previews[0].VisualAnchor==asset.Prefab.VisualAnchor && previews[0].FitsStateWidth==asset.Prefab.FitsStateWidth,"Native preview потерял исходный prefab/registration/материал: "+record.Id);
            }
            CheckGeometry(room.CreateLayout(false),physical,batch.npcs.actors,batch.doors.doors);
        }
        internal static void CheckPredecessor(PhysicalLayout source,bool connected,string projectRoot=null)
        {
            Require(source!=null && source.props!=null && source.containers!=null && source.loot!=null &&
                source.patch==source.level+":"+(connected?FactoryWarehouseConnectionAuthoring.PhysicalPatch:FactoryWarehouseDraft.PhysicalPatch) &&
                source.props.Length==(connected?29:17) && source.containers.Length==(connected?52:16) && source.loot.Length==(connected?50:16),"Неполная predecessor v7/v1.");
            var all=source.props.Cast<PropState>().Concat(source.containers).ToArray();var ids=FactoryWarehouseFurnitureAuthoring.NewIds(source.level);
            Require(all.All(p=>p!=null && !string.IsNullOrWhiteSpace(p.id) && p.level==source.level) && all.Select(p=>p.id).Distinct(StringComparer.Ordinal).Count()==all.Length &&
                source.loot.All(b=>b!=null && b.table!=null && !string.IsNullOrWhiteSpace(b.container)) && source.loot.Select(b=>b.container).Distinct(StringComparer.Ordinal).Count()==source.loot.Length &&
                !all.Any(p=>ids.Contains(p.id)) && !source.loot.Any(b=>ids.Contains(b.container)),"Predecessor содержит повторный/чужой/new furniture ID.");
            if(!connected)Require(source.expansion==null,"Local predecessor уже содержит неизвестное дополнение.");
            string root=projectRoot??Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            var plan=JObject.Parse(File.ReadAllText(Path.Combine(root,"Tools/Research/FactoryWarehouse40/content-plan.json")),new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            var entries=(plan["records"] as JArray)?.Cast<JObject>().ToArray();
            Require((int?)plan["schema"]==1 && (int?)plan["sourceObjects"]==57 && entries?.Length==57,"Нет полного source плана склада для исторической проекции.");
            foreach(string kind in new[]{"planned-existing-prop","planned-existing-container"})
            {
                var expected=entries.Where(r=>(string)r["disposition"]==kind).ToArray();
                Require(expected.Length==(kind=="planned-existing-prop"?17:16),"Изменён полный predecessor source набор: "+kind);
                var actual=(kind=="planned-existing-prop"?source.props.Cast<PropState>():source.containers.Cast<PropState>()).Where(p=>p.id.StartsWith(source.level+":"+FactoryWarehouseFurnitureAuthoring.SourcePrefix,StringComparison.Ordinal)).ToArray();
                Require(FactoryWarehouseFurnitureAuthoring.Same(actual.Select(p=>p.id),expected.Select(r=>source.level+":"+(string)r["entityIdSuffix"])) &&
                    expected.All(r=>actual.Any(p=>p.id==source.level+":"+(string)r["entityIdSuffix"] && p.definition==(string)r["kind"])),"Потерян/подменён прежний source ID/тип склада: "+kind);
            }
        }
        public static void CheckNewRecords(PhysicalLayout source,float offset)
        {
            Require(source!=null && !string.IsNullOrWhiteSpace(source.level) && source.props!=null && source.containers!=null && source.loot!=null,"Нет физических массивов.");
            var ids=FactoryWarehouseFurnitureAuthoring.NewIds(source.level);var all=source.props.Cast<PropState>().Concat(source.containers).ToArray();
            Require(all.All(p=>p!=null && !string.IsNullOrWhiteSpace(p.id)) && all.Select(p=>p.id).Distinct(StringComparer.Ordinal).Count()==all.Length &&
                source.loot.All(b=>b!=null && b.table!=null && !string.IsNullOrWhiteSpace(b.container)) && source.loot.Select(b=>b.container).Distinct(StringComparer.Ordinal).Count()==source.loot.Length &&
                all.Count(p=>ids.Contains(p.id))==4 && source.props.Count(p=>ids.Contains(p.id))==1 && source.containers.Count(p=>ids.Contains(p.id))==3 && source.loot.Count(b=>ids.Contains(b.container))==3,"Нет точных четырёх ID/трёх таблиц.");
            foreach(var record in FactoryWarehouseFurnitureAuthoring.Records)
            {
                var state=all.Single(p=>p.id==source.level+":"+record.Id);var definition=FactoryWarehouseFurnitureArtworkAuthoring.Definition(record.Type);
                PropBodyRules.Validate(state);PropState expected=record.Container?new ContainerState():new PropState();
                expected.id=state.id;expected.definition=record.Type;expected.level=source.level;expected.x=record.Anchor.x+offset;expected.y=record.Anchor.y;
                expected.width=definition.width;expected.height=definition.height;expected.anchorHeight=0;expected.mass=definition.mass;expected.movable=definition.movable;expected.hasPhysicalBody=definition.hasPhysicalBody;
                // Source->Unity float arithmetic can differ by a few ULP in an actual nested transform.
                Require(Vector2.Distance(state.Position,expected.Position)<.00001f,"Изменён source anchor: "+state.id);
                expected.x=state.x;expected.y=state.y;
                Require(StateJson.ToJson(state)==StateJson.ToJson(expected),"Изменён полный authored state/тип/размер/масса/extensions: "+state.id);
                if(record.Container)Require(StateJson.ToJson(source.loot.Single(b=>b.container==state.id).table)==StateJson.ToJson(FactoryWarehouseFurnitureAuthoring.CreateLoot(record.Type)),"Изменена таблица initial loot: "+state.id);
            }
        }
        public static void CheckGeometry(RoomLayout room,PhysicalLayout physical,ActorState[] actors,DoorState[] doors)
        {
            Require(room!=null && actors!=null && doors!=null,"Нет geometry/NPC/door для проверки.");
            var all=physical.props.Cast<PropState>().Concat(physical.containers).ToArray();var ids=FactoryWarehouseFurnitureAuthoring.NewIds(physical.level);
            foreach(var state in all.Where(p=>ids.Contains(p.id)))
            {
                var body=new Rect(state.x-state.width*.5f,state.y,state.width,state.height);
                Require(body.xMin>=room.left-.00001f && body.xMax<=room.right+.00001f && body.yMin>=room.bottom-.00001f && body.yMax<=room.top+.00001f,"Объект вне комнаты: "+state.id);
                if(state is ContainerState container)Require(((InventorySource)container).Bounds==body,"E bounds не читают единственный prop state: "+state.id);
                if(!state.hasPhysicalBody)
                {
                    Require(state.definition=="locker" && !state.movable && state.mass==0 && !new TelekinesisTarget(state).Movable,"Wall1 шкаф получил физическую опору/TK.");
                    var above=new PropState {id="locker-support-check",x=state.x,y=state.y+state.height,width=.2f,height=.2f,mass=1,level=state.level};
                    Require(!PropSupport.Supported(above,above.Position,new[]{state}),"NoBody шкаф создаёт опору.");continue;
                }
                var hull=new MovementHull {width=state.width,height=state.height};
                Require(ActorTerrainMotion.IsClear(room,hull,state.Position) && ActorTerrainMotion.Supported(room,hull,state.Position),"Исходное тело пересекает terrain/не имеет опоры: "+state.id);
                foreach(var other in all.Where(p=>p.id!=state.id && p.hasPhysicalBody))Require(!body.Overlaps(new Rect(other.x-other.width*.5f,other.y-other.anchorHeight,other.width,other.height)),"Мебель пересекает физический груз: "+state.id+" / "+other.id);
                foreach(var actor in actors)
                {var a=Anatomy.Movement(actor);Require(!body.Overlaps(new Rect(actor.x-a.width*.5f,actor.y,a.width,a.height)),"Мебель пересекает NPC: "+state.id+" / "+actor.id);}
                foreach(var door in doors)Require(!body.Overlaps(door.Bounds),"Мебель пересекает authored дверь: "+state.id+" / "+door.id);
            }
        }
        public static void CheckPersistence(RoomContentBatch batch,bool connected)
        {
            var layout=batch.physical;FactoryWarehouseFurnitureAuthoring.BeforeFurniture(layout);var ids=FactoryWarehouseFurnitureAuthoring.NewIds(layout.level);
            var fresh=new WorldState {worldId="warehouse-furniture-fresh",mechanismVersion=1};
            Require(RoomContentInstaller.Apply(fresh,batch),"Fresh мир не устанавливается.");
            var duplicate=new WorldState {worldId=fresh.worldId,mechanismVersion=1};RoomContentInstaller.Apply(duplicate,batch);
            Require(StateJson.ToJson(fresh.containers)==StateJson.ToJson(duplicate.containers),"Лут недетерминирован для того же WorldId/ID.");
            foreach(var record in FactoryWarehouseFurnitureAuthoring.Records.Where(r=>r.Container))
            {var container=fresh.containers.Single(c=>c.id==layout.level+":"+record.Id);Require(StateJson.ToJson(container.items)==StateJson.ToJson(FactoryWarehouseFurnitureAuthoring.CreateLoot(record.Type).Generate(fresh.worldId,container.id)),"Не один фиксированный shared initial loot: "+container.id);}
            var saved=StateJson.Copy(fresh);saved.props.RemoveAll(p=>p.id==ids[1]);saved.containers.RemoveAll(c=>c.id==ids[2]);
            foreach(var container in saved.containers)container.items.Clear();
            string complete=StateJson.ToJson(saved);Require(!RoomContentInstaller.Apply(saved,batch) && StateJson.ToJson(saved)==complete,"Повторный вход resurrect/refill изменил saved мир.");
            foreach(var step in new[]{(ContentExpansionStep)layout.expansion}.Concat(layout.expansion.earlierVersions))
            {
                var world=StateJson.Copy(fresh);world.contentPatches.Remove(layout.patch);world.contentPatches.Add(step.previousPatch);
                world.props.RemoveAll(p=>step.additions.Contains(p.id));world.containers.RemoveAll(c=>step.additions.Contains(c.id));
                string removedProp=world.props[0].id,removedContainer=world.containers[0].id;world.props.RemoveAt(0);world.containers.RemoveAt(0);
                var cargo=world.props[0];cargo.x+=.13f;cargo.y+=.27f;cargo.owner="saved-owner";cargo.generation=9;cargo.sequence=17;cargo.held=true;cargo.extensions=Extensions("cargo");
                foreach(var container in world.containers){container.items.Clear();container.extensions=Extensions("empty");}
                var deposit=world.containers[0];var item=new ItemStack {id="custom-deposit",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=3,revision=21,extensions=Extensions("weapon")},extensions=Extensions("item")};
                deposit.items.Add(item);deposit.itemAliases["old-alias"]=item.id;
                if(world.actors.Count>0){var actor=world.actors[0];actor.health=0;actor.down=true;actor.x+=.2f;actor.owner="saved-body";actor.generation=11;actor.extensions=Extensions("actor");}
                if(world.doors.Count>0){world.doors[0].open=true;world.doors[0].revision=8;world.doors[0].extensions=Extensions("door");}
                var retainedProps=world.props.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();var retainedContainers=world.containers.Select(c=>(c.id,json:StateJson.ToJson(c))).ToArray();
                string other=OtherWorld(world);Require(PhysicalContentInstaller.Apply(world,layout),"Predecessor не обновляется: "+step.previousPatch);
                Require(!world.props.Any(p=>p.id==removedProp) && !world.containers.Any(c=>c.id==removedContainer) && OtherWorld(world)==other &&
                    retainedProps.All(p=>world.props.Any(a=>a.id==p.id && StateJson.ToJson(a)==p.json)) && retainedContainers.All(c=>world.containers.Any(a=>a.id==c.id && StateJson.ToJson(a)==c.json)),"Дополнение заменило saved cargo/empty/deposit/alias/body/door или resurrect удалённый прежний ID.");
                Require(world.props.Count==retainedProps.Length+layout.props.Count(p=>step.additions.Contains(p.id)) && world.containers.Count==retainedContainers.Length+layout.containers.Count(c=>step.additions.Contains(c.id)),"Установлены не только additions выбранной версии.");
                var reload=StateJson.Copy(world);string before=StateJson.ToJson(reload);Require(!RoomContentInstaller.Apply(reload,batch) && StateJson.ToJson(reload)==before,"Serialization/reload повторно изменил мир.");
            }
            // Existing IDs before the first-ever patch keep custom inventory/alias/pose instead of initial loot.
            foreach(bool deposited in new[]{false,true})
            {
                var already=new WorldState {worldId="warehouse-furniture-existing-"+deposited};var existing=StateJson.Copy(layout.containers.Single(c=>c.id==ids[0]));
                existing.x+=.31f;existing.owner="saved-owner";existing.generation=7;existing.sequence=19;existing.extensions=Extensions("container");
                if(deposited){existing.items.Add(new ItemStack {id="already-owned",definition="caps",count=2,extensions=Extensions("existing")});existing.itemAliases["prior-alias"]="already-owned";}
                already.containers.Add(existing);string exact=StateJson.ToJson(existing);PhysicalContentInstaller.Apply(already,layout);
                Require(StateJson.ToJson(already.containers.Single(c=>c.id==existing.id))==exact,"Первый patch наполнил/заменил существующий empty/custom stable ID.");
            }
            CheckClaim(fresh,ids[0]);
        }
        private static void CheckNegatives(RoomContentBatch batch,RoomLayout geometry)
        {
            var current=batch.physical;string before=StateJson.ToJson(current);var original=FactoryWarehouseFurnitureAuthoring.BeforeFurniture(current);
            Require(StateJson.ToJson(current)==before,"Projection изменила вход.");
            var missing=StateJson.Copy(current);missing.containers=missing.containers.Where(c=>c.id!=FactoryWarehouseFurnitureAuthoring.NewIds(current.level)[2]).ToArray();Reject(()=>FactoryWarehouseFurnitureAuthoring.BeforeFurniture(missing),"partial addition");
            var hidden=StateJson.Copy(original);hidden.props[0]=StateJson.Copy(current.props.Single(p=>p.id==FactoryWarehouseFurnitureAuthoring.NewIds(current.level)[1]));Reject(()=>FactoryWarehouseFurnitureAuthoring.BeforeFurniture(hidden),"old patch hides new ID");
            var chain=StateJson.Copy(current);chain.expansion.earlierVersions[0].additions=chain.expansion.earlierVersions[0].additions.Skip(1).ToArray();Reject(()=>FactoryWarehouseFurnitureAuthoring.BeforeFurniture(chain),"incomplete cumulative chain");
            var table=StateJson.Copy(current);table.loot.Single(b=>b.container==FactoryWarehouseFurnitureAuthoring.NewIds(current.level)[0]).table.entries[0].maximum++;Reject(()=>FactoryWarehouseFurnitureAuthoring.BeforeFurniture(table),"changed loot");
            var moved=StateJson.Copy(current);moved.props.Single(p=>p.id==FactoryWarehouseFurnitureAuthoring.NewIds(current.level)[1]).x+=.1f;Reject(()=>FactoryWarehouseFurnitureAuthoring.BeforeFurniture(moved),"changed source anchor");
            var addedExtensions=StateJson.Copy(current);addedExtensions.containers.Single(c=>c.id==FactoryWarehouseFurnitureAuthoring.NewIds(current.level)[0]).extensions=Extensions("unknown-new-state");Reject(()=>FactoryWarehouseFurnitureAuthoring.BeforeFurniture(addedExtensions),"unverified addition state");
            var preserved=StateJson.Copy(current);preserved.props.First(p=>!FactoryWarehouseFurnitureAuthoring.NewIds(current.level).Contains(p.id)).extensions=Extensions("predecessor");
            string kept=StateJson.ToJson(preserved.props.First(p=>!FactoryWarehouseFurnitureAuthoring.NewIds(current.level).Contains(p.id)));
            Require(FactoryWarehouseFurnitureAuthoring.BeforeFurniture(preserved).props.Any(p=>StateJson.ToJson(p)==kept),"Projection потеряла unknown predecessor поля.");
            string snapshot=FactoryWarehouseFurnitureAuthoring.Snapshot(geometry,batch,original);var changed=StateJson.Copy(geometry);changed.platforms[0].x+=.25f;
            Require(snapshot!=FactoryWarehouseFurnitureAuthoring.Snapshot(changed,batch,original),"Snapshot скрывает изменение geometry (public-field DTO).");
            var changedBatch=StateJson.Copy(batch);changedBatch.doors.doors[0].open=!changedBatch.doors.doors[0].open;
            Require(snapshot!=FactoryWarehouseFurnitureAuthoring.Snapshot(geometry,changedBatch,original),"Snapshot скрывает изменение door.");
            var oldState=StateJson.Copy(original);oldState.props[0].x+=.25f;Require(snapshot!=FactoryWarehouseFurnitureAuthoring.Snapshot(geometry,batch,oldState),"Snapshot скрывает изменение прежнего physical state.");
            var removedSupport=StateJson.Copy(geometry);var safe=current.containers.Single(c=>c.definition=="safe");removedSupport.platforms=removedSupport.platforms.Where(s=>Mathf.Abs(s.y+s.height-safe.y)>.0001f).ToArray();
            Reject(()=>CheckGeometry(removedSupport,current,batch.npcs.actors,batch.doors.doors),"missing actual support");
        }
        private static void CheckClaim(WorldState world,string id)
        {
            world=StateJson.Copy(world);var source=world.containers.Single(c=>c.id==id);var item=source.items.First();item.extensions=Extensions("claim");string expected=StateJson.ToJson(item);
            var a=new ActorState {id="warehouse-furniture-A",player=true,connected=true,level=source.level,Position=source.Position};
            var b=new ActorState {id="warehouse-furniture-B",player=true,connected=true,level=source.level,Position=source.Position};world.actors.Add(a);world.actors.Add(b);
            var shared=new SharedInventory(world);
            Require(shared.PrepareClaim("warehouse-furniture-claim",source.id,item.id,a) && a.inventory.Count==0 &&
                !shared.PrepareClaim("warehouse-furniture-claim",source.id,item.id,a) && !shared.PrepareClaim("warehouse-furniture-competing",source.id,item.id,b),"Shared claim выдан спекулятивно/дважды.");
            world=StateJson.Copy(world);shared=new SharedInventory(world);var grant=world.pendingGrants.Single();shared.CommitGrant(grant);shared.CommitGrant(grant);
            Require(world.Find(a.id).inventory.Count==1 && world.Find(b.id).inventory.Count==0 && StateJson.ToJson(world.Find(a.id).inventory[0])==expected &&
                !world.containers.Single(c=>c.id==id).items.Any(i=>i.id==item.id),"Restore/duplicate grant удвоил shared loot/потерял extensions.");
        }
        private static Dictionary<string,JToken> Extensions(string tag)=>new() {["furniture-check"]=JToken.Parse("{\"tag\":\""+tag+"\",\"nested\":[17,true]}")};
        private static string OtherWorld(WorldState world)=>StateJson.ToJson(world.actors)+StateJson.ToJson(world.doors)+StateJson.ToJson(world.movingPlatforms)+StateJson.ToJson(world.mechanismButtons);
        private static void Reject(Action action,string context)
        {bool rejected=false;try {action();}catch(InvalidOperationException){rejected=true;}Require(rejected,"Не отклонён отрицательный случай: "+context);}
        private static void Require(bool valid,string message)=>FactoryWarehouseFurnitureAuthoring.Require(valid,message);
    }
}
