using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationChecks
    {
        public static void Run()
        {
            PropCatalog.Load();Check(FactoryContinuationSource.Asset<GameObject>(FactoryContinuationDraft.Path).GetComponent<RoomAuthoring>());
            Debug.Log("Продолжение27: PASS — точные29 поверхностей/8лестниц/79 слоёв, сохранённые23 ресурса/5масок/5светильников, исходные44 записи и baseline17 physical/3 перегородки. Проверена только явно известная стадия additions; original runtime не проверен.");
        }
        public static void Check(RoomAuthoring room)
        {
            Require(room!=null && room.HasAuthoredArtwork,"Нет сохранённой authored комнаты.");
            var data=FactoryContinuationSource.Load();var input=FactoryContinuationDraft.CreateInput(data);var actual=room.CreateLayout();
            Require(actual.left==0 && actual.right==38.4f && actual.bottom==0 && actual.top==20 &&
                actual.spawnX==data.geometry.spawnX && actual.spawnY==data.geometry.spawnY && actual.platforms.Length==29 && actual.ladders.Length==8 && actual.artwork.Length==79,
                "Изменён exact geometry envelope/count/source spawn reference.");
            var surfaces=actual.platforms.ToList();
            foreach(var expected in data.geometry.platforms)
            {
                int index=surfaces.FindIndex(p=>FactoryLadderLandingChecks.SameSurface(p,expected));
                Require(index>=0,"Потеряна source surface: "+StateJson.ToJson(expected));surfaces.RemoveAt(index);
            }
            var ladders=actual.ladders.ToList();
            foreach(var expected in data.geometry.ladders)
            {
                int index=ladders.FindIndex(l=>Mathf.Abs(l.x-expected.x)<.00001f && l.bottom==expected.bottom && l.top==expected.top &&
                    l.facing==expected.facing && l.movementOffset==expected.movementOffset && l.artworkOffset==expected.artworkOffset && l.artwork==expected.artwork);
                Require(index>=0,"Потеряна source ladder/registration: "+StateJson.ToJson(expected));ladders.RemoveAt(index);
            }
            var artwork=actual.artwork.ToList();
            foreach(var expected in input.geometry.artwork.Concat(input.decor.artwork).Concat(input.additionalArtwork))
            {
                int index=artwork.FindIndex(a=>FactoryContinuationArtwork.Same(a,expected));
                Require(index>=0,"Потеряны source artwork bounds/order/lighting: "+StateJson.ToJson(expected));artwork.RemoveAt(index);
            }
            CheckResources(room,input);FactoryLadderLandingChecks.CheckInstalled(room,data.landings,0,false);
            CheckContent(room,input);CheckSourceDecisions(room,input);CheckLighting(room,data);FactoryContinuationArtwork.Check(room,data);
            FactoryModuleAuthoringChecks.Run(room.gameObject);
        }
        private static void CheckResources(RoomAuthoring room,FactoryModuleAuthoringInput input)
        {
            foreach(var resource in input.resources)
            {
                FactoryModuleAuthoring.ValidateResource(resource,FactoryContinuationSource.SourceId);
                string path="Assets/Remains/Resources/Original/Room/"+resource.textureKey+Path.GetExtension(resource.file);
                var texture=FactoryContinuationSource.Asset<Texture2D>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer!=null && texture.width==resource.pixelWidth && texture.height==resource.pixelHeight && !importer.mipmapEnabled &&
                    importer.textureCompression==TextureImporterCompression.Uncompressed && importer.npotScale==TextureImporterNPOTScale.None &&
                    importer.wrapModeU==(resource.sampler.u=="Repeat"?TextureWrapMode.Repeat:TextureWrapMode.Clamp) &&
                    importer.wrapModeV==(resource.sampler.v=="Repeat"?TextureWrapMode.Repeat:TextureWrapMode.Clamp),
                    "Сохранённый ресурс изменил canvas/format/sampler: "+resource.textureKey);
                foreach(var layer in room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(a=>a.Read(room).texture==resource.textureKey))
                    Require(layer.GetComponent<MeshRenderer>().sharedMaterial.mainTexture==texture,"Чужая actual texture reference: "+resource.textureKey);
            }
        }
        private static void CheckContent(RoomAuthoring room,FactoryModuleAuthoringInput input)
        {
            var physical=FactoryContinuationObjectsAuthoring.BeforeObjects(RoomPhysicalContent.Read(room,"factory:0"));
            var doors=FactoryContinuationObjectsAuthoring.BeforeObjects(RoomDoorContent.Read(room,"factory:0"));
            Require(physical?.patch=="factory:0:"+FactoryContinuationDraft.PhysicalPatch && physical.props.Length==6 && physical.containers.Length==11 && physical.loot.Length==11 &&
                doors?.patch=="factory:0:"+FactoryContinuationDraft.DoorPatch && doors.doors.Length==3,"Неверная exact baseline или неизвестная additive stage.");
            var entries=physical.props.Cast<PropState>().Concat(physical.containers).ToArray();
            var expectedIds=input.physical.Select(b=>"factory:0:"+FactoryContinuationSource.SourceId+":"+input.records.Single(r=>r.recordId==b.recordId).code).ToArray();
            Require(SameIds(entries.Select(e=>e.id),expectedIds),"Потерян/добавлен baseline physical stable ID.");
            foreach(var binding in input.physical)
            {
                var record=input.records.Single(r=>r.recordId==binding.recordId);string local=FactoryContinuationSource.SourceId+":"+record.code;
                var state=entries.Single(e=>e.id=="factory:0:"+local);var expected=binding.definition.CreateDefinition();PropBodyRules.Validate(state);
                Require(state.definition==expected.id && expected.id==record.kind && Vector2.Distance(state.Position,binding.anchor)<.00001f &&
                    state.width==expected.width && state.height==expected.height && state.mass==expected.mass && state.anchorHeight==0 &&
                    state.hasPhysicalBody==expected.hasPhysicalBody && state.movable==expected.movable && (state is ContainerState)==binding.container,
                    "Named SO/source body/anchor изменены: "+local+", state="+StateJson.ToJson(state)+", anchor="+binding.anchor.ToString("R"));
                var marker=room.GetComponentsInChildren<RoomPhysicalPlacement>(true).Single(p=>p.Read(room).id==local);
                Require(room.Included(marker) && marker.Definition==binding.definition,"Потерян actual SO binding: "+local);
                var views=marker.GetComponentsInChildren<PropView>(true);
                Require(views.Length==1 && PrefabUtility.GetCorrespondingObjectFromOriginalSource(views[0].gameObject)==binding.definition.Prefab.gameObject &&
                    Vector3.Distance(views[0].transform.localPosition,binding.definition.Prefab.VisualAnchor==PropVisualAnchor.PlacementAnchor?Vector3.zero:Vector3.up*(expected.height*.5f))<.00001f,
                    "Исходный nested prop prefab/registration изменены: "+local);
                if(binding.container)
                {
                    var container=(RoomContainerPlacement)marker;
                    Require(container.ReadInitialItems().Length==0 && StateJson.ToJson(container.ReadLoot())==StateJson.ToJson(binding.loot.Read()) &&
                        StateJson.ToJson(physical.loot.Single(l=>l.container==state.id).table)==StateJson.ToJson(binding.loot.Read()),"Дублирован/изменён initial loot source: "+local);
                }
            }
            var doorIds=input.doors.Select(b=>"factory:0:"+FactoryContinuationSource.SourceId+":"+input.records.Single(r=>r.recordId==b.recordId).code).ToArray();
            Require(SameIds(doors.doors.Select(d=>d.id),doorIds),"Изменён exact набор трёх septum stable IDs.");
            foreach(var binding in input.doors)
            {
                var record=input.records.Single(r=>r.recordId==binding.recordId);var d=doors.doors.Single(e=>e.id=="factory:0:"+FactoryContinuationSource.SourceId+":"+record.code);
                var definition=binding.prefab.GetComponent<DoorView>().Definition;
                Require(d.definition==definition.Id && definition.Id=="office-septum" && !d.open && !d.interactive && d.integrity?.health==30 &&
                    d.integrity.maxHealth==definition.MaxHealth && d.integrity.resolvedAttacks.Count==0 && d.width==definition.Size.x && d.height==definition.Size.y &&
                    Mathf.Abs(d.x-(binding.anchor.x-d.width*.5f))<.00001f && Mathf.Abs(d.y-binding.anchor.y)<.00001f && d.surfaceTexture==binding.movementSound,
                    "Named septum state/source registration изменены: "+d.id+", actual="+StateJson.ToJson(d));
            }
        }
        private static void CheckSourceDecisions(RoomAuthoring room,FactoryModuleAuthoringInput input)
        {
            var report=room.GetComponent<RoomImportAudit>()?.Report;
            Require(report!=null && report.SourceId==FactoryContinuationSource.SourceId && report.Records.Length==44 &&
                new SerializedObject(report).FindProperty("sourceHash").stringValue==FactoryContinuationSource.SourceHash,"Изменён полный source audit/hash.");
            bool objects=RoomPhysicalContent.Read(room,"factory:0").patch=="factory:0:"+FactoryContinuationObjectsAuthoring.PhysicalPatch;
            Require(objects==(RoomDoorContent.Read(room,"factory:0").patch=="factory:0:"+FactoryContinuationObjectsAuthoring.DoorPatch),"Частично установлена группа physical/doors objects.");
            var actors=room.GetComponentsInChildren<RoomActorPlacement>(true).Where(room.Included).Select(p=>p.Read(room)).ToArray();
            var npcRecords=input.records.Where(r=>r.kind=="enl1").ToArray();
            var activeNpcRecords=npcRecords.Where(r=>r.code!=FactoryContinuationNpcAuthoring.DeferredCode).ToArray();
            Require(npcRecords.Length==14 && activeNpcRecords.Length==13 && (actors.Length==0 || actors.Length==13 && SameIds(actors.Select(a=>a.id),activeNpcRecords.Select(r=>FactoryContinuationSource.SourceId+":"+r.code))),
                "Частичная/чужая NPC группа или подмена source player/rat.");
            var npcs=RoomNpcContent.Read(room,"factory:0");
            Require(actors.Length==0?npcs==null:npcs?.patch=="factory:0:"+FactoryContinuationNpcAuthoring.Patch && npcs.actors.Length==13,
                "NPC placements не соответствуют явной local stage.");
            foreach(var expected in input.records)
            {
                var r=report.Records.Single(a=>a.recordId==expected.recordId);
                bool installed=expected.installed || objects && FactoryContinuationObjectsAuthoring.Records.Any(a=>a.RecordId==r.recordId) || actors.Length==13 && activeNpcRecords.Any(a=>a.recordId==r.recordId);
                Require(r.code==expected.code && r.kind==expected.kind && r.sourceData==expected.sourceData && r.sourceX==expected.sourceX && r.sourceY==expected.sourceY &&
                    r.installed==installed && !string.IsNullOrWhiteSpace(r.reason),"Изменён source ID/XML/flag: "+expected.recordId);
            }
            Require(report.Records.Count(r=>r.installed)==21+(objects?6:0)+actors.Length,"Изменён точный21/27/34/40 installed stage; source enl1:64 остаётся pending из-за authored couch overlap.");
        }
        private static void CheckLighting(RoomAuthoring room,FactoryContinuationSource.Data data)
        {
            var profile=room.GetComponent<RoomLightingAuthoring>()?.CreateProfile(room);var template=FactoryContinuationLighting.TemplateProfile;
            Require(profile?.lights.Length==5 && profile.ambient==template.ambient && profile.backgroundAmbientScale==template.backgroundAmbientScale,"Изменён explicit five-light Unity profile.");
            foreach(var source in data.source.room.records.Where(r=>r.tag=="back" && FactoryContinuationSource.Value(r,"id") is "light2" or "light4"))
            {
                var expected=FactoryContinuationLighting.Expected(source);var actual=profile.lights.Single(l=>l.id==source.recordId);
                Require(Mathf.Abs(actual.x-expected.x)<.00001f && Mathf.Abs(actual.y-expected.y)<.00001f && Mathf.Abs(actual.maskX-expected.maskX)<.00001f && Mathf.Abs(actual.maskY-expected.maskY)<.00001f &&
                    actual.radius==expected.radius && actual.intensity==expected.intensity && actual.color==expected.color && actual.mask==expected.mask && actual.maskTexture==expected.maskTexture &&
                    actual.maskWidth==expected.maskWidth && actual.maskHeight==expected.maskHeight && actual.width==expected.width && actual.height==expected.height && actual.fixture==expected.fixture &&
                    actual.shadow==expected.shadow && actual.backgroundIntensity==expected.backgroundIntensity,
                    "Светильник/mask потерял перевод регистрации/поля: "+source.recordId+", expected="+StateJson.ToJson(expected)+", actual="+StateJson.ToJson(actual));
            }
        }
        private static bool SameIds(System.Collections.Generic.IEnumerable<string> a,System.Collections.Generic.IEnumerable<string> b)=>
            a.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(b.OrderBy(id=>id,StringComparer.Ordinal));
        private static void Require(bool value,string detail)=>FactoryContinuationSource.Require(value,detail);
    }
}
