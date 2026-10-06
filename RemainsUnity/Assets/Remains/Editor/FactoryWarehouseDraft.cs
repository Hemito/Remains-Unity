using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class FactoryWarehouseDraft
    {
        public const string Path="Assets/Remains/RoomDrafts/FactoryWarehouse40.prefab";
        public const string ReportPath="Assets/Remains/RoomDrafts/FactoryWarehouse40Import.asset";
        public const string PhysicalPatch="base:factory-warehouse-40:physical-v1",DoorPatch="base:factory-warehouse-40:doors-v1";
        public static FactoryLadderLandingRecord[] Landings()=>StateJson.Copy(FactoryWarehouseSource.Load().landings);
        internal static string DefinitionPath(string kind)=>kind switch {
            "box"=>"Assets/Remains/Resources/Content/Props/box.asset", "bigbox"=>"Assets/Remains/RoomDrafts/FactoryProps/bigbox.asset",
            "case" or "chest" or "ammobox" or "weapbox" or "explbox"=>"Assets/Remains/RoomDrafts/Containers/"+kind+".asset",
            _=>throw new InvalidOperationException("Склад40: тип не имеет явной физической привязки, kind="+kind)};
        internal static string LootPath(string kind)=>kind switch {
            "case" or "ammobox"=>"Assets/Remains/RoomDrafts/Containers/"+kind+"-loot.asset",
            "chest" or "weapbox" or "explbox"=>"Assets/Remains/RoomDrafts/Containers/office53-"+kind+"-loot.asset",
            _=>throw new InvalidOperationException("Склад40: нет явно выбранной таблицы, kind="+kind)};
        public static void Import()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();PropCatalog.Load();
            var data=FactoryWarehouseSource.Load();var input=CreateInput(data,true);
            // Common validation refuses an existing prefab/artwork/report before creating anything.
            FactoryModuleAuthoring.Validate(input);
            var root=FactoryModuleAuthoring.Create(input);
            try
            {
                FactoryWarehouseLighting.Install(root,data,input);
                FactoryModuleAuthoring.Save(root,input);
                FactoryWarehouseChecks.Check(root.GetComponent<RoomAuthoring>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("Склад40: сохранены 52 поверхности, 3 лестницы, 97 слоёв, 4 светильника, 16 контейнеров, 17 грузовых ящиков, 2 двери; 3 source player reference, 9 NPC и 10 неподдержанных объектов сохранены в аудите.");
        }
        internal static FactoryModuleAuthoringInput CreateInput(FactoryWarehouseSource.Data data,bool warnings)
        {
            var records=new List<RoomSourceRecord>();var physical=new List<FactoryModulePhysicalBinding>();var doors=new List<FactoryModuleDoorBinding>();
            foreach(var source in data.source.room.records.Where(r=>r.tag=="obj"))
            {
                string kind=FactoryWarehouseSource.Value(source,"id"),code=FactoryWarehouseSource.Value(source,"code");
                var record=new RoomSourceRecord {recordId=source.recordId,kind=kind,code=code,sourceX=FactoryWarehouseSource.Number(source,"x"),sourceY=FactoryWarehouseSource.Number(source,"y"),sourceData=source.contentXml};
                if(kind is "case" or "chest" or "ammobox" or "weapbox" or "explbox" or "box" or "bigbox")
                {
                    bool container=kind is not ("box" or "bigbox");var definition=FactoryWarehouseSource.Asset<PropAsset>(DefinitionPath(kind));
                    physical.Add(new FactoryModulePhysicalBinding {recordId=record.recordId,definition=definition,container=container,
                        loot=container?FactoryWarehouseSource.Asset<ContainerLootAsset>(LootPath(kind)):null,explicitlyEmpty=false,anchor=FactoryWarehouseSource.Anchor(data,source)});
                    record.installed=true;record.reason=container?"Исходный ID/anchor и named определение; существующая явная таблица лута Unity. Исходные skills/locks/loot generator не реализованы.":
                        "Исходный переносимый грузовой ящик; named самостоятельное body/mass определение Unity. Source anchor сохранён, опора/падение не подменены offset.";
                }
                else if(kind=="door1")
                {
                    doors.Add(new FactoryModuleDoorBinding {recordId=record.recordId,prefab=FactoryWarehouseSource.Asset<GameObject>("Assets/Remains/Resources/Presentation/Doors/office-door1.prefab"),
                        movementSound="door:wood",anchor=FactoryWarehouseSource.Anchor(data,source)});
                    record.installed=true;record.reason="Исходная дверь: existing office-door1 closed/HP60/interactive, fixed initial unlocked Unity. Lock/context распределение не реализовано; исходные атрибуты сохранены.";
                }
                else if(kind=="player") {record.installed=true;record.reason="Source player reference: preview record81 выбран явно; actor не создаётся, вход сохраняет настоящий body/lifecycle.";}
                else {record.installed=false;record.reason=kind switch {
                    "enl1" or "enl2"=>"Исходная категория наземного NPC: отдельная fixed встреча Unity устанавливается следующим additive authorer, не исходный generator roll.",
                    "enc1"=>"Потолочная категория врага: самостоятельное поведение/тело не реализованы, raider/radroach подмена отсутствует.",
                    "msp"=>"UnitMsp мина-паук требует собственного поведения/взрыва/тела; запись не заменена декором.",
                    "doctor"=>"Медицинская услуга/NPC и торговля отложены; исходный turn/контекст сохранены.",
                    "safe" or "locker"=>"Нужны отдельные original graphic/body/container definitions; замки/мины/навыки не реализованы.",
                    "door2"=>"Нужен отдельный source metal door2 контракт/ресурс; ordinary door не подставлена, source lock override сохранён.",
                    "couch"=>"Нужны самостоятельные диван graphic/body/mass данные, декоративная замена отсутствует.",_=>throw new InvalidOperationException("Склад40: не рассмотрен source type="+kind+", record="+record.recordId)};}
                records.Add(record);
            }
            FactoryWarehouseSource.Require(physical.Count==33 && physical.Count(p=>p.container)==16 && doors.Count==2 && records.Count(r=>r.installed)==38,"Изменён явный набор 33physical/2doors/3references.");
            var decor=StateJson.Copy(data.decor);var placements=data.manifest.decorPlacements;
            FactoryWarehouseSource.Require(placements?.Length==19 && placements.Select(p=>p.artworkIndex).Distinct().Count()==19,"Неоднозначные source decor bindings.");
            foreach(var p in placements)
            {
                string kind=p.sourceAttributes["id"];
                bool skip=kind=="chole1" && p.sourceDefinition.TryGetValue("er",out string er)&&er=="1" && p.disposition=="unsupported-erasure-skipped";
                FactoryWarehouseSource.Require(p.blend=="normal" && (skip || p.disposition=="diffuse-sprite" && !p.sourceDefinition.ContainsKey("er")),"Неподдержанная композиция без явного решения: "+p.recordId);
                if(skip && warnings) Debug.LogWarning("Склад40: chole1 пропущен до самостоятельной композиции вырезания фона; record="+p.recordId+", sourceErase=1. Исходные XML/PNG сохранены, альфа-подмена отсутствует.");
            }
            var skipped=placements.Where(p=>p.disposition=="unsupported-erasure-skipped").Select(p=>p.artworkIndex).ToArray();
            FactoryWarehouseSource.Require(skipped.Length==1,"Изменено решение единственного chole1.");decor.artwork=decor.artwork.Where((a,i)=>!skipped.Contains(i)).ToArray();
            return new FactoryModuleAuthoringInput {sourceIndex=40,sourceId=FactoryWarehouseSource.SourceId,sourceHash=FactoryWarehouseSource.SourceHash,name="Завод · склад1 · исходный шаблон40",
                prefabPath=Path,artworkFolder="Assets/Remains/RoomDrafts/FactoryWarehouse40Artwork",reportPath=ReportPath,physicalPatch=PhysicalPatch,doorPatch=DoorPatch,
                geometry=data.geometry,decor=decor,additionalArtwork=new[]{new RoomArtwork {x=0,y=0,width=38.4f,height=20,texture="Factory46/backwall",order=-40,repeat=true,shade=1,backgroundLighting=true}},
                resources=data.manifest.resources,sourceBindings=data.bindings,records=records.ToArray(),physical=physical.ToArray(),doors=doors.ToArray(),installLadderLandings=true,landingRecords=data.landings};
        }
    }
}
