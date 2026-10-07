using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Historical regression fixtures only. Never install this projection into a live world.
    public static class FactoryWarehouseHistoryProjection
    {
        private const string SourcePrefix="original:plant:40:";
        private const string PatchPrefix="base:factory-connected-46-18:";
        private const string ContinuationPrefix="original:plant:27:";
        // The approved r60 interpretation adds couch/cup/wallcab/platform1 and fixes
        // enl1 to radroach except source64: its couch overlap remains explicitly
        // pending. The 14-category prepared research plan is retained unchanged.
        internal static string[] ContinuationIds(string level,ContentEntityKind kind,string projectRoot=null)
        {
            string root=projectRoot??Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            string path=Path.Combine(root,"Tools/Research/FactoryContinuation27/content-plan.json");
            FactoryContinuationSource.RequireFile(path,"7f7a44fb2ab1228dc2fdf3e8ecec7e2796ec2207763fa5d5c09cb893154f2453");
            var data=JObject.Parse(File.ReadAllText(path),new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            var records=(data["records"] as JArray)?.Cast<JObject>().ToArray();
            Require((int?)data["schema"]==1 && (int?)data["sourceObjects"]==44 && records?.Length==44,"Нет полного source27 плана из 44 записей.");
            Unique(records.Select(r=>(string)r["recordId"]),"Source27 record IDs");
            Unique(records.Select(r=>(string)r["entityIdSuffix"]),"Source27 stable IDs");
            Require(records.All(r=>(string)r["entityIdSuffix"]==ContinuationPrefix+(string)r["code"]),"Source27 ID не соответствует исходному code.");
            Require(records.Count(r=>(string)r["kind"]=="enl1")==14 && records.Count(r=>(string)r["kind"]=="enl1" && (string)r["code"]=="icyxG67X0pCmenkm")==1,
                "Изменены14 source enl1 или явная pending64 запись.");
            bool Selected(JObject record)
            {
                string type=(string)record["kind"],disposition=(string)record["disposition"];
                return kind switch {
                    ContentEntityKind.Prop=>disposition=="planned-existing-prop" || type=="couch",
                    ContentEntityKind.Container=>disposition=="planned-existing-container" || type is "cup" or "wallcab",
                    ContentEntityKind.Door=>disposition=="planned-existing-barrier" || type=="platform1",
                    ContentEntityKind.Actor=>type=="enl1" && (string)record["code"]!="icyxG67X0pCmenkm",
                    _=>throw new InvalidOperationException("Историческая проверка27: неизвестный тип проекции "+kind)};
            }
            var ids=records.Where(Selected).Select(r=>level+":"+(string)r["entityIdSuffix"]).ToArray();
            int count=kind switch {ContentEntityKind.Prop=>6,ContentEntityKind.Container=>15,ContentEntityKind.Door=>5,_=>13};
            Require(ids.Length==count,"Изменён точный source27 набор: "+kind);return ids;
        }
        public static RoomContentBatch BeforeContinuation(RoomContentBatch source,string projectRoot=null)
        {
            if(source==null) return null;
            bool current=source.physical?.patch==source.physical?.level+":"+FactoryContinuationConnectionAuthoring.PhysicalPatch ||
                source.doors?.patch==source.doors?.level+":"+FactoryContinuationConnectionAuthoring.DoorPatch ||
                source.npcs?.patch==source.npcs?.level+":"+FactoryContinuationConnectionAuthoring.NpcPatch;
            if(current) Require(source.physical!=null && source.doors!=null && source.npcs!=null &&
                source.physical.level==source.doors.level && source.physical.level==source.npcs.level &&
                source.physical.patch==source.physical.level+":"+FactoryContinuationConnectionAuthoring.PhysicalPatch &&
                source.doors.patch==source.doors.level+":"+FactoryContinuationConnectionAuthoring.DoorPatch &&
                source.npcs.patch==source.npcs.level+":"+FactoryContinuationConnectionAuthoring.NpcPatch,"Частичная current стадия27 не скрывается исторической fixture.");
            string prefix=source.physical?.level+":"+ContinuationPrefix;
            Require(!(source.mechanisms?.movingPlatforms?.Any(p=>p?.id?.StartsWith(prefix,StringComparison.Ordinal)==true)??false) &&
                !(source.mechanisms?.mechanismButtons?.Any(p=>p?.id?.StartsWith(prefix,StringComparison.Ordinal)==true)??false) &&
                !(source.travelPoints?.Any(p=>p?.id?.StartsWith(prefix,StringComparison.Ordinal)==true)??false),"Source27 скрыт в неподдерживаемом механизме/выходе.");
            var result=StateJson.Copy(source);result.physical=BeforeContinuation(source.physical,projectRoot);
            result.doors=BeforeContinuation(source.doors,projectRoot);result.npcs=BeforeContinuation(source.npcs,projectRoot);return result;
        }
        public static PhysicalLayout BeforeContinuation(PhysicalLayout source,string projectRoot=null)
        {
            if(source==null) return null;
            Require(source.props!=null && source.containers!=null && source.loot!=null,"Нет физических массивов27.");
            var all=source.props.Cast<PropState>().Concat(source.containers).Select(p=>p?.id).ToArray();
            if(!CurrentContinuation(source.level,source.patch,FactoryContinuationConnectionAuthoring.PhysicalPatch,all.Concat(source.loot.Select(b=>b?.container)))) return StateJson.Copy(source);
            Require(source.props.Length==36 && source.containers.Length==70 && source.loot.Length==68 && source.props.Cast<PropState>().Concat(source.containers).All(p=>p!=null && p.level==source.level),"Нужны ровно36props/70containers/68loot v9 одного уровня.");
            Unique(all,"Физические ID27");Unique(source.loot.Select(b=>b?.container),"ID loot27");
            var props=ContinuationIds(source.level,ContentEntityKind.Prop,projectRoot);var containers=ContinuationIds(source.level,ContentEntityKind.Container,projectRoot);
            ExactContinuation(source.level,source.props.Select(p=>p.id),props);ExactContinuation(source.level,source.containers.Select(p=>p.id),containers);
            ExactContinuation(source.level,source.loot.Select(b=>b.container),containers);
            Require(source.loot.All(b=>b.table!=null && source.containers.Any(c=>c.id==b.container)),"Неполная/чужая таблица27.");foreach(var binding in source.loot)binding.table.Validate();
            var added=props.Concat(containers).ToArray();var result=StateJson.Copy(source);
            result.expansion=RetireContinuation<PhysicalExpansion>(source.level,source.expansion,added,all,FactoryWarehouseFurnitureAuthoring.ConnectedPatch,7);
            result.props=result.props.Where(p=>!added.Contains(p.id)).ToArray();result.containers=result.containers.Where(p=>!added.Contains(p.id)).ToArray();
            result.loot=result.loot.Where(b=>!added.Contains(b.container)).ToArray();result.patch=source.level+":"+FactoryWarehouseFurnitureAuthoring.ConnectedPatch;
            Require(result.props.Length==30 && result.containers.Length==55 && result.loot.Length==53,"Проекция27 потеряла прежнюю physical-v8.");
            BeforeWarehouse(result,projectRoot);return result;
        }
        public static DoorLayout BeforeContinuation(DoorLayout source,string projectRoot=null)
        {
            if(source==null) return null;Require(source.doors!=null,"Нет массива дверей27.");var all=source.doors.Select(d=>d?.id).ToArray();
            if(!CurrentContinuation(source.level,source.patch,FactoryContinuationConnectionAuthoring.DoorPatch,all)) return StateJson.Copy(source);
            Require(source.doors.Length==24 && source.doors.All(d=>d!=null && d.level==source.level),"Нужны ровно24двери v6 одного уровня.");Unique(all,"ID дверей27");
            var added=ContinuationIds(source.level,ContentEntityKind.Door,projectRoot);ExactContinuation(source.level,all,added);
            var result=StateJson.Copy(source);result.expansion=RetireContinuation<ContentExpansion>(source.level,source.expansion,added,all,FactoryWarehouseMetalDoorsPlacement.ConnectedPatch,4);
            result.doors=result.doors.Where(d=>!added.Contains(d.id)).ToArray();result.patch=source.level+":"+FactoryWarehouseMetalDoorsPlacement.ConnectedPatch;
            Require(result.doors.Length==19,"Проекция27 потеряла прежнюю doors-v5.");BeforeWarehouse(result,projectRoot);return result;
        }
        public static NpcLayout BeforeContinuation(NpcLayout source,string projectRoot=null)
        {
            if(source==null) return null;Require(source.actors!=null && source.loot!=null,"Нет массивов NPC27.");var all=source.actors.Select(a=>a?.id).ToArray();
            if(!CurrentContinuation(source.level,source.patch,FactoryContinuationConnectionAuthoring.NpcPatch,all.Concat(source.loot.Select(b=>b?.actor)))) return StateJson.Copy(source);
            Require(source.actors.Length==35 && source.loot.Length==35 && source.actors.All(a=>a!=null && a.level==source.level),"Нужны ровно35NPC/loot v6 одного уровня; source64 остаётся pending.");
            Unique(all,"ID NPC27");Unique(source.loot.Select(b=>b?.actor),"ID NPC inventory27");var added=ContinuationIds(source.level,ContentEntityKind.Actor,projectRoot);
            ExactContinuation(source.level,all,added);ExactContinuation(source.level,source.loot.Select(b=>b.actor),added);
            Require(source.loot.All(b=>b.table!=null && source.actors.Any(a=>a.id==b.actor)),"Неполная/чужая таблица NPC27.");foreach(var binding in source.loot)binding.table.Validate();
            var result=StateJson.Copy(source);result.expansion=RetireContinuation<ContentExpansion>(source.level,source.expansion,added,all,FactoryWarehouseConnectionAuthoring.NpcPatch,4);
            result.actors=result.actors.Where(a=>!added.Contains(a.id)).ToArray();result.loot=result.loot.Where(b=>!added.Contains(b.actor)).ToArray();result.patch=source.level+":"+FactoryWarehouseConnectionAuthoring.NpcPatch;
            Require(result.actors.Length==22 && result.loot.Length==22,"Проекция27 потеряла прежнюю npcs-v5.");BeforeWarehouse(result,projectRoot);return result;
        }
        private static bool CurrentContinuation(string level,string patch,string suffix,IEnumerable<string> ids)
        {
            Require(!string.IsNullOrWhiteSpace(level) && !string.IsNullOrWhiteSpace(patch),"Нет уровня/версии27.");
            if(patch==level+":"+suffix) return true;
            Require(!ids.Any(id=>id?.StartsWith(level+":"+ContinuationPrefix,StringComparison.Ordinal)==true),"Source27 скрыт в прежней/неизвестной версии: "+patch);return false;
        }
        private static void ExactContinuation(string level,IEnumerable<string> actual,string[] expected)
            =>Require(Same(actual.Where(id=>id.StartsWith(level+":"+ContinuationPrefix,StringComparison.Ordinal)),expected),"Неполные/чужие source27 IDs: "+level);
        private static T RetireContinuation<T>(string level,ContentExpansion expansion,string[] added,string[] known,string predecessor,int earlierCount) where T:ContentExpansion,new()
        {
            Require(expansion?.previousPatch==level+":"+predecessor && expansion.additions!=null && Same(expansion.additions,added) &&
                expansion.earlierVersions?.Length==earlierCount,"Изменён точный последний predecessor/additions27.");Unique(expansion.additions,"Последний шаг27");
            foreach(var step in expansion.earlierVersions)
            {
                Require(step!=null && step.additions!=null,"Потерян earlier шаг27.");Unique(step.additions,"Cumulative additions27");
                Require(added.All(id=>step.additions.Contains(id)) && step.additions.All(id=>known.Contains(id)),"Cumulative шаг потерял/подменил additions27.");
            }
            return Retire<T>(expansion,added);
        }
        public static RoomContentBatch BeforeWarehouse(RoomContentBatch source,string projectRoot=null)
        {
            if(source==null) return null;
            source=BeforeContinuation(source,projectRoot);
            var result=StateJson.Copy(source);
            result.physical=BeforeWarehouse(source.physical,projectRoot);
            result.doors=BeforeWarehouse(source.doors,projectRoot);
            result.npcs=BeforeWarehouse(source.npcs,projectRoot);
            return result;
        }
        public static PhysicalLayout BeforeWarehouse(PhysicalLayout source,string projectRoot=null)
        {
            if(source==null) return null;
            source=BeforeContinuation(source,projectRoot);
            Require(source.props!=null && source.containers!=null && source.loot!=null,"Нет физических массивов.");
            source=FactoryWarehouseFurnitureAuthoring.BeforeFurniture(source,projectRoot);
            string[] all=source.props.Cast<PropState>().Concat(source.containers).Select(p=>p?.id).ToArray();
            if(!Current(source.level,source.patch,FactoryWarehouseConnectionAuthoring.PhysicalPatch,all.Concat(source.loot.Select(b=>b?.container))))
                return StateJson.Copy(source);
            var plan=Load(projectRoot);string[] props=Ids(plan,"planned-existing-prop",source.level,17),containers=Ids(plan,"planned-existing-container",source.level,16);
            string[] added=props.Concat(containers).ToArray();
            Require(source.props.Length==29 && source.containers.Length==52 && source.loot.Length==50,"Нужны ровно 29 props/52 контейнера/50 таблиц v7.");
            Unique(all,"Физические ID");Unique(source.loot.Select(b=>b?.container),"ID таблиц контейнеров");
            ExactWarehouse(source.level,source.props.Select(p=>p.id),props);ExactWarehouse(source.level,source.containers.Select(c=>c.id),containers);
            ExactWarehouse(source.level,source.loot.Select(b=>b.container),containers);
            Require(source.loot.All(b=>b.table!=null) && source.loot.All(b=>source.containers.Any(c=>c.id==b.container)),"Таблица отсутствует или указывает не на контейнер.");
            foreach(var binding in source.loot) binding.table.Validate();
            foreach(var state in source.props.Cast<PropState>().Concat(source.containers))
                Require(state.level==source.level,"Физический объект другого уровня: "+state.id);
            foreach(var record in plan.Where(r=>added.Contains(source.level+":"+(string)r["entityIdSuffix"])))
            {
                var state=source.props.Cast<PropState>().Concat(source.containers).Single(p=>p.id==source.level+":"+(string)record["entityIdSuffix"]);
                Require(state.definition==(string)record["kind"],"Неверный тип складского объекта: "+state.id);
            }
            string prefix=source.level+":";
            var furniture=FactoryOfficeFurnitureAuthoring.Records.Select(r=>prefix+r.Id).ToArray();
            var loot=FactoryOfficeLootAuthoring.Containers.Select(r=>prefix+r.Id).ToArray();
            var office=all.Where(id=>id.StartsWith(prefix+"original:plant:53:",StringComparison.Ordinal) && !furniture.Contains(id) && !loot.Contains(id)).ToArray();
            Require(office.Length==15,"Потеряна исходная физическая группа офиса из 15 ID.");
            ValidateChain(source.level,source.expansion,all,"physical",new[]{added,furniture,loot,office,
                new[]{prefix+FactoryToolStandAuthoring.SourceId},new[]{prefix+"original:plant:18:ZoIAvJZMFks7XYbh",prefix+"original:plant:18:Ainu8fDKejKYdTIz"}});
            var result=StateJson.Copy(source);
            result.props=result.props.Where(p=>!added.Contains(p.id)).ToArray();result.containers=result.containers.Where(c=>!added.Contains(c.id)).ToArray();
            result.loot=result.loot.Where(b=>!added.Contains(b.container)).ToArray();result.patch=prefix+FactoryOfficeFurnitureAuthoring.ConnectedPatch;
            result.expansion=Retire<PhysicalExpansion>(source.expansion,added);
            Require(result.props.Length==12 && result.containers.Length==36 && result.loot.Length==34,"Неверная историческая физическая fixture v6.");
            return result;
        }
        public static DoorLayout BeforeWarehouse(DoorLayout source,string projectRoot=null)
        {
            if(source==null) return null;source=BeforeContinuation(source,projectRoot);Require(source.doors!=null,"Нет массива дверей.");
            source=FactoryWarehouseMetalDoorsPlacement.BeforeMetalDoors(source,projectRoot);
            var all=source.doors.Select(d=>d?.id).ToArray();
            if(!Current(source.level,source.patch,FactoryWarehouseConnectionAuthoring.DoorPatch,all)) return StateJson.Copy(source);
            var added=Ids(Load(projectRoot),"planned-existing-door",source.level,2);Unique(all,"ID дверей");
            Require(source.doors.Length==17 && source.doors.All(d=>d.level==source.level),"Нужны ровно 17 дверей одного уровня v4.");
            ExactWarehouse(source.level,all,added);
            string prefix=source.level+":";
            var office=source.doors.Where(d=>d.id.StartsWith(prefix+"original:plant:53:",StringComparison.Ordinal)).ToArray();
            var openings=office.Where(d=>d.integrity!=null).Select(d=>d.id).ToArray();
            var initial=office.Where(d=>d.integrity==null).Select(d=>d.id).ToArray();
            Require(openings.Length==6 && initial.Length==3,"Потеряны шесть проёмов и три прежние двери офиса.");
            ValidateChain(source.level,source.expansion,all,"doors",new[]{added,openings,initial});
            var result=StateJson.Copy(source);result.doors=result.doors.Where(d=>!added.Contains(d.id)).ToArray();
            result.patch=prefix+FactoryOfficeOpeningsAuthoring.ConnectedPatch;result.expansion=Retire<ContentExpansion>(source.expansion,added);
            Require(result.doors.Length==15,"Неверная историческая дверная fixture v3.");return result;
        }
        public static NpcLayout BeforeWarehouse(NpcLayout source,string projectRoot=null)
        {
            if(source==null) return null;source=BeforeContinuation(source,projectRoot);Require(source.actors!=null && source.loot!=null,"Нет массивов NPC/инвентарей.");
            var all=source.actors.Select(a=>a?.id).ToArray();
            if(!Current(source.level,source.patch,FactoryWarehouseConnectionAuthoring.NpcPatch,all.Concat(source.loot.Select(b=>b?.actor)))) return StateJson.Copy(source);
            var added=Ids(Load(projectRoot),"planned-fixed-unity-encounter",source.level,9);Unique(all,"ID NPC");Unique(source.loot.Select(b=>b?.actor),"ID инвентарей NPC");
            Require(source.actors.Length==22 && source.loot.Length==22 && source.actors.All(a=>a.level==source.level),"Нужны ровно 22 NPC/инвентаря одного уровня v5.");
            ExactWarehouse(source.level,all,added);ExactWarehouse(source.level,source.loot.Select(b=>b.actor),added);
            Require(source.loot.All(b=>b.table!=null && source.actors.Any(a=>a.id==b.actor)),"Инвентарь NPC отсутствует или указывает не на тело.");
            foreach(var binding in source.loot) binding.table.Validate();
            string prefix=source.level+":";
            var office=FactoryOfficeNpcAuthoring.Spawns.Select(s=>prefix+s.Id).ToArray();
            var workshop=source.actors.Where(a=>a.id.StartsWith(prefix+"original:plant:18:",StringComparison.Ordinal)).ToArray();
            var crawlers=workshop.Where(a=>a.actorType=="radroach").Select(a=>a.id).ToArray();
            var raiders=workshop.Where(a=>a.actorType=="raider").Select(a=>a.id).ToArray();
            Require(crawlers.Length==2 && raiders.Length==5,"Потеряна прежняя группа цеха: 2 радтаракана/5 рейдеров.");
            ValidateChain(source.level,source.expansion,all,"npcs",new[]{added,office,crawlers,raiders});
            var result=StateJson.Copy(source);result.actors=result.actors.Where(a=>!added.Contains(a.id)).ToArray();result.loot=result.loot.Where(b=>!added.Contains(b.actor)).ToArray();
            result.patch=prefix+FactoryOfficeNpcAuthoring.ConnectedPatch;result.expansion=Retire<ContentExpansion>(source.expansion,added);
            Require(result.actors.Length==13 && result.loot.Length==13,"Неверная историческая fixture NPC v4.");return result;
        }
        private static bool Current(string level,string patch,string suffix,IEnumerable<string> ids)
        {
            Require(!string.IsNullOrWhiteSpace(level) && !string.IsNullOrWhiteSpace(patch),"Нет уровня/версии.");
            if(patch==level+":"+suffix) return true;
            Require(!ids.Any(id=>id!=null && id.StartsWith(level+":"+SourcePrefix,StringComparison.Ordinal)),"Прежняя/неизвестная версия скрывает складские ID: "+patch);
            return false;
        }
        private static void ValidateChain(string level,ContentExpansion expansion,string[] known,string kind,string[][] groups)
        {
            Require(expansion?.earlierVersions!=null && expansion.earlierVersions.Length==groups.Length-1,"Потеряна полная цепочка: "+kind);
            var steps=new[]{(ContentExpansionStep)expansion}.Concat(expansion.earlierVersions).ToArray();
            var expected=new List<string>();
            for(int i=0;i<steps.Length;i++)
            {
                expected.AddRange(groups[i]);Unique(expected,"ID накопленного дополнения "+kind);
                var step=steps[i];string previous=level+":"+PatchPrefix+kind+"-v"+(groups.Length-i);
                Require(step!=null && step.previousPatch==previous && step.additions!=null,"Неверный порядок/версия predecessor: "+previous);
                Unique(step.additions,"ID шага "+previous);
                Require(Same(step.additions,expected) && step.additions.All(id=>known.Contains(id)),"Изменены точные cumulative ID шага: "+previous);
            }
        }
        private static T Retire<T>(ContentExpansion expansion,string[] added) where T:ContentExpansion,new()
        {
            var steps=expansion.earlierVersions;
            return new T {previousPatch=steps[0].previousPatch,additions=steps[0].additions.Where(id=>!added.Contains(id)).ToArray(),
                earlierVersions=steps.Skip(1).Select(s=>new ContentExpansionStep {previousPatch=s.previousPatch,additions=s.additions.Where(id=>!added.Contains(id)).ToArray()}).ToArray()};
        }
        private static JObject[] Load(string projectRoot)
        {
            string root=projectRoot??Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            string path=Path.Combine(root,"Tools/Research/FactoryWarehouse40/content-plan.json");
            var data=JObject.Parse(File.ReadAllText(path),new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            var records=(data["records"] as JArray)?.Cast<JObject>().ToArray();
            Require((int?)data["schema"]==1 && (int?)data["sourceObjects"]==57 && records?.Length==57,"Изменён исходный пакет склада: "+path);
            Unique(records.Select(r=>(string)r["recordId"]),"Исходные записи склада");
            Unique(records.Select(r=>(string)r["entityIdSuffix"]),"Исходные stable ID склада");
            Require(records.All(r=>(string)r["entityIdSuffix"]==SourcePrefix+(string)r["code"]),"Складской stable ID не соответствует source code.");
            return records;
        }
        private static string[] Ids(JObject[] records,string disposition,string level,int count)
        {
            var ids=records.Where(r=>(string)r["disposition"]==disposition).Select(r=>level+":"+(string)r["entityIdSuffix"]).ToArray();
            Require(ids.Length==count,"Неверный точный набор пакета: "+disposition);Unique(ids,disposition);return ids;
        }
        private static void ExactWarehouse(string level,IEnumerable<string> actual,string[] expected)
        {Require(Same(actual.Where(id=>id.StartsWith(level+":"+SourcePrefix,StringComparison.Ordinal)),expected),"Неполные/лишние складские ID: "+level);}
        private static bool Same(IEnumerable<string> a,IEnumerable<string> b)=>a.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(b.OrderBy(id=>id,StringComparer.Ordinal));
        private static void Unique(IEnumerable<string> source,string context)
        {var ids=source.ToArray();Require(!ids.Any(string.IsNullOrWhiteSpace) && ids.Distinct(StringComparer.Ordinal).Count()==ids.Length,"Пустые/повторённые "+context);}
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Историческая проверка до склада40: "+reason);}
    }
}
