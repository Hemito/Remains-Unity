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
    public static class FactoryOfficeOpeningsChecks
    {
        public static void Run()
        {
            PropCatalog.Load();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            FactoryOfficeOpeningsAuthoring.CheckAssets();Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        }
        public static bool CheckOfficeStage(RoomAuthoring office)
        {
            var data=FactoryOfficeOpeningsAuthoring.ReadData();var layout=RoomDoorContent.Read(office,"factory:0");
            bool installed=layout.patch==layout.level+":"+data.officePatch;var projected=Project(layout,data);
            Require(projected.patch==layout.level+":"+data.officePrevious && projected.doors.Length==3,"Неизвестная или неполная офисная дверная версия.");
            if(!installed) Require(!layout.doors.Any(d=>data.placements.Any(p=>d.id==layout.level+":"+p.Id)),"Прежняя версия скрывает частичное дополнение.");
            FactoryOfficeOpeningsAuthoring.ValidateSource(office.GetComponent<RoomImportAudit>().Report,installed);return installed;
        }
        // Detached projection for historical assertions; rejects incomplete six-ID additions.
        public static DoorLayout BeforeOpenings(DoorLayout source)=>Project(source,FactoryOfficeOpeningsAuthoring.ReadData());
        // Office save updated the nested instance, while the root patch still records v2.
        internal static DoorLayout ConnectedPredecessorForResume(DoorLayout source,FactoryOfficeOpeningsAuthoring.Data data)
        {
            Require(source.patch==source.level+":"+data.connectedPrevious && source.doors.Length==15,"Неизвестный interrupted root stage.");
            ValidateNew(source,data,76.8f);var result=StateJson.Copy(source);var ids=data.placements.Select(p=>source.level+":"+p.Id).ToArray();
            result.doors=result.doors.Where(d=>!ids.Contains(d.id)).ToArray();FactoryOfficeOpeningsAuthoring.CreateExpansion(result,true,data);return result;
        }
        private static DoorLayout Project(DoorLayout source,FactoryOfficeOpeningsAuthoring.Data data)
        {
            string prefix=source.level+":";bool connected=source.patch==prefix+data.connectedPatch,office=source.patch==prefix+data.officePatch;
            if(!connected && !office) return source;
            ValidateNew(source,data,connected?76.8f:0);Require(source.doors.Length==(connected?15:9),"Неполное дополнение скрыто predecessor projection.");
            var result=StateJson.Copy(source);var ids=data.placements.Select(p=>prefix+p.Id).ToArray();
            result.doors=result.doors.Where(d=>!ids.Contains(d.id)).ToArray();result.patch=prefix+(connected?data.connectedPrevious:data.officePrevious);
            Require(source.expansion?.previousPatch==result.patch,"Неверный ближайший predecessor.");
            if(!connected) {Require(source.expansion.earlierVersions.Length==0,"Неожиданная офисная цепочка.");result.expansion=null;}
            else
            {
                Require(source.expansion.earlierVersions.Length==1,"Потеряна cumulative root v1→v2→v3 chain.");
                var step=source.expansion.earlierVersions[0];result.expansion=new ContentExpansion {previousPatch=step.previousPatch,
                    additions=step.additions.Where(id=>!ids.Contains(id)).ToArray(),earlierVersions=Array.Empty<ContentExpansionStep>()};
            }
            var expected=FactoryOfficeOpeningsAuthoring.CreateExpansion(result,connected,data);
            Require(StateJson.ToJson(source.expansion)==StateJson.ToJson(ContentExpansionRules.InLevel(expected,source.level)),"Изменены точные additions/earlierVersions.");return result;
        }
        public static void CheckOffice(RoomAuthoring office)
        {
            Require(CheckOfficeStage(office),"Шесть unique проёмов ещё не установлены.");var data=FactoryOfficeOpeningsAuthoring.ReadData();
            var batch=RoomContentBatch.Read(office,"factory:0");var report=office.GetComponent<RoomImportAudit>().Report;
            Require(report.Records.Count(r=>r.installed)==44 && report.Records.Where(r=>!r.installed).Select(r=>r.recordId).OrderBy(id=>id)
                .SequenceEqual(new[]{"original:plant:53:record:66","original:plant:53:record:67"}),"Не сохранены ровно две pending lov категории.");
            Require(batch.physical.props.Length==6 && batch.physical.containers.Length==21 && batch.physical.loot.Length==20 && batch.npcs.actors.Length==5,
                "Изменены прежние физические объекты/лут/NPC.");
            CheckPlacements(office,batch,data,0);CheckGeometry(office,batch,data);CheckPersistence(batch,RoomDoorContent.Read(office,"factory:6"),data);
        }
        public static void Check(RoomAuthoring room)
        {
            var data=FactoryOfficeOpeningsAuthoring.ReadData();var office=room.GetComponentsInChildren<RoomAuthoring>(true)
                .Single(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53");
            CheckOffice(office);var batch=RoomContentBatch.Read(room,"factory:0");Project(batch.doors,data);
            Require(batch.doors.patch=="factory:0:"+data.connectedPatch && batch.doors.doors.Length==15 && batch.physical.props.Length==12 &&
                batch.physical.containers.Length==36 && batch.physical.loot.Length==33 && batch.npcs.actors.Length==13 &&
                batch.mechanisms.movingPlatforms.Length==1 && batch.mechanisms.mechanismButtons.Length==2,"Изменено прежнее содержимое связного завода.");
            CheckPlacements(room,batch,data,76.8f);CheckGeometry(room,batch,data);CheckPersistence(batch,RoomDoorContent.Read(room,"factory:6"),data);
            Debug.Log("Проёмы офисов53: PASS — 6 SO/6 unique placements + reference28, прежние9 дверей сохранены; офис44/46,2 lov pending, root15 дверей; source registration/terrain unchanged, E отдельно, additive save/no resurrection/extensions. Нативный бой/занятый проём/обе реплики и исходная игра этим check не подтверждены.");
        }
        private static void ValidateNew(DoorLayout source,FactoryOfficeOpeningsAuthoring.Data data,float offset)
        {
            var ids=data.placements.Select(p=>source.level+":"+p.Id).ToArray();
            Require(source.doors.Count(d=>ids.Contains(d.id))==6 && source.doors.Where(d=>ids.Contains(d.id)).Select(d=>d.id).Distinct().Count()==6,"Нет ровно шести unique source IDs.");
            foreach(var placement in data.placements)
            {
                var d=source.doors.Single(d=>d.id==source.level+":"+placement.Id);var definition=data.definitions.Single(d=>d.type==placement.type);d.Validate();var p=placement.Position+Vector2.right*offset;
                Require(d.definition==definition.id && d.level==source.level && FactoryLadderLandingChecks.SameRect(d.Bounds,p.x-definition.Size.x*.5f,p.y,definition.Size.x,definition.Size.y) &&
                    d.interactive==definition.interactive && d.integrity?.version==1 && d.integrity.maxHealth==definition.maxHealth && d.integrity.health==definition.maxHealth &&
                    d.integrity.resolvedAttacks.Count==0 && d.revision==0 && !d.open && !d.Destroyed && d.Blocks && d.surfaceTexture==definition.surfaceTexture &&
                    d.interaction?.version==1 && FactoryLadderLandingChecks.SameRect(d.InteractionBounds,p.x-definition.InteractionSize.x*.5f,p.y,definition.InteractionSize.x,definition.InteractionSize.y),
                    "Authored state не читает SO/anchor/interaction/integrity: "+d.id);
            }
            Require(!source.doors.Any(d=>d.id==source.level+":"+FactoryOfficeOpeningsAuthoring.Prefix+data.reference.code),"Reference28 стал второй world entity.");
            foreach(string code in new[]{"NMeCLKXlWsv6CYYL","foMRNLiLOHCLfItd","ffoNKTl2qsZVBCG3"})
            {
                var old=source.doors.Single(d=>d.id==source.level+":"+FactoryOfficeOpeningsAuthoring.Prefix+code);
                Require(old.definition=="door1" && old.integrity==null && old.interaction==null && old.interactive,"Прежняя дверь получила новые capabilities: "+old.id);
            }
        }
        private static void CheckPlacements(RoomAuthoring room,RoomContentBatch batch,FactoryOfficeOpeningsAuthoring.Data data,float offset)
        {
            ValidateNew(batch.doors,data,offset);
            foreach(var entry in data.placements)
            {
                var marker=room.GetComponentsInChildren<RoomDoorAuthoring>(true).Where(room.Included).Single(m=>m.Read(room,batch.doors.level).id==entry.Id);
                var definition=data.definitions.Single(d=>d.type==entry.type);var view=marker.GetComponent<DoorView>();
                var prefab=FactoryOfficeOpeningsAuthoring.Asset<GameObject>(FactoryOfficeOpeningsAuthoring.PrefabPath(definition.id));
                Require(PrefabUtility.GetCorrespondingObjectFromOriginalSource(view.gameObject)==prefab && view.Definition==FactoryOfficeOpeningsAuthoring.Asset<DoorAsset>(FactoryOfficeOpeningsAuthoring.AssetPath(definition.id)) &&
                    view.Artwork.sharedMaterial==prefab.GetComponent<DoorView>().Artwork.sharedMaterial && view.Artwork.sprite==view.Definition.Closed,"Nested original prefab/SO/artwork/material изменён: "+entry.Id);
                Require(marker.GetComponentsInChildren<RoomPhysicalPlacement>(true).Length==0 && marker.GetComponentsInChildren<Collider2D>(true).Length==0,"Проём создаёт вторую физику/TK/контейнер: "+entry.Id);
                var copy=UnityEngine.Object.Instantiate(prefab);try
                {
                    var renderer=copy.GetComponent<DoorView>();var position=renderer.transform.position;var rotation=renderer.transform.rotation;var scale=renderer.transform.localScale;
                    renderer.Show(true);Require(renderer.Artwork.sprite==renderer.Definition.Opened,"Не показан open frame: "+entry.Id);
                    renderer.Show(false,true);Require(renderer.Artwork.sprite==renderer.Definition.Destroyed,"Не показан die frame: "+entry.Id);
                    Require(renderer.transform.position==position && renderer.transform.rotation==rotation && renderer.transform.localScale==scale,"Artwork подменил registration: "+entry.Id);
                }
                finally {UnityEngine.Object.DestroyImmediate(copy);}
            }
        }
        private static void CheckGeometry(RoomAuthoring room,RoomContentBatch batch,FactoryOfficeOpeningsAuthoring.Data data)
        {
            var geometry=room.CreateLayout(false);var ids=data.placements.Select(p=>batch.doors.level+":"+p.Id).ToArray();var added=batch.doors.doors.Where(d=>ids.Contains(d.id)).ToArray();
            foreach(var door in added)
            {
                Require(door.Bounds.xMin>=geometry.left-.001f && door.Bounds.xMax<=geometry.right+.001f && door.Bounds.yMin>=geometry.bottom-.001f && door.Bounds.yMax<=geometry.top+.001f,"Проём вне сохранённой комнаты: "+door.id);
                Require(!geometry.platforms.Any(s=>InteriorOverlap(door.Bounds,new Rect(s.x,s.y,s.width,s.height))),"Closed volume пересекает retained source terrain: "+door.id);
                Require(!batch.physical.props.Cast<PropState>().Concat(batch.physical.containers).Where(p=>p.hasPhysicalBody).Any(p=>InteriorOverlap(door.Bounds,new Rect(p.x-p.width*.5f,p.y-p.anchorHeight,p.width,p.height))),"Проём пересекает прежнее физическое тело: "+door.id);
                Require(!batch.npcs.actors.Any(a=>{var h=Anatomy.Movement(a);return InteriorOverlap(door.Bounds,new Rect(a.x-h.width*.5f,a.y,h.width,h.height));}),"Проём пересекает source NPC: "+door.id);
                Require(!batch.doors.doors.Any(d=>d.id!=door.id&&InteriorOverlap(door.Bounds,d.Bounds)),"Перекрываются разные двери: "+door.id);
            }
            var hatch=added.Single(d=>d.definition=="office-hatch1");var hull=new MovementHull {width=.2f,height=.4f};var top=new Vector2(hatch.x+hatch.width*.5f,hatch.y+hatch.height);
            var closed=StateJson.Copy(geometry);closed.platforms=DoorGeometry.Compose(geometry.platforms,new[]{hatch},batch.doors.level);
            Require(ActorTerrainMotion.Supported(closed,hull,top),"Closed hatch не создаёт опору.");
            var opened=StateJson.Copy(hatch);opened.open=true;var released=StateJson.Copy(geometry);released.platforms=DoorGeometry.Compose(geometry.platforms,new[]{opened},batch.doors.level);
            Require(!ActorTerrainMotion.Supported(released,hull,top) && ActorTerrainMotion.Move(released,hull,top,new Vector2(0,-.3f),false).y<top.y-.2f,"Open hatch оставил невидимую опору.");
            opened.open=false;opened.integrity.health=0;released.platforms=DoorGeometry.Compose(geometry.platforms,new[]{opened},batch.doors.level);
            Require(!ActorTerrainMotion.Supported(released,hull,top),"Destroyed hatch оставил невидимую опору.");
            var office=room.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53"?room:room.GetComponentsInChildren<RoomAuthoring>().Single(r=>r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53");
            var source=StateJson.FromJson<RoomLayout>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Research/FactoryOffice53/geometry.json"))));var local=office.CreateLayout(false);
            foreach(var surface in source.platforms) Require(local.platforms.Count(s=>FactoryLadderLandingChecks.SameSurface(s,surface))==1,"Изменена/удалена/дублирована исходная поверхность офиса.");
        }
        private static bool InteriorOverlap(Rect a,Rect b)=>a.xMin<b.xMax-.0001f&&a.xMax>b.xMin+.0001f&&a.yMin<b.yMax-.0001f&&a.yMax>b.yMin+.0001f;
        private static void CheckPersistence(RoomContentBatch batch,DoorLayout other,FactoryOfficeOpeningsAuthoring.Data data)
        {
            var fresh=new WorldState {worldId="office-openings-check",mechanismVersion=1};RoomContentInstaller.Apply(fresh,batch);VerifyDoorPersistence(fresh,batch.doors,data);
            var separate=StateJson.Copy(fresh);DoorContentInstaller.Apply(separate,other);
            Require(separate.doors.Select(d=>d.id).Distinct(StringComparer.Ordinal).Count()==separate.doors.Count && separate.doors.Count==batch.doors.doors.Length*2,"Фабрики0 и6 разделяют ID проёмов.");
        }
        private static void VerifyDoorPersistence(WorldState fresh,DoorLayout layout,FactoryOfficeOpeningsAuthoring.Data data)
        {
            foreach(var step in new[]{(ContentExpansionStep)layout.expansion}.Concat(layout.expansion.earlierVersions))
            {
                var world=StateJson.Copy(fresh);world.contentPatches.Remove(layout.patch);world.contentPatches.Add(step.previousPatch);
                world.doors.RemoveAll(d=>step.additions.Contains(d.id));string removed=world.doors[0].id;world.doors.RemoveAt(0);
                var saved=world.doors[0];saved.open=true;saved.revision=17;saved.extensions=Extensions("legacy-door");
                foreach(var container in world.containers) {container.items.Clear();container.extensions=Extensions("depleted");}
                if(world.props.Count>0) {world.props[0].x+=.17f;world.props[0].owner="saved-cargo-owner";world.props[0].generation=7;world.props[0].extensions=Extensions("cargo");}
                if(world.actors.Count>0) {var dead=world.actors[0];dead.down=true;dead.health=0;dead.inventory.Clear();dead.owner="saved-dead-owner";dead.generation=9;dead.extensions=Extensions("dead");}
                if(world.movingPlatforms.Count>0)
                {
                    var p=world.movingPlatforms[0];p.endpoint=0;p.revision=7;p.segment=new MovingPlatformSegment {sequence=7,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=3,durationSeconds=6,extensions=Extensions("midsegment")};
                    world.mechanismButtons[0].repairState=MechanismRepairStates.Ready;world.mechanismButtons[0].revision=9;
                }
                string other=StateJson.ToJson(new {world.props,world.containers,world.actors,world.movingPlatforms,world.mechanismButtons});
                var old=world.doors.Select(d=>(d.id,json:StateJson.ToJson(d))).ToArray();int count=world.doors.Count;world=StateJson.Copy(world);
                Require(DoorContentInstaller.Apply(world,layout) && world.doors.Count==count+step.additions.Length && !world.doors.Any(d=>d.id==removed) &&
                    old.All(d=>world.doors.Any(p=>p.id==d.id&&StateJson.ToJson(p)==d.json)) && StateJson.ToJson(new {world.props,world.containers,world.actors,world.movingPlatforms,world.mechanismButtons})==other,
                    "Additive update вернул удалённую дверь/заменил old state/refill/dead/cargo/midsegment: "+step.previousPatch);
                string before=StateJson.ToJson(world);Require(!DoorContentInstaller.Apply(world,layout)&&StateJson.ToJson(world)==before,"Повторное применение не идемпотентно.");
            }
            var current=StateJson.Copy(fresh);var id=layout.level+":"+data.placements[0].Id;var destroyed=current.doors.Single(d=>d.id==id);
            destroyed.integrity.health=0;destroyed.integrity.resolvedAttacks.Add("saved-impact");destroyed.integrity.extensions=Extensions("integrity");
            destroyed.interaction.extensions=Extensions("interaction");destroyed.extensions=Extensions("barrier");destroyed.revision=19;
            current=StateJson.Copy(current);string state=StateJson.ToJson(current);Require(!DoorContentInstaller.Apply(current,layout)&&StateJson.ToJson(current)==state && current.doors.Single(d=>d.id==id).Destroyed,"Reload вернул destroyed barrier/потерял extensions/journal.");
            current.doors.RemoveAll(d=>d.id==id);state=StateJson.ToJson(current);Require(!DoorContentInstaller.Apply(current,layout)&&StateJson.ToJson(current)==state,"Current patch resurrected removed barrier.");
            var partial=StateJson.Copy(layout);partial.doors=partial.doors.Where(d=>d.id!=id).ToArray();bool rejected=false;
            try {Project(partial,data);} catch(InvalidOperationException) {rejected=true;}Require(rejected,"Historical projection скрыла partial six-ID install.");
        }
        // Managed check of explicit data with actual installers/serialization; no scene or live mutation.
        public static void RunPure(string researchPath)
        {
            var data=FactoryOfficeOpeningsAuthoring.ReadData(researchPath);
            var sounds=FactoryOfficeOpeningsAuthoring.ReadResources(researchPath);
            FactoryOfficeOpeningsAuthoring.ValidateSoundFiles(sounds,Path.GetFullPath(Path.Combine(researchPath,"../../../RemainsUnity")));
            Require(sounds.sounds.Count(s=>FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,s.id).EndsWith(".mp3",StringComparison.Ordinal))==4 &&
                sounds.sounds.Count(s=>FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,s.id).EndsWith(".wav",StringComparison.Ordinal))==3,"Смешанные source аудиоформаты заменены эвристикой.");
            Require(FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,null)==null,"Неинтерактивная преграда получила выдуманный звук.");
            var wrong=StateJson.Copy(sounds);wrong.sounds[0].sourceFile="unregistered";bool badPath=false;
            try {FactoryOfficeOpeningsAuthoring.SoundAssetPath(wrong,wrong.sounds[0].id);} catch(InvalidOperationException) {badPath=true;}
            Require(badPath,"Audio path guard принял незарегистрированный путь вместо отказа.");
            foreach(bool connected in new[]{false,true}) foreach(string level in new[]{"factory:0","factory:6","factory:-27"})
            {
                var oldCodes=new[]{"NMeCLKXlWsv6CYYL","foMRNLiLOHCLfItd","ffoNKTl2qsZVBCG3"};
                var old=oldCodes.Select((code,i)=>new DoorState {id=level+":"+FactoryOfficeOpeningsAuthoring.Prefix+code,definition="door1",level=level,surfaceTexture="door:wood",x=30+i,y=1,width=.8f,height=1.6f}).ToList();
                if(connected) for(int i=0;i<6;i++) old.Add(new DoorState {id=level+":earlier-door:"+i,definition="door1",level=level,surfaceTexture="door:wood",x=i,y=1,width=.8f,height=1.6f});
                var prior=new DoorLayout {level=level,patch=level+":"+(connected?data.connectedPrevious:data.officePrevious),doors=old.ToArray(),
                    expansion=connected?new ContentExpansion {previousPatch=level+":base:factory-connected-46-18:doors-v1",additions=old.Take(3).Select(d=>d.id).ToArray()}:null};
                var additions=data.placements.Select(p=>
                {
                    var d=data.definitions.Single(d=>d.type==p.type);var size=d.Size;var interaction=d.InteractionSize;
                    return new DoorState {id=level+":"+p.Id,definition=d.id,level=level,surfaceTexture=d.surfaceTexture,x=p.Position.x-size.x*.5f+(connected?76.8f:0),y=p.Position.y,width=size.x,height=size.y,
                        interactive=d.interactive,integrity=new DoorIntegrity {maxHealth=d.maxHealth,health=d.maxHealth},interaction=new DoorInteractionVolume {offsetX=(size.x-interaction.x)*.5f,width=interaction.x,height=interaction.y}};
                }).ToArray();
                var layout=new DoorLayout {level=level,patch=level+":"+(connected?data.connectedPatch:data.officePatch),doors=old.Concat(additions).ToArray(),
                    expansion=ContentExpansionRules.InLevel(FactoryOfficeOpeningsAuthoring.CreateExpansion(prior,connected,data),level)};
                Require(StateJson.ToJson(Project(layout,data))==StateJson.ToJson(prior),"Exact predecessor projection потеряла fixture.");
                if(connected)
                {
                    var resumed=StateJson.Copy(layout);resumed.patch=prior.patch;resumed.expansion=StateJson.Copy(prior.expansion);
                    Require(StateJson.ToJson(ConnectedPredecessorForResume(resumed,data))==StateJson.ToJson(prior),"Interrupted office save потерял прежний root manifest.");
                }
                var world=new WorldState {worldId="pure-openings-"+level,mechanismVersion=1};DoorContentInstaller.Apply(world,layout);VerifyDoorPersistence(world,layout,data);
                var hatch=additions.Single(d=>d.definition=="office-hatch1");Require(hatch.InteractionBounds.height==1&&hatch.Bounds.height==.8f,"Hatch E стал body.");
                var window=additions.Single(d=>d.definition=="office-window1");Require(window.y==3.2f && !window.CanInteract,"Window source anchor/E ошибочны.");
                Require(DoorGeometry.Compose(Array.Empty<RoomSurface>(),additions,level).Length==6,"Closed unique объёмы не созданы.");
                foreach(var d in additions) d.open=true;Require(DoorGeometry.Compose(Array.Empty<RoomSurface>(),additions,level).Length==0,"Open оставил collider.");
                foreach(var d in additions) {d.open=false;d.integrity.health=0;}Require(DoorGeometry.Compose(Array.Empty<RoomSurface>(),additions,level).Length==0,"Destroyed оставил collider.");
            }
        }
        private static Dictionary<string,JToken> Extensions(string marker)=>new() {{"modOpening",JToken.Parse("{\"marker\":\""+marker+"\",\"nested\":[7,\"данные мода\"]}")}};
        private static void Require(bool value,string reason)=>FactoryOfficeOpeningsAuthoring.Require(value,reason);
    }
}
