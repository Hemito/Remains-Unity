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
    public static class FactoryContinuationObjectsChecks
    {
        public static void Run()
        {
            FactoryContinuationObjectsArtworkAuthoring.Check();WeaponCatalog.Load();
            var room=FactoryContinuationObjectsAuthoring.Load<GameObject>(FactoryContinuationObjectsAuthoring.Path).GetComponent<RoomAuthoring>();CheckLocal(room);
            var physical=RoomPhysicalContent.Read(room,"factory:0");var doors=RoomDoorContent.Read(room,"factory:0");
            CheckPersistence(physical,doors);CheckProjection(room,physical,doors);CheckPlatformSupport(doors);
            Debug.Log("Продолжение27 objects: PASS — 4 общих контейнера/2 неподвижные опоры, 6 исходных позиций, physical6/15/15+doors5; начальный лут один раз, старые пустые/перемещённые контейнеры, aliases/extensions/удалённые ID сохранены, повторный урон не дублируется, исторические проекции и опоры проверены.");
        }
        public static void CheckLocal(RoomAuthoring room)=>CheckLocal(room,true);
        public static void Check(RoomAuthoring room)=>CheckLocal(room);
        internal static void CheckLocal(RoomAuthoring room,bool decisionsInstalled)
        {
            Require(room!=null,"Нет RoomAuthoring.");FactoryContinuationObjectsAuthoring.ValidateReport(room,decisionsInstalled);
            var physical=RoomPhysicalContent.Read(room,"factory:0");var doors=RoomDoorContent.Read(room,"factory:0");CheckNewPhysical(physical);CheckNewDoors(doors);
            var baseline=FactoryContinuationObjectsAuthoring.BeforeObjects(physical);var priorDoors=FactoryContinuationObjectsAuthoring.BeforeObjects(doors);
            Require(baseline.props.Length==6 && baseline.containers.Length==11 && baseline.loot.Length==11 && priorDoors.doors.Length==3,"Не сохранилась exact baseline17physical/3barriers.");
            foreach(var r in FactoryContinuationObjectsAuthoring.Records)
            {
                if(r.Barrier)
                {
                    var markers=room.GetComponentsInChildren<RoomDoorAuthoring>(true).Where(room.Included).Where(p=>p.Read(room,"factory:0").id==r.Id).ToArray();
                    Require(markers.Length==1 && markers[0].GetComponent<Remains.Presentation.DoorView>().Definition==FactoryContinuationObjectsArtworkAuthoring.PlatformAsset,"Нет единственного actual platform prefab: "+r.RecordId);
                }
                else
                {
                    var marker=room.GetComponentsInChildren<RoomContainerPlacement>(true).Where(room.Included).Single(p=>p.Read(room).id==r.Id);
                    var definition=FactoryContinuationObjectsAuthoring.Load<PropAsset>(FactoryContinuationObjectsArtworkAuthoring.AssetPath(r.kind));
                    var view=marker.GetComponentsInChildren<Remains.Presentation.PropView>(true).Single();
                    Require(marker.Definition==definition && StateJson.ToJson(marker.ReadLoot())==StateJson.ToJson(FactoryContinuationObjectsAuthoring.CreateLoot(r.kind)) && marker.ReadInitialItems().Length==0,
                        "Изменены SO/единственная начальная таблица лута: "+r.RecordId);
                    var source=PrefabUtility.GetCorrespondingObjectFromOriginalSource(view.gameObject);
                    Require(source==definition.Prefab.gameObject,
                        "Изменён исходный prefab preview: "+r.RecordId+", actual="+AssetDatabase.GetAssetPath(source)+", expected="+AssetDatabase.GetAssetPath(definition.Prefab.gameObject));
                    Require(Vector3.Distance(view.transform.localPosition,r.kind=="cup"?Vector3.up*.8f:Vector3.zero)<.00001f &&
                        view.transform.localRotation==definition.Prefab.transform.localRotation && view.transform.localScale==definition.Prefab.transform.localScale &&
                        view.Artwork!=null && view.Artwork.sprite==definition.Prefab.Artwork.sprite && view.Artwork.sharedMaterial==definition.Prefab.Artwork.sharedMaterial &&
                        view.VisualAnchor==definition.Prefab.VisualAnchor && view.FitsStateWidth==definition.Prefab.FitsStateWidth && view.ContainerArtwork==definition.Prefab.ContainerArtwork,
                        "Изменены фактическая графика/материал/profile/registration preview: "+r.RecordId+", position="+view.transform.localPosition+", source="+AssetDatabase.GetAssetPath(source));
                }
            }
        }
        public static void CheckNewPhysical(PhysicalLayout data)
        {
            Require(data?.patch==data?.level+":"+FactoryContinuationObjectsAuthoring.PhysicalPatch && data.props?.Length==6 && data.containers?.Length==15 && data.loot?.Length==15,
                "Не полная exact physical-v2 стадия6/15/15.");
            foreach(var r in FactoryContinuationObjectsAuthoring.Records.Where(r=>!r.Barrier))
            {
                var c=data.containers.Single(p=>p.id==data.level+":"+r.Id);var d=FactoryContinuationObjectsArtworkAuthoring.Definition(r.kind);
                Require(c.definition==r.kind && c.level==data.level && Vector2.Distance(c.Position,r.Anchor)<.00001f && c.anchorHeight==0 &&
                    c.width==d.width && c.height==d.height && c.mass==d.mass && c.movable==d.movable && c.hasPhysicalBody==d.hasPhysicalBody && !c.held && c.owner==null && c.items.Count==0,
                    "Изменены sourceanchor/body/initial state: "+r.RecordId);
                var binding=data.loot.Single(b=>b.container==c.id);
                Require(StateJson.ToJson(binding.table)==StateJson.ToJson(FactoryContinuationObjectsAuthoring.CreateLoot(r.kind)),"Изменена explicit initial table: "+r.RecordId);
            }
        }
        public static void CheckNewDoors(DoorLayout data)
        {
            Require(data?.patch==data?.level+":"+FactoryContinuationObjectsAuthoring.DoorPatch && data.doors?.Length==5,"Не полная exact doors-v2 стадия5.");
            foreach(var r in FactoryContinuationObjectsAuthoring.Records.Where(r=>r.Barrier))
            {
                var d=data.doors.Single(p=>p.id==data.level+":"+r.Id);d.Validate();
                Require(d.definition==FactoryContinuationObjectsArtworkAuthoring.PlatformId && Mathf.Abs(d.x-(r.Anchor.x-1.2f))<.00001f && Mathf.Abs(d.y-r.Anchor.y)<.00001f &&
                    d.width==2.4f && d.height==.8f && d.surfaceTexture=="door:metal" && !d.open && !d.interactive && !d.CanInteract && d.Blocks && d.integrity?.maxHealth==5000 &&
                    d.integrity.health==5000 && d.integrity.resolvedAttacks.Count==0,"Изменён stationary actual barrier: "+r.RecordId);
            }
        }
        private static void CheckPersistence(PhysicalLayout physical,DoorLayout doors)
        {
            var fresh=new WorldState {worldId="continuation-objects-proof"};Require(PhysicalContentInstaller.Apply(fresh,physical) && DoorContentInstaller.Apply(fresh,doors),"Fresh initial install отсутствует.");
            foreach(var r in FactoryContinuationObjectsAuthoring.Records.Where(r=>!r.Barrier))
            {
                var c=fresh.containers.Single(c=>c.id==physical.level+":"+r.Id);var items=FactoryContinuationObjectsAuthoring.CreateLoot(r.kind).Generate(fresh.worldId,c.id);
                Require(StateJson.ToJson(c.items)==StateJson.ToJson(items),"Fresh fixed loot изменён: "+c.id);
            }
            var before=StateJson.ToJson(fresh);Require(!PhysicalContentInstaller.Apply(fresh,physical) && !DoorContentInstaller.Apply(fresh,doors) && StateJson.ToJson(fresh)==before,"Повтор fresh install изменил мир.");
            var prior=FactoryContinuationObjectsAuthoring.BeforeObjects(physical);var oldDoors=FactoryContinuationObjectsAuthoring.BeforeObjects(doors);
            var saved=new WorldState {worldId=fresh.worldId};PhysicalContentInstaller.Apply(saved,prior);DoorContentInstaller.Apply(saved,oldDoors);
            var retained=saved.containers[0];retained.items.Clear();retained.x+=.37f;retained.owner="saved-owner";retained.generation=7;retained.held=true;
            retained.itemAliases??=new();retained.extensions??=new Dictionary<string,JToken>();
            retained.itemAliases["old-alias"]="taken-item";retained.extensions["unknown-mod-field"]=JToken.Parse("{\"nested\":[1,2]}");
            string retainedJson=StateJson.ToJson(retained),removed=saved.containers[^1].id;saved.containers.RemoveAt(saved.containers.Count-1);
            PhysicalContentInstaller.Apply(saved,physical);DoorContentInstaller.Apply(saved,doors);
            Require(ReferenceEquals(retained,saved.containers.Find(c=>c.id==retained.id)) && StateJson.ToJson(retained)==retainedJson && saved.containers.All(c=>c.id!=removed),"Обновление пополнило/восстановило старый контейнер или изменило ссылки/aliases/pose/extensions.");
            foreach(var c in saved.containers.Where(c=>FactoryContinuationObjectsAuthoring.Records.Any(r=>physical.level+":"+r.Id==c.id))) c.items.Clear();
            string platformId=doors.level+":"+FactoryContinuationObjectsAuthoring.Records.Single(r=>r.record==50).Id;
            var platform=saved.doors.Single(d=>d.id==platformId);string reason;
            Require(DoorDamageOperations.Apply(platform,"proof-destroy",5000,out reason)==DoorChange.Changed && platform.Destroyed && !platform.Blocks &&
                DoorDamageOperations.Apply(platform,"proof-destroy",5000,out reason)==DoorChange.Unchanged,"Stationary destruction receipt не durable/dedup.");
            saved=StateJson.Copy(saved);before=StateJson.ToJson(saved);
            Require(!PhysicalContentInstaller.Apply(saved,physical) && !DoorContentInstaller.Apply(saved,doors) && StateJson.ToJson(saved)==before,"Reload пополнил шкаф/восстановил платформу.");
        }
        private static void CheckProjection(RoomAuthoring room,PhysicalLayout physical,DoorLayout doors)
        {
            string snapshot=FactoryContinuationObjectsAuthoring.Snapshot(room);Require(snapshot!="{}" && snapshot.Contains("geometry") && snapshot.Contains("records"),"Snapshot оказался пустым property DTO.");
            var projected=FactoryContinuationObjectsAuthoring.BeforeObjects(physical);projected.containers[0].extensions??=new Dictionary<string,JToken>();
            projected.containers[0].extensions["unknown"]=JToken.Parse("[1,2]");
            var extended=StateJson.Copy(physical);var matching=extended.containers.Single(c=>c.id==projected.containers[0].id);
            matching.extensions??=new Dictionary<string,JToken>();matching.extensions["unknown"]=JToken.Parse("[1,2]");
            Require(StateJson.ToJson(FactoryContinuationObjectsAuthoring.BeforeObjects(extended))==StateJson.ToJson(projected),"Projection изменила неизвестное сохранённое поле predecessor.");
            var partial=StateJson.Copy(physical);partial.containers=partial.containers.Where(c=>c.id!=physical.level+":"+FactoryContinuationObjectsAuthoring.Records[0].Id).ToArray();
            Reject(()=>FactoryContinuationObjectsAuthoring.BeforeObjects(partial),"Partial addition принимается.");partial=StateJson.Copy(physical);partial.patch=physical.level+":"+FactoryContinuationObjectsAuthoring.PhysicalPrevious;
            Reject(()=>FactoryContinuationObjectsAuthoring.BeforeObjects(partial),"Новое ID под oldpatch принимается.");
            var broken=StateJson.Copy(doors);broken.doors.First(d=>d.definition==FactoryContinuationObjectsArtworkAuthoring.PlatformId).width=.8f;
            Reject(()=>FactoryContinuationObjectsAuthoring.BeforeObjects(broken),"Изменённый platform body незаметно проектируется.");
        }
        private static void CheckPlatformSupport(DoorLayout doors)
        {
            var platforms=doors.doors.Where(d=>d.definition==FactoryContinuationObjectsArtworkAuthoring.PlatformId).Select(StateJson.Copy).ToArray();
            var room=new RoomLayout {platforms=DoorGeometry.Compose(Array.Empty<RoomSurface>(),platforms,doors.level)};
            foreach(var p in platforms) Require(RoomSupport.IsSupported(room,new Vector2(p.x+p.width*.5f,p.y+p.height)) && !p.CanInteract,"Platform не реальная support geometry либо E активен.");
            foreach(var p in platforms) DoorDamageOperations.Apply(p,"support-destroy-"+p.id,5000,out _);
            Require(DoorGeometry.Compose(Array.Empty<RoomSurface>(),platforms,doors.level).Length==0,"Destroyed platform всё ещё даёт опору.");
        }
        private static void Reject(Action action,string reason) {bool rejected=false;try {action();}catch(InvalidOperationException){rejected=true;}Require(rejected,reason);}
        private static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException("Проверка objects27: "+reason);}
    }
}
