using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationDraft
    {
        public const string Path="Assets/Remains/RoomDrafts/FactoryContinuation27.prefab";
        public const string ReportPath="Assets/Remains/RoomDrafts/FactoryContinuation27Import.asset";
        public const string PhysicalPatch="base:factory-continuation-27:physical-v1",DoorPatch="base:factory-continuation-27:doors-v1";
        public static void Import()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();PropCatalog.Load();var data=FactoryContinuationSource.Load();
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if(existing!=null) {FactoryContinuationChecks.Check(existing.GetComponent<RoomAuthoring>());return;}
            var input=CreateInput(data);FactoryModuleAuthoring.Validate(input);var root=FactoryModuleAuthoring.Create(input);
            try
            {
                FactoryContinuationLighting.Install(root,data);
                FactoryContinuationArtwork.ApplyAlpha(root.GetComponent<RoomAuthoring>(),data);
                FactoryModuleAuthoring.Save(root,input);
                FactoryContinuationArtwork.InstallWindows(root,data);
                PrefabUtility.SaveAsPrefabAsset(root,Path);AssetDatabase.SaveAssets();
                FactoryContinuationChecks.Check(root.GetComponent<RoomAuthoring>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("Продолжение27: сохранены29 поверхностей/8лестниц/79слоёв/5светильников/5окон;11контейнеров/6грузов/3перегородки/1source reference. Остальные source записи явно pending; generator/locks/skills/source photometry не перенесены.");
        }
        public static FactoryModuleAuthoringInput CreateInput(FactoryContinuationSource.Data data)
        {
            var records=new List<RoomSourceRecord>();var physical=new List<FactoryModulePhysicalBinding>();var doors=new List<FactoryModuleDoorBinding>();
            foreach(var source in data.source.room.records.Where(r=>r.tag=="obj"))
            {
                string kind=FactoryContinuationSource.Value(source,"id"),code=FactoryContinuationSource.Value(source,"code");
                var record=new RoomSourceRecord {recordId=source.recordId,kind=kind,code=code,sourceX=FactoryContinuationSource.Number(source,"x"),sourceY=FactoryContinuationSource.Number(source,"y"),sourceData=source.contentXml};
                if(kind is "ammobox" or "trash" or "table" or "medbox" or "instr2" or "filecab" or "box" or "bigbox" or "couch")
                {
                    bool container=kind is not ("box" or "bigbox" or "couch");
                    physical.Add(new FactoryModulePhysicalBinding {recordId=record.recordId,definition=FactoryContinuationSource.Asset<PropAsset>(DefinitionPath(kind)),
                        container=container,loot=container?FactoryContinuationSource.Asset<ContainerLootAsset>(LootPath(kind)):null,anchor=FactoryContinuationSource.Anchor(data,source)});
                    record.installed=true;record.reason=container?
                        "Исходный stable ID/anchor; явный existing Unity container/loot table. Fixed unlocked/untrapped policy; source skills/locks/loot generator не реализованы.":
                        "Исходный груз/диван: existing named body/mass/art/audio definition Unity; source anchor сохранён, physics не подменена.";
                }
                else if(kind=="septum")
                {
                    doors.Add(new FactoryModuleDoorBinding {recordId=record.recordId,prefab=FactoryContinuationSource.Asset<GameObject>("Assets/Remains/Resources/Presentation/Doors/office-septum.prefab"),
                        movementSound="door:wood",anchor=FactoryContinuationSource.Anchor(data,source)});
                    record.installed=true;record.reason="Исходная деревянная перегородка; existing office-septum HP30/interactive=false/closed. Разрушается обычным оружием; E не открывает её.";
                }
                else if(kind=="player") {record.installed=true;record.reason="Source player reference51: actor не создаётся и не телепортируется; Preview spawn23.2/10.4 только справочный.";}
                else
                {
                    record.installed=false;record.reason=kind switch {
                        "enl1"=>code==FactoryContinuationNpcAuthoring.DeferredCode?
                            "Source enl1 record64 pending: raw anchor(16.4,7.2) пересекает сохранённый couch33 body[15.2,7.2,1.6,.4]. NPC/couch не смещаются; исходный generator/actor collision policy не воспроизведён.":
                            "Исходная категория малого наземного врага: отдельный fixed NPC additive authorer; без source generator roll/замены вида молча.",
                        "rat"=>"Настоящая крыса требует отдельного anatomy/AI/source graphic контракта; не подмена radroach.",
                        "cup" or "wallcab"=>"Отдельный исходный physical/container profile ещё не установлен; без декоративной подмены.",
                        "platform1"=>"Отдельная стационарная solid платформа/барьер, не moving platform2; coordinated authorer pending.",
                        "term3"=>"Терминал/данные/lock/mines требуют отдельного интерактивного контракта; не простая картинка.",
                        "work"=>"Верстак/сервис ещё не реализован; source сохранён, простой container не подставлен.",
                        _=>throw new InvalidOperationException("Продолжение27: не рассмотрен source type="+kind+", record="+record.recordId)};
                }
                records.Add(record);
            }
            FactoryContinuationSource.Require(records.Count==44 && physical.Count==17 && physical.Count(p=>p.container)==11 && doors.Count==3 && records.Count(r=>r.installed)==21,
                "Изменён baseline21installed/17physical/3septum/1reference.");
            return new FactoryModuleAuthoringInput {sourceIndex=27,sourceId=FactoryContinuationSource.SourceId,sourceHash=FactoryContinuationSource.SourceHash,
                name="Завод · продолжение склада · исходный шаблон27",prefabPath=Path,reportPath=ReportPath,
                artworkFolder="Assets/Remains/RoomDrafts/FactoryContinuation27Artwork",physicalPatch=PhysicalPatch,doorPatch=DoorPatch,
                geometry=data.geometry,decor=data.decor,resources=data.manifest.resources,additionalArtwork=data.manifest.approvedUnityArtwork,
                sourceBindings=data.bindings,records=records.ToArray(),physical=physical.ToArray(),doors=doors.ToArray(),installLadderLandings=true,landingRecords=data.landings};
        }
        public static string DefinitionPath(string kind)=>kind switch {
            "box"=>"Assets/Remains/Resources/Content/Props/box.asset",
            "bigbox"=>"Assets/Remains/RoomDrafts/FactoryProps/bigbox.asset",
            "instr2"=>"Assets/Remains/RoomDrafts/ToolStand/tool-stand.asset",
            "ammobox" or "trash" or "table" or "medbox" or "filecab" or "couch"=>"Assets/Remains/RoomDrafts/Containers/"+kind+".asset",
            _=>throw new InvalidOperationException("Продолжение27: нет named definition для "+kind)};
        public static string LootPath(string kind)=>kind switch {
            "ammobox"=>"Assets/Remains/RoomDrafts/Containers/ammobox-loot.asset",
            "filecab"=>"Assets/Remains/RoomDrafts/FactoryOffice53FileCabinetLoot.asset",
            "instr2"=>"Assets/Remains/RoomDrafts/ToolStand/tool-stand-loot.asset",
            "trash" or "table" or "medbox"=>"Assets/Remains/RoomDrafts/Containers/office53-"+kind+"-loot.asset",
            _=>throw new InvalidOperationException("Продолжение27: нет явной loot table для "+kind)};
    }
}
