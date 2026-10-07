using System;
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
    public static class FactoryWarehouseChecks
    {
        public static void Run()
        {
            PropCatalog.Load();Check(FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseDraft.Path).GetComponent<RoomAuthoring>());
            Debug.Log("Склад40: PASS native source projection, named definitions/loot, 33 физических объекта, 2 двери, 3 посадки, 4 маски, сохранённые artwork anchors и повторный вход без refill/resurrection.");
        }
        public static void Check(RoomAuthoring room)
        {
            FactoryWarehouseSource.Require(room!=null && room.HasAuthoredArtwork,"Нет сохранённого authored модуля.");
            var data=FactoryWarehouseSource.Load();var input=FactoryWarehouseDraft.CreateInput(data,false);var actual=room.CreateLayout();
            bool composition=room.GetComponent<RoomBackdropCompositionAuthoring>()!=null;
            FactoryWarehouseSource.Require(actual.platforms.Length==52 && actual.ladders.Length==3 && actual.artwork.Length==(composition?98:97),"Изменён полный набор geometry/artwork (97 historical/98 с единственной chole-композицией).");
            if(composition) FactoryBackdropComposition.ValidateModule(room,FactoryBackdropComposition.Read(),"FactoryWarehouse40");
            CheckProjection(actual,input);CheckResources(room,input);
            FactoryLadderLandingChecks.CheckInstalled(room,data.landings,0,false);
            var report=room.GetComponent<RoomImportAudit>()?.Report;
            FactoryWarehouseSource.Require(report?.SourceId==FactoryWarehouseSource.SourceId && report.Records.Length==57,"Потерян полный source audit.");
            var spawns=room.GetComponentsInChildren<RoomActorPlacement>(true).Where(room.Included).ToArray();
            var npcs=input.records.Where(r=>r.kind is "enl1" or "enl2").ToArray();
            FactoryWarehouseSource.Require(spawns.Length==0 || spawns.Length==9 && spawns.Select(p=>p.Read(room).id).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(npcs.Select(n=>FactoryWarehouseSource.SourceId+":"+n.code).OrderBy(id=>id,StringComparer.Ordinal)),"Частичная/чужая NPC-встреча не скрывается стадией.");
            foreach(var expected in input.records)
            {
                var record=report.Records.Single(r=>r.recordId==expected.recordId);
                bool npc=npcs.Any(n=>n.recordId==record.recordId),installed=expected.installed || npc && spawns.Length==9;
                FactoryWarehouseSource.Require(record.code==expected.code && record.kind==expected.kind && record.sourceData==expected.sourceData && record.sourceX==expected.sourceX && record.sourceY==expected.sourceY &&
                    record.installed==installed && !string.IsNullOrWhiteSpace(record.reason),"Source ID/XML/flags изменены: "+expected.recordId);
            }
            FactoryWarehouseSource.Require(report.Records.Count(r=>r.installed)==38+spawns.Length,"Изменены source counts/stage.");
            CheckPhysical(room,input);CheckDoors(room,input);CheckLights(room,data);CheckSharedContracts();
            CheckPersistence(RoomPhysicalContent.Read(room,"factory:0"),RoomDoorContent.Read(room,"factory:0"));
            FactoryModuleAuthoringChecks.Run(room.gameObject);
        }
        private static void CheckProjection(RoomLayout actual,FactoryModuleAuthoringInput input)
        {
            foreach(var expected in input.geometry.platforms) FactoryWarehouseSource.Require(actual.platforms.Count(p=>FactoryLadderLandingChecks.SameSurface(p,expected))==1,"Terrain source projection изменена.");
            foreach(var expected in input.geometry.ladders) FactoryWarehouseSource.Require(actual.ladders.Count(l=>Mathf.Abs(l.x-expected.x)<.001f && l.bottom==expected.bottom && l.top==expected.top && l.facing==expected.facing && l.movementOffset==expected.movementOffset && l.artwork==expected.artwork)==1,"Source лестница/offset изменены.");
            foreach(var expected in input.geometry.artwork.Concat(input.decor.artwork).Concat(input.additionalArtwork))
            {
                var authored=StateJson.Copy(expected);if(authored.order<-10)authored.backgroundLighting=true;
                FactoryWarehouseSource.Require(actual.artwork.Count(a=>FactoryLadderLandingChecks.SameArtwork(a,authored))==1,"Graphic source registration/material/lighting изменены: "+expected.texture);
            }
        }
        private static void CheckResources(RoomAuthoring room,FactoryModuleAuthoringInput input)
        {
            foreach(var resource in input.resources)
            {
                FactoryModuleAuthoring.ValidateResource(resource,FactoryWarehouseSource.SourceId);
                string path="Assets/Remains/Resources/Original/Room/"+resource.textureKey+System.IO.Path.GetExtension(resource.file);
                var texture=FactoryWarehouseSource.Asset<Texture2D>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                FactoryWarehouseSource.Require(importer!=null && texture.width==resource.pixelWidth && texture.height==resource.pixelHeight &&
                    importer.wrapModeU==(resource.sampler.u=="Repeat"?TextureWrapMode.Repeat:TextureWrapMode.Clamp) &&
                    importer.wrapModeV==(resource.sampler.v=="Repeat"?TextureWrapMode.Repeat:TextureWrapMode.Clamp) &&
                    importer.textureCompression==TextureImporterCompression.Uncompressed && importer.npotScale==TextureImporterNPOTScale.None && !importer.mipmapEnabled,
                    "Cold saved resource потерял canvas/sampler/format: "+resource.textureKey);
                foreach(var art in room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(a=>a.Read(room).texture==resource.textureKey))
                    FactoryWarehouseSource.Require(art.GetComponent<MeshRenderer>().sharedMaterial.mainTexture==texture,"Авторский слой использует чужой texture asset: "+resource.textureKey);
            }
        }
        private static void CheckPhysical(RoomAuthoring room,FactoryModuleAuthoringInput input)
        {
            var layout=RoomPhysicalContent.Read(room,"factory:0");var all=layout.props.Cast<PropState>().Concat(layout.containers).ToArray();
            FactoryWarehouseSource.Require(layout.patch=="factory:0:"+FactoryWarehouseDraft.PhysicalPatch && layout.props.Length==17 && layout.containers.Length==16 && layout.loot.Length==16,"Нет точных 33physical/16loot/версии.");
            foreach(var binding in input.physical)
            {
                var record=input.records.Single(r=>r.recordId==binding.recordId);string id=FactoryWarehouseSource.SourceId+":"+record.code;var definition=binding.definition.CreateDefinition();
                var state=all.Single(p=>p.id==layout.level+":"+id);PropBodyRules.Validate(state);
                FactoryWarehouseSource.Require(state.definition==record.kind && Vector2.Distance(state.Position,binding.anchor)<.001f && state.anchorHeight==0 &&
                    state.width==definition.width && state.height==definition.height && state.mass==definition.mass && state.hasPhysicalBody==definition.hasPhysicalBody && state.movable==definition.movable &&
                    (state is ContainerState)==binding.container,"Physical state не читает named SO/source anchor: "+id);
                var marker=room.GetComponentsInChildren<RoomPhysicalPlacement>(true).Single(p=>p.Read(room).id==id);
                FactoryWarehouseSource.Require(room.Included(marker) && marker.Definition==binding.definition,"Source marker/SO binding изменён: "+id);
                var preview=marker.GetComponentsInChildren<PropView>(true);
                FactoryWarehouseSource.Require(preview.Length==1 && PrefabUtility.GetCorrespondingObjectFromOriginalSource(preview[0].gameObject)==binding.definition.Prefab.gameObject &&
                    Vector3.Distance(preview[0].transform.localPosition,binding.definition.Prefab.VisualAnchor==PropVisualAnchor.PlacementAnchor?Vector3.zero:Vector3.up*(state.height*.5f))<.001f,
                    "Nested original prop prefab/graphic anchor изменён: "+id);
                if(binding.container)
                {
                    var c=(RoomContainerPlacement)marker;
                    FactoryWarehouseSource.Require(c.ReadInitialItems().Length==0 && StateJson.ToJson(c.ReadLoot())==StateJson.ToJson(binding.loot.Read()) &&
                        StateJson.ToJson(layout.loot.Single(b=>b.container==state.id).table)==StateJson.ToJson(binding.loot.Read()),"Два источника initial loot или чужая таблица: "+id);
                }
                var r=new Rect(state.x-state.width*.5f,state.y-state.anchorHeight,state.width,state.height);
                if(state is ContainerState container)FactoryWarehouseSource.Require(((InventorySource)container).Bounds==r,"Container E rect не читает physical state: "+id);
                FactoryWarehouseSource.Require(r.xMin>=-.001f && r.xMax<=38.401f && r.yMin>=-.001f && r.yMax<=20.001f,"Body/E bounds вышли за source комнату: "+id);
            }
        }
        private static void CheckDoors(RoomAuthoring room,FactoryModuleAuthoringInput input)
        {
            var layout=RoomDoorContent.Read(room,"factory:0");FactoryWarehouseSource.Require(layout.patch=="factory:0:"+FactoryWarehouseDraft.DoorPatch && layout.doors.Length==2,"Нет двух named дверей/версии.");
            var terrain=room.CreateLayout();
            foreach(var binding in input.doors)
            {
                var record=input.records.Single(r=>r.recordId==binding.recordId);var door=layout.doors.Single(d=>d.id=="factory:0:"+FactoryWarehouseSource.SourceId+":"+record.code);
                var definition=binding.prefab.GetComponent<DoorView>().Definition;
                FactoryWarehouseSource.Require(door.definition=="office-door1" && !door.open && door.interactive && door.integrity?.health==definition.MaxHealth &&
                    door.width==definition.Size.x && door.height==definition.Size.y &&
                    Mathf.Abs(door.x-(binding.anchor.x-door.width*.5f))<.00001f && Mathf.Abs(door.y-binding.anchor.y)<.00001f &&
                    door.surfaceTexture==binding.movementSound,"Door definition/source anchor/state изменены: "+door.id+
                    ", actual="+StateJson.ToJson(door)+", anchor="+binding.anchor.ToString("R")+", size="+definition.Size.ToString("R")+", health="+definition.MaxHealth+
                    ", delta="+new Vector4(door.x-(binding.anchor.x-door.width*.5f),door.y-binding.anchor.y,door.width-definition.Size.x,door.height-definition.Size.y).ToString("R")+
                    ", keys="+binding.movementSound+"/"+door.surfaceTexture);
                FactoryWarehouseSource.Require(!terrain.platforms.Any(s=>!s.oneWay && Overlap(door.Bounds,new Rect(s.x,s.y,s.width,s.height))),"Дверь в retained source wall: "+door.id);
            }
        }
        private static bool Overlap(Rect a,Rect b)=>Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>.0001f && Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>.0001f;
        private static void CheckLights(RoomAuthoring room,FactoryWarehouseSource.Data data)
        {
            var template=FactoryWarehouseLighting.Template;var profile=room.GetComponent<RoomLightingAuthoring>()?.CreateProfile(room);var old=FactoryWarehouseLighting.TemplateProfile(template);
            FactoryWarehouseSource.Require(profile?.lights.Length==4 && profile.ambient==old.ambient && profile.backgroundAmbientScale==old.backgroundAmbientScale,"Изменён явный lighting template.");
            foreach(var source in data.source.room.records.Where(r=>r.tag=="back" && FactoryWarehouseSource.Value(r,"id").StartsWith("light",StringComparison.Ordinal)))
            {
                var expected=FactoryWarehouseLighting.Expected(template,source);var actual=profile.lights.Single(l=>l.id==source.recordId);
                FactoryWarehouseSource.Require(Vector2.Distance(new Vector2(actual.x,actual.y),new Vector2(expected.x,expected.y))<.001f && actual.mask==expected.mask &&
                    FactoryLadderLandingChecks.SameRect(new Rect(actual.maskX,actual.maskY,actual.maskWidth,actual.maskHeight),expected.maskX,expected.maskY,expected.maskWidth,expected.maskHeight) &&
                    actual.intensity==expected.intensity && actual.radius==expected.radius && actual.color==expected.color,"Маска/точка потеряла source-anchor регистрацию: "+actual.id);
            }
        }
        public static void CheckSharedContracts()
        {
            var seed=new RoomLight {x=2,y=3,maskX=-1,maskY=4,maskWidth=9,maskHeight=5,radius=7,intensity=1.1f,mask="known",backgroundIntensity=.6f,fixture="fixture",width=2,height=1,shadow=true};
            var copy=FactoryModuleLightTemplate.Translate(seed,new Vector2(1,2),new Vector2(4,6),"translated");
            FactoryWarehouseSource.Require(copy.x==5 && copy.y==7 && copy.maskX==2 && copy.maskY==8 && seed.x==2 && copy.maskWidth==9 && copy.fixture==seed.fixture && copy.backgroundIntensity==seed.backgroundIntensity && copy.shadow,
                "Общий point/mask translation потерял поля или изменил template.");
            var officeSeed=new RoomLight {x=2,y=3,radius=7,intensity=1.1f,color=Color.white,mask="known",maskX=-1,maskY=4,maskWidth=9,maskHeight=5};
            var officeOld=new RoomLight {id="office",x=2.125f,y=2.75f,radius=7,intensity=1.1f,color=Color.white,mask="known",maskX=-.875f,maskY=3.75f,maskWidth=9,maskHeight=5};
            FactoryWarehouseSource.Require(StateJson.ToJson(officeOld)==StateJson.ToJson(FactoryModuleLightTemplate.Translate(officeSeed,Vector2.zero,new Vector2(.125f,-.25f),"office")),"Office refactor изменил прежние сериализованные значения.");
        }
        public static void CheckPersistence(PhysicalLayout physical,DoorLayout doors)
        {
            foreach(string level in new[]{"factory:0","factory:6","factory:-27"})
            {
                var p=StateJson.Copy(physical);var d=StateJson.Copy(doors);string old=p.level;
                p.level=level;p.patch=level+":"+FactoryWarehouseDraft.PhysicalPatch;
                foreach(var entry in p.props.Cast<PropState>().Concat(p.containers)) {entry.id=level+entry.id.Substring(old.Length);entry.level=level;}
                foreach(var binding in p.loot)binding.container=level+binding.container.Substring(old.Length);
                d.level=level;d.patch=level+":"+FactoryWarehouseDraft.DoorPatch;foreach(var door in d.doors){door.id=level+door.id.Substring(old.Length);door.level=level;}
                var existing=new WorldState {worldId="warehouse-existing-"+level};
                var retainedCargo=StateJson.Copy(p.props[0]);retainedCargo.x+=.1f;retainedCargo.owner="prior-owner";retainedCargo.generation=4;
                var retainedEmpty=StateJson.Copy(p.containers[0]);retainedEmpty.items.Clear();retainedEmpty.extensions=new System.Collections.Generic.Dictionary<string,JToken>{["depleted-before-install"]=JToken.FromObject(true)};
                var retainedDoor=StateJson.Copy(d.doors[0]);retainedDoor.open=true;retainedDoor.integrity.health=3;retainedDoor.revision=9;
                existing.props.Add(retainedCargo);existing.containers.Add(retainedEmpty);existing.doors.Add(retainedDoor);
                string kept=StateJson.ToJson(new[]{StateJson.ToJson(retainedCargo),StateJson.ToJson(retainedEmpty),StateJson.ToJson(retainedDoor)});
                PhysicalContentInstaller.Apply(existing,p);DoorContentInstaller.Apply(existing,d);
                FactoryWarehouseSource.Require(kept==StateJson.ToJson(new[]{StateJson.ToJson(existing.props.Single(a=>a.id==retainedCargo.id)),StateJson.ToJson(existing.containers.Single(a=>a.id==retainedEmpty.id)),StateJson.ToJson(existing.doors.Single(a=>a.id==retainedDoor.id))}),
                    "Первое добавление модуля заменило уже имеющийся cargo/depleted/open door state.");
                var world=new WorldState {worldId="warehouse-persistence-"+level};PhysicalContentInstaller.Apply(world,p);DoorContentInstaller.Apply(world,d);
                FactoryWarehouseSource.Require(world.props.Count==17 && world.containers.Count==16 && world.doors.Count==2 && world.containers.SelectMany(c=>c.items).Select(i=>i.id).Distinct().Count()==world.containers.Sum(c=>c.items.Count),"Fresh warehouse ID/loot counts не согласованы.");
                world.props.RemoveAt(0);world.containers.RemoveAt(0);world.doors.RemoveAt(0);
                var cargo=world.props[0];cargo.x+=.2f;cargo.owner="saved-owner";cargo.generation=7;cargo.extensions=new System.Collections.Generic.Dictionary<string,JToken>{["cargo-progress"]=JToken.Parse("{\"nested\":[17,\"груз\"]}")};
                foreach(var c in world.containers){c.items.Clear();c.extensions=new System.Collections.Generic.Dictionary<string,JToken>{["empty-progress"]=JToken.Parse("{\"claimed\":true}")};}
                world.doors[0].open=true;world.doors[0].integrity.health=1;world.doors[0].revision=11;
                var saved=StateJson.Copy(world);string before=StateJson.ToJson(saved);
                FactoryWarehouseSource.Require(!PhysicalContentInstaller.Apply(saved,p) && !DoorContentInstaller.Apply(saved,d) && StateJson.ToJson(saved)==before,"Повторный вход/load возродил removed, наполнил depleted или изменил cargo/door/extensions.");
            }
        }
    }
}
