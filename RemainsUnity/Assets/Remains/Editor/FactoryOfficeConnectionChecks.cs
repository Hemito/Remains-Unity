using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryOfficeConnectionChecks
    {
        public static void Run()=>Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        public static void Check(RoomAuthoring room)
        {
            var modules=room.GetComponentsInChildren<RoomAuthoring>(true);
            var office=modules.Single(r=>r!=room && r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:53");
            bool continuation=modules.Any(r=>r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:27");
            var layout=room.CreateLayout();var local=office.CreateLayout();
            Require(Mathf.Abs(layout.right-(continuation?FactoryContinuationConnectionAuthoring.Right:modules.Any(r=>r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:40")?FactoryWarehouseConnectionAuthoring.Right:115.2f))<.001f && Vector2.Distance(office.transform.localPosition,new Vector2(76.8f,0))<.001f,
                "Модуль получил другую позицию или границы.");
            var report=office.GetComponent<RoomImportAudit>().Report;
            CheckOfficeLights(office);
            Require(report.Records.Length==46 && report.Records.All(r=>!string.IsNullOrWhiteSpace(r.reason)),"Потеряны исходные записи и причины решений.");
            bool openings=FactoryOfficeOpeningsChecks.CheckOfficeStage(office);
            Require(report.Records.Single(r=>r.code=="NMeCLKXlWsv6CYYL").installed &&
                report.Records.Single(r=>r.code=="zlnR8eeCKCDkaJ8P").installed==openings &&
                !RoomDoorContent.Read(office,"factory:0").doors.Any(d=>d.id.EndsWith(":zlnR8eeCKCDkaJ8P",StringComparison.Ordinal)),
                "Совпадающая исходная дверь скрыта или установлена дважды.");
            var connector=room.transform.Find(FactoryOfficeConnectionAuthoring.Connector);
            var connection=FactoryOfficeConnectionData.Load();
            Require(connector!=null && connector.GetComponentsInChildren<RoomSurfaceAuthoring>().Length==1,"Нет единственной рамки прохода.");
            var surface=connector.GetComponentInChildren<RoomSurfaceAuthoring>().Read(room);
            Require(FactoryLadderLandingChecks.SameSurface(surface,connection.connector),"Рамка закрыла проход либо потеряла опору.");
            var art=connector.GetComponentInChildren<RoomArtworkAuthoring>().Read(room);
            Require(FactoryLadderLandingChecks.SameRect(new Rect(art.x,art.y,art.width,art.height),surface.x,surface.y,surface.width,surface.height),
                "Графика рамки расходится с опорой.");
            var frame=room.transform.Find(FactoryOfficeConnectionAuthoring.Frame);
            Require(frame!=null && frame.GetComponentsInChildren<RoomSurfaceAuthoring>().Length==2,"Вне выбранного прохода открылись случайные клетки исходного края.");
            Require(frame.GetComponentsInChildren<RoomSurfaceAuthoring>().All(s=>connection.seamClosures.Any(e=>FactoryLadderLandingChecks.SameSurface(s.Read(room),e))),
                "Стены стыка расходятся с явными данными подключения.");
            foreach(var y in new[]{7.2f,15.2f})
            {
                var hull=new MovementHull {width=.5f,height=2};var p=ActorTerrainMotion.Move(layout,hull,new Vector2(75.3f,y),new Vector2(3,0),false);
                Require(p.x<76,"Невыбранный исходный проход оказался доступен: y="+y);
            }
            FactoryBoundaryChecks.Check(room);
            CheckContourGuard(room);
            foreach(float width in new[]{.2f,1.6f})
            foreach(int direction in new[]{-1,1})
            {
                var hull=new MovementHull {width=width,height=2};var p=new Vector2(direction>0?75:78.6f,10.4f);
                for(int i=0;i<72;i++)
                {
                    p=ActorTerrainMotion.Move(layout,hull,p,new Vector2(direction*.05f,-.01f),false);
                    Require(Mathf.Abs(p.y-10.4f)<.001f && ActorTerrainMotion.Supported(layout,hull,p) && ActorTerrainMotion.IsClear(layout,hull,p),
                        "Проход цех→офисы блокирует тело или теряет пол.");
                }
                Require(direction>0?p.x>78.5f:p.x<75.1f,"Тело не пересекло стык в обе стороны.");
            }
            // Preserve every old source surface/ladder through nested prefab authoring.
            foreach(var module in modules.Where(r=>r!=room))
            {
                float offset=module.transform.localPosition.x;var geometry=module.CreateLayout();
                foreach(var expected in geometry.platforms)
                    Require(layout.platforms.Any(s=>FactoryLadderLandingChecks.SameSurface(s,expected,offset)),"Потеряна поверхность модуля: "+module.name);
                foreach(var expected in geometry.ladders)
                    Require(layout.ladders.Any(l=>Mathf.Abs(l.x-expected.x-offset)<.001f && l.bottom==expected.bottom && l.top==expected.top),
                        "Потеряна лестница модуля: "+module.name);
            }
            var batch=RoomContentBatch.Read(room,"factory:0");var additions=RoomContentBatch.Read(office,"factory:0");
            additions.physical=FactoryOfficeFurnitureChecks.BeforeFurniture(additions.physical);
            bool officeLoot=FactoryOfficeLootChecks.CheckOfficeStage(office);
            Require(additions.physical.containers.Count(c=>c.definition=="filecab")==4 && additions.physical.containers.Length==(officeLoot?16:12) &&
                additions.physical.props.Length==3,"Нет исходных четырёх картотек/восьми прежних общих ящиков/трёх деревянных ящиков либо нового дополнения четырёх контейнеров.");
            CheckPreservation(batch);
            var independent=new WorldState {worldId="office-factory-index-check",mechanismVersion=1};
            RoomContentInstaller.Apply(independent,batch);RoomContentInstaller.Apply(independent,RoomContentBatch.Read(room,"factory:6"));
            var ids=independent.props.Select(p=>p.id).Concat(independent.containers.Select(c=>c.id)).Concat(independent.doors.Select(d=>d.id)).Concat(independent.actors.Select(a=>a.id));
            Require(ids.Distinct(StringComparer.Ordinal).Count()==ids.Count() && independent.containers.Count==batch.physical.containers.Length*2 &&
                independent.actors.Count==batch.npcs.actors.Length*2 &&
                independent.containers.SelectMany(c=>c.items).Concat(independent.actors.SelectMany(a=>a.inventory)).Select(i=>i.id).Distinct(StringComparer.Ordinal).Count()==
                    independent.containers.Sum(c=>c.items.Count)+independent.actors.Sum(a=>a.inventory.Count),
                "Фабрики0 и6 разделяют ID объекта или лута.");
            Debug.Log($"Офисы53: PASS — {local.platforms.Length} поверхностей, {local.ladders.Length} лестниц, {local.artwork.Length} слоёв, средний стык в обе стороны, замкнутый контур, 46 исходных записей, цепочка старых миров без возврата лута/удалённых объектов.");
        }
        private static void CheckContourGuard(RoomAuthoring source)
        {
            var copy=UnityEngine.Object.Instantiate(source.gameObject);
            try
            {
                var room=copy.GetComponent<RoomAuthoring>();FactoryBoundaryAuthoring.ValidateDerivedContour(room);
                var renderer=room.transform.Find(FactoryBoundaryAuthoring.GroupName).GetComponentInChildren<MeshRenderer>();renderer.sortingOrder=-7;
                bool rejected=false;try {FactoryBoundaryAuthoring.ValidateDerivedContour(room);} catch(InvalidOperationException) {rejected=true;}
                Require(rejected,"Пересборка приняла ручное изменение слоя старого внешнего контура и потеряла бы его.");
            }
            finally {UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static void CheckOfficeLights(RoomAuthoring room)
        {
            var lights=room.GetComponent<RoomLightingAuthoring>().CreateProfile(room).lights;
            foreach(var expected in new[]{("original:plant:53:record:110",11.331f),("original:plant:53:record:111",7.331f)})
            {
                var light=lights.Single(l=>l.id==expected.Item1);
                Require(light.mask=="Original/Room/Hub/light-mask-light3-1" && Mathf.Abs(light.maskX-10.499f)<.001f && Mathf.Abs(light.maskY-expected.Item2)<.001f &&
                    Mathf.Abs(light.maskWidth-6.17f)<.001f && Mathf.Abs(light.maskHeight-3.77f)<.001f,
                    "Маска light3 потеряла исходную регистрацию shape601/PNG617×377: "+light.id);
            }
        }
        private static void CheckPreservation(RoomContentBatch batch)
        {
            foreach(var step in new[]{(ContentExpansionStep)batch.physical.expansion}.Concat(batch.physical.expansion.earlierVersions))
            {
                var world=new WorldState {worldId="office-expansion-"+step.previousPatch,mechanismVersion=1};RoomContentInstaller.Apply(world,batch);
                world.contentPatches.Remove(batch.physical.patch);world.contentPatches.Add(step.previousPatch);
                world.props.RemoveAll(p=>step.additions.Contains(p.id));world.containers.RemoveAll(c=>step.additions.Contains(c.id));
                world.props.RemoveAt(0);world.containers.RemoveAt(0);
                world.props[0].x+=.2f;world.props[0].held=true;world.props[0].owner="saved-owner";world.props[0].generation=8;
                foreach(var container in world.containers) container.items.Clear();
                world.contentPatches.Remove(batch.doors.patch);world.contentPatches.Add(batch.doors.expansion.previousPatch);
                world.doors.RemoveAll(d=>batch.doors.expansion.additions.Contains(d.id));world.doors.RemoveAt(0);
                world.doors[0].open=true;world.doors[0].revision=17;
                var dead=world.actors[0];dead.health=0;dead.down=true;dead.inventory.Clear();dead.owner="saved-npc-owner";dead.generation=9;
                dead.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken> {
                    {"modBodyProgress",Newtonsoft.Json.Linq.JToken.Parse("{\"nested\":[19,\"сохранённая смерть\"]}")}};
                var armed=world.actors.First(a=>a.id!=dead.id && a.weapon=="pistol");armed.owner="another-saved-owner";armed.x+=.15f;
                foreach(var item in armed.inventory.Where(i=>i.weaponState!=null)) {item.weaponState.rounds=1;item.weaponState.revision=12;}
                var oldProps=world.props.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                var oldContainers=world.containers.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                var oldDoors=world.doors.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
                string actors=StateJson.ToJson(world.actors),mechanisms=StateJson.ToJson(world.movingPlatforms);
                int oldCount=world.props.Count+world.containers.Count;
                world=StateJson.Copy(world);Require(RoomContentInstaller.Apply(world,batch),"Старая версия не получила расширение.");
                Require(world.props.Count+world.containers.Count==oldCount+step.additions.Length &&
                    oldProps.All(p=>world.props.Any(s=>s.id==p.id && StateJson.ToJson(s)==p.json)) &&
                    oldContainers.All(p=>world.containers.Any(s=>s.id==p.id && StateJson.ToJson(s)==p.json)) &&
                    oldDoors.All(p=>world.doors.Any(s=>s.id==p.id && StateJson.ToJson(s)==p.json)) &&
                    StateJson.ToJson(world.actors)==actors && StateJson.ToJson(world.movingPlatforms)==mechanisms,
                    "Дополнение изменило прежнее положение/лут/дверь/NPC/платформу либо возродило удалённый объект.");
                string before=StateJson.ToJson(world);
                Require(!RoomContentInstaller.Apply(world,batch) && StateJson.ToJson(world)==before,"Повторный вход изменил мир.");
            }
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Офисы53: "+reason);}
    }
}
