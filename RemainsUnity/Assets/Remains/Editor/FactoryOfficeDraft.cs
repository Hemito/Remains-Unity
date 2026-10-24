using System;
using System.Collections.Generic;
using System.Globalization;
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
    // Office policy is explicit here; the shared builder never guesses a source type.
    public static class FactoryOfficeDraft
    {
        public const string Path="Assets/Remains/RoomDrafts/FactoryOffice53.prefab";
        public const string FileCabinetLootPath="Assets/Remains/RoomDrafts/FactoryOffice53FileCabinetLoot.asset";
        public const string PhysicalPatch="base:factory-office-53:physical-v1",DoorPatch="base:factory-office-53:doors-v1";
        private const string SourceHash="e008b7795261a45fe627eaa921ad764ba0492365fc5e91b93b097251a57767d8";
        [Serializable] private sealed class SourceFile {public int schema;public SourceRoom room;}
        [Serializable] private sealed class SourceRoom {public string id,contentSha256;public int sourceIndex,width,height;public SourceRecord[] records;}
        [Serializable] private sealed class SourceRecord {public string recordId,tag,contentXml;public Dictionary<string,string> attributes;}
        [Serializable] private sealed class Manifest {public FactoryModuleResource[] resources;public DecorPlacement[] decorPlacements;public RoomArtwork[] approvedUnityArtwork;}
        [Serializable] private sealed class DecorPlacement {public string recordId,blend,disposition;public int artworkIndex;public Dictionary<string,string> sourceAttributes,sourceDefinition;}
        [Serializable] private sealed class GeometryBindings {public FactoryModuleSurfaceBinding[] sourceBindings;}
        public static void Import()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            if(AssetDatabase.LoadMainAssetAtPath(Path)!=null) throw new InvalidOperationException("Офисы53 уже сохранены; авторские данные не заменены.");
            string folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Tools/Research/FactoryOffice53"));
            T Read<T>(string file)=>StateJson.FromJson<T>(File.ReadAllText(System.IO.Path.Combine(folder,file)));
            var source=Read<SourceFile>("source-room.json");var raw=source.room;
            if(source.schema!=1 || raw==null || raw.sourceIndex!=53 || raw.width!=48 || raw.height!=25 || raw.id!="original:plant:53" || raw.contentSha256!=SourceHash || raw.records==null || raw.records.Count(r=>r.tag=="obj")!=46 || raw.records.Count(r=>r.tag=="back")!=73)
                throw new InvalidOperationException("Офисы53: изменился проверенный источник/формат; импорт остановлен до создания комнаты.");
            var geometry=Read<RoomLayout>("geometry.json");var decor=Read<RoomLayout>("decor.json");var manifest=Read<Manifest>("manifest.json");var resources=manifest.resources;
            ApplyDecorPolicy(decor,manifest);
            if(manifest.approvedUnityArtwork?.Length!=1 || manifest.approvedUnityArtwork[0].texture!="Factory46/backwall" || manifest.approvedUnityArtwork[0].x!=0 || manifest.approvedUnityArtwork[0].y!=0 ||
                manifest.approvedUnityArtwork[0].width!=38.4f || manifest.approvedUnityArtwork[0].height!=20 || manifest.approvedUnityArtwork[0].order!= -40 || !manifest.approvedUnityArtwork[0].repeat || !manifest.approvedUnityArtwork[0].backgroundLighting)
                throw new InvalidOperationException("Офисы53: нужен явный согласованный общий задник Unity, отдельно от 73 исходных записей.");
            if(Mathf.Abs(geometry.spawnX-8)>.0001f || Mathf.Abs(geometry.spawnY-.8f)>.0001f)
                throw new InvalidOperationException("Офисы53: preview использует выбранную source-метку (9;23), anchor (8;0,8); сетевые игроки из неё не создаются.");
            var records=raw.records.Where(r=>r.tag=="obj").Select(r=>new RoomSourceRecord {recordId=r.recordId,kind=Value(r,"id"),code=Value(r,"code"),sourceX=Number(r,"x"),sourceY=Number(r,"y"),sourceData=r.contentXml}).ToArray();
            var physical=new List<FactoryModulePhysicalBinding>();var doors=new List<FactoryModuleDoorBinding>();
            var filecab=Require<PropAsset>("Assets/Remains/RoomDrafts/Containers/filecab.asset");
            var filecabLoot=FileCabinetLoot();
            foreach(var record in records)
            {
                if(record.kind is "case" or "chest" or "ammobox" or "filecab" or "woodbox")
                {
                    bool prop=record.kind=="woodbox";
                    var definition=prop?Require<PropAsset>("Assets/Remains/RoomDrafts/woodbox.asset"):record.kind=="filecab"?filecab:Require<PropAsset>("Assets/Remains/RoomDrafts/Containers/"+record.kind+".asset");
                    ContainerLootAsset loot=record.kind=="chest"?FactoryOfficeChestLootAuthoring.Asset():record.kind=="filecab"?filecabLoot:record.kind is "case" or "ammobox"?Require<ContainerLootAsset>("Assets/Remains/RoomDrafts/Containers/"+record.kind+"-loot.asset"):null;
                    float cells=record.kind=="chest"?2:1;
                    physical.Add(new FactoryModulePhysicalBinding {recordId=record.recordId,definition=definition,container=!prop,loot=loot,explicitlyEmpty=!prop&&loot==null,
                        anchor=FactorySourceCoordinates.Placement(record.sourceX,record.sourceY,cells)});
                    record.installed=true;record.reason=prop?"Исходная метка переносимого ящика; используется существующее самостоятельное определение Unity.":
                        loot==null?"Общий контейнер; начальное содержимое явно пустое, исходный loot generator не переносился.":"Общий контейнер; исходный ID/anchor, существующее определение и явно выбранная начальная таблица Unity.";
                }
                else if(record.kind=="door1" && record.code!="zlnR8eeCKCDkaJ8P")
                {
                    doors.Add(new FactoryModuleDoorBinding {recordId=record.recordId,prefab=Require<GameObject>("Assets/Remains/Resources/Presentation/Doors/door1.prefab"),movementSound="door:wood",
                        anchor=FactorySourceCoordinates.Placement(record.sourceX,record.sourceY,1)});
                    record.installed=true;record.reason="Обычная дверь Unity через E; source code сохранён. Совпадающий record28 не создаёт второй collider.";
                }
                else if(record.kind=="player")
                {record.installed=true;record.reason="Исходная стартовая метка сохранена как reference. Preview выбран по record60 (9;23); actor из метки не создаётся, вход связного уровня задаётся отдельно.";}
                else
                {
                    record.installed=false;
                    record.reason=record.code=="zlnR8eeCKCDkaJ8P"?"coincident-source-door: совпадает с record27 в (28;5); выбран record27, условия исходной пары не подтверждены, второй collider не создаётся.":record.kind switch {
                        "door1a" or "door1b" or "hatch1"=>"Нет подтверждённого отдельного door/hatch контракта и ресурса; обычной дверью молча не подменяется.",
                        "enl1" or "enl2"=>"Исходное место противника сохранено; NPC-встреча этого модуля пока не выбрана.",
                        "lov"=>"Ловушка требует самостоятельной механики и ownership; запись сохранена без подмены.",
                        "bookcase" or "table" or "table1" or "trash"=>"Нужно независимое физическое/контейнерное определение с исходной графикой; неинтерактивная декоративная замена не создана.",
                        "explbox" or "weapbox" or "medbox" or "instr1"=>"Нужны отдельные исходные тип/ресурс/начальное содержимое; чужим контейнером не подменяется.",
                        "septum" or "window1"=>"Нужны независимые проём/повреждение/физические данные; запись сохранена без generic wall.",
                        _=>"Не поддержан исходный тип; record/code/XML сохранены, новый generic объект не создаётся."};
                }
            }
            if(physical.Count(b=>b.container)!=12 || physical.Count(b=>!b.container)!=3 || doors.Count!=3)
                throw new InvalidOperationException("Офисы53: изменился явный набор 12 контейнеров, 3 ящиков, 3 обычных дверей.");
            var input=new FactoryModuleAuthoringInput {sourceIndex=53,sourceId=raw.id,sourceHash=SourceHash,name="Завод · офисы2 · исходный шаблон53",prefabPath=Path,
                artworkFolder="Assets/Remains/RoomDrafts/FactoryOffice53Artwork",reportPath="Assets/Remains/RoomDrafts/FactoryOffice53Import.asset",physicalPatch=PhysicalPatch,doorPatch=DoorPatch,
                geometry=geometry,decor=decor,additionalArtwork=manifest.approvedUnityArtwork,resources=resources,sourceBindings=Read<GeometryBindings>("geometry.json").sourceBindings,records=records,physical=physical.ToArray(),doors=doors.ToArray(),installLadderLandings=true};
            var root=FactoryModuleAuthoring.Create(input);
            try {Lighting(root,input,raw);AssetDatabase.CreateAsset(filecabLoot,FileCabinetLootPath);FactoryModuleAuthoring.Save(root,input);FactoryModuleAuthoringChecks.Run(root);}
            finally {UnityEngine.Object.DestroyImmediate(root);if(!AssetDatabase.Contains(filecabLoot)) UnityEngine.Object.DestroyImmediate(filecabLoot);}
            Debug.Log("Офисы53: сохранены 103 поверхности, 2 лестницы, 134 слоя геометрии, 68 из 73 слоёв декора, отдельный общий задник Unity, 15 светильников, 12 общих контейнеров, 3 ящика и 3 двери; неподдержанные записи показаны в аудите.");
        }
        private static void ApplyDecorPolicy(RoomLayout decor,Manifest manifest)
        {
            if(decor.artwork?.Length!=73 || manifest.decorPlacements?.Length!=73 || manifest.decorPlacements.Select(p=>p.artworkIndex).Distinct().Count()!=73 ||
                manifest.decorPlacements.Any(p=>p.artworkIndex<0 || p.artworkIndex>=73 || p.sourceAttributes==null || p.sourceDefinition==null || string.IsNullOrWhiteSpace(p.recordId)))
                throw new InvalidOperationException("Офисы53: неполное единственное соответствие 73 декораций исходным записям.");
            var skipped=new HashSet<int>();
            int partialWindows=0;
            foreach(var placement in manifest.decorPlacements)
            {
                string kind=placement.sourceAttributes["id"];
                bool blood=kind=="blood1" && placement.blend=="multiply" && placement.disposition=="unsupported-multiply-skipped";
                bool hole=kind=="chole6" && placement.sourceDefinition.TryGetValue("er",out string er) && er=="1" && placement.disposition=="unsupported-erasure-skipped";
                if(blood || hole)
                {
                    skipped.Add(placement.artworkIndex);
                    Debug.LogWarning("Офисы53: декор пропущен до отдельного rendering контракта; record="+placement.recordId+", type="+kind+", policy="+placement.disposition+". Исходные запись и PNG сохранены; alpha-подмена не создана.");
                }
                else if(kind is "bwindow" or "swindow" && placement.blend=="normal" &&
                    placement.sourceDefinition.TryGetValue("er",out string windowErase) && windowErase=="1" &&
                    placement.disposition=="partial-window-diffuse-erasure-pending")
                {
                    partialWindows++;
                    Debug.LogWarning("Офисы53: показан исходный рисунок окна без отдельного вырезания фона; record="+placement.recordId+
                        ", type="+kind+", sourceErase=1, policy="+placement.disposition+". Исходная маска не заменена альфой рисунка; полная композиция ещё не восстановлена.");
                }
                else if(placement.blend!="normal" || placement.sourceDefinition.TryGetValue("er",out string erase)&&erase=="1")
                    throw new InvalidOperationException("Офисы53: неподдержанная композиция декора без явного решения; record="+placement.recordId+", type="+kind+", blend="+placement.blend);
            }
            if(skipped.Count!=5 || partialWindows!=7) throw new InvalidOperationException("Офисы53: изменилось явное решение о 4 multiply, 1 erase и 7 частичных окнах.");
            decor.artwork=decor.artwork.Where((a,i)=>!skipped.Contains(i)).ToArray();
        }
        private static void Lighting(GameObject root,FactoryModuleAuthoringInput input,SourceRoom source)
        {
            var reference=Require<GameObject>("Assets/Remains/RoomDrafts/FactoryEntry46.prefab").GetComponent<RoomAuthoring>();
            var old=reference.GetComponent<RoomLightingAuthoring>();
            if(old==null) throw new InvalidOperationException("Офисы53: нет явного существующего профиля освещения входа46.");
            var profile=old.CreateProfile(reference);
            var hub=Require<GameObject>("Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab").GetComponent<RoomAuthoring>();
            var hubProfile=hub.GetComponent<RoomLightingAuthoring>()?.CreateProfile(hub)??throw new InvalidOperationException("Офисы53: нет явного профиля light3 в хабе.");
            var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(profile.ambient);lighting.SetBackgroundAmbientScale(profile.backgroundAmbientScale);
            var room=root.GetComponent<RoomAuthoring>();var art=root.GetComponentsInChildren<RoomArtworkAuthoring>();
            foreach(var record in source.records.Where(r=>r.tag=="back" && Value(r,"id").StartsWith("light",StringComparison.Ordinal)))
            {
                string kind=Value(record,"id");
                if(kind is not ("light1" or "light3" or "light4")) throw new InvalidOperationException("Офисы53: неподдержанный светильник, record="+record.recordId+", kind="+kind);
                var templateRoom=kind=="light3"?hub:reference;
                var templateProfile=kind=="light3"?hubProfile:profile;
                string templateFolder=kind=="light3"?"Hub":"Factory46";
                string key="Factory53/decor-"+kind+"-1",oldKey=templateFolder+"/decor-"+kind+"-1",mask="Original/Room/"+templateFolder+"/light-mask-"+kind+"-1";
                var resource=input.resources.Single(r=>r.textureKey==key);var b=resource.sourceBoundsPixelsYDown;
                var sourceAnchor=new Vector2(Number(record,"x")*.8f,(source.height-Number(record,"y"))*.8f);
                float x=sourceAnchor.x+b[0]/resource.sourcePixelsPerMetre,y=sourceAnchor.y-b[3]/resource.sourcePixelsPerMetre;
                var actual=art.Single(a=>{var r=a.Read(room);return r.texture==key&&Mathf.Abs(r.x-x)<.001f&&Mathf.Abs(r.y-y)<.001f;});
                // One explicitly selected existing fixture is a Unity lighting template, not original lighting proof.
                var candidates=templateRoom.CreateLayout().artwork.Where(f=>f.texture==oldKey).OrderBy(f=>f.x).ThenBy(f=>f.y).ToArray();
                if(candidates.Length==0) throw new InvalidOperationException("Офисы53: нет существующего fixture template, kind="+kind+", record="+record.recordId);
                var fixture=candidates[0];var lamp=templateProfile.lights.Single(l=>l.mask==mask&&Mathf.Abs(l.x-fixture.x-fixture.width/2)<.001f&&Mathf.Abs(l.y-fixture.y-fixture.height/2)<.001f);
                // The approved existing fixture anchors were authored at their canvas left/top.
                // Translate masks by those source anchors, never by new exported canvas padding.
                var shift=sourceAnchor-new Vector2(fixture.x,fixture.y+fixture.height);
                if(kind=="light3")
                {
                    // Hub's earlier canvas omitted the source diffuse y-min of 4.5px.
                    // Matching the correctly registered new canvas restores source mask602's top edge.
                    var canvas=actual.Read(room);shift=new Vector2(canvas.x,canvas.y+canvas.height)-new Vector2(fixture.x,fixture.y+fixture.height);
                }
                var texture=GameAssets.Load<Texture2D>(mask);
                if(texture==null || !texture.isReadable) throw new InvalidOperationException("Офисы53: нет читаемой исходной маски, record="+record.recordId+", mask="+mask);
                var node=new GameObject("Светильник · "+record.recordId);node.transform.SetParent(root.GetComponentInChildren<RoomMeshLifetime>().transform,false);
                // Keep the previous Office field selection, including RoomLight defaults.
                var seed=new RoomLight {x=lamp.x,y=lamp.y,radius=lamp.radius,intensity=lamp.intensity,color=lamp.color,mask=mask,
                    maskX=lamp.maskX,maskY=lamp.maskY,maskWidth=lamp.maskWidth,maskHeight=lamp.maskHeight};
                node.AddComponent<RoomLightAuthoring>().Configure(FactoryModuleLightTemplate.Translate(seed,Vector2.zero,shift,record.recordId),texture);
                actual.transform.SetParent(node.transform,true);
            }
            foreach(var item in art)
            {
                if(item.Read(room).order>=-10) continue;
                var renderer=item.GetComponent<MeshRenderer>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetFloat("_BackgroundLighting",1);renderer.SetPropertyBlock(block);
            }
        }
        private static ContainerLootAsset FileCabinetLoot()
        {
            if(AssetDatabase.LoadMainAssetAtPath(FileCabinetLootPath)!=null) throw new InvalidOperationException("Офисы53: начальная таблица уже существует, ручные данные не заменены: "+FileCabinetLootPath);
            var table=ScriptableObject.CreateInstance<ContainerLootAsset>();table.Configure(new ContainerLootTable {id="base:office-filecab",version="1",entries=new[]{
                new ContainerLootEntry {id="pins",definition="pin",minimum=1,maximum=3},new ContainerLootEntry {id="caps",definition="caps",minimum=0,maximum=20},new ContainerLootEntry {id="repair-kit",definition="rep",minimum=0,maximum=1}}});
            return table;
        }
        private static string Value(SourceRecord r,string key)=>r.attributes!=null&&r.attributes.TryGetValue(key,out string v)?v:throw new InvalidOperationException("Офисы53: отсутствует атрибут "+key+", record="+r.recordId);
        private static float Number(SourceRecord r,string key)=>float.Parse(Value(r,key),NumberStyles.Float,CultureInfo.InvariantCulture);
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Офисы53: отсутствует подготовленный ассет "+path);
    }
}
