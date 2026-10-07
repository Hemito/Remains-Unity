using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationConnectionChecks
    {
        public static void Run()=>Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        public static void Check(RoomAuthoring room)
        {
            var modules=room.GetComponentsInChildren<RoomAuthoring>(true);
            var paths=modules.Where(r=>r!=room).Select(r=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            Require(modules.Length==6 && paths.SequenceEqual(new[]{FactoryLadderLandingAuthoring.EntryPath,FactoryWorkshopDraft.Path,FactoryOfficeDraft.Path,FactoryWarehouseDraft.Path,FactoryContinuationDraft.Path}.OrderBy(p=>p,StringComparer.Ordinal)),"Изменён точный набор пяти вложенных модулей.");
            var module=modules.Single(r=>r.GetComponent<RoomImportAudit>()?.Report.SourceId=="original:plant:27");
            Require(module.transform.localPosition==new Vector3(153.6f,0,0) && module.transform.localScale==Vector3.one && module.transform.localRotation==Quaternion.identity,"Изменена регистрация комнаты27.");
            FactoryContinuationChecks.Check(module);FactoryContinuationObjectsChecks.Check(module);FactoryContinuationNpcChecks.Check(module);
            var data=FactoryContinuationConnectionData.Load();var layout=room.CreateLayout();
            Require(layout.left==0 && layout.bottom==0 && layout.right==192 && layout.top==20,"Изменены границы связного уровня.");
            foreach(var entry in new[]{(FactoryContinuationConnectionAuthoring.Connector,new[]{data.upperBridge}),(FactoryContinuationConnectionAuthoring.Frame,data.seamClosures)})
            {
                var group=room.transform.Find(entry.Item1);Require(group!=null,"Нет явной группы стыка: "+entry.Item1);
                var surfaces=group.GetComponentsInChildren<RoomSurfaceAuthoring>();
                Require(surfaces.Length==entry.Item2.Length && surfaces.All(s=>entry.Item2.Count(e=>FactoryLadderLandingChecks.SameSurface(s.Read(room),e))==1),"Рамка/опора расходятся с данными.");
                foreach(var s in surfaces)
                {
                    var expected=s.Read(room);var arts=s.GetComponentsInChildren<RoomArtworkAuthoring>();
                    Require(arts.Length==1 && FactoryLadderLandingChecks.SameRect(new Rect(arts[0].Read(room).x,arts[0].Read(room).y,arts[0].Read(room).width,arts[0].Read(room).height),expected.x,expected.y,expected.width,expected.height),"Оформление рамки расходится с коллизией.");
                }
            }
            FactoryBoundaryAuthoring.ValidateDerivedContour(room);
            foreach(var portal in data.portals) foreach(float width in new[]{.2f,1.6f}) foreach(int direction in new[]{-1,1})
            {
                var hull=new MovementHull {width=width,height=2};var position=new Vector2(direction>0?151.8f:155.4f,portal.floor);
                for(int i=0;i<80;i++)
                {
                    position=ActorTerrainMotion.Move(layout,hull,position,new Vector2(direction*.05f,-.01f),false);
                    Require(Mathf.Abs(position.y-portal.floor)<.001f && ActorTerrainMotion.IsClear(layout,hull,position) && ActorTerrainMotion.Supported(layout,hull,position),"Проход теряет опору/содержит стену: "+portal.id+", width="+width+", at="+position);
                }
                Require(direction>0?position.x>155.3f:position.x<151.9f,"Проход не пересечён: "+portal.id);
            }
            foreach(float y in new[]{.8f,10.4f,16.8f})
            {
                var hull=new MovementHull {width=.5f,height=2};var p=ActorTerrainMotion.Move(layout,hull,new Vector2(152,y),new Vector2(3,0),false);
                Require(p.x<153.6f,"Открылся незаявленный проход: y="+y);
            }
            var batch=RoomContentBatch.Read(room,"factory:0");
            Require(batch.physical.patch=="factory:0:"+FactoryContinuationConnectionAuthoring.PhysicalPatch && batch.doors.patch=="factory:0:"+FactoryContinuationConnectionAuthoring.DoorPatch && batch.npcs.patch=="factory:0:"+FactoryContinuationConnectionAuthoring.NpcPatch,"Потеряны текущие версии контента.");
            Require(batch.physical.props.Length==36 && batch.physical.containers.Length==70 && batch.physical.loot.Length==68 && batch.doors.doors.Length==24 && batch.npcs.actors.Length==35 && batch.npcs.loot.Length==35 && batch.travelPoints.Length==1,"Потерян полный набор контента пяти комнат.");
            var world=new WorldState {worldId="continuation-signed-instances",mechanismVersion=1};
            foreach(string level in new[]{"factory:0","factory:6","factory:-27"}) RoomContentInstaller.Apply(world,RoomContentBatch.Read(room,level));
            var ids=world.props.Select(e=>e.id).Concat(world.containers.Select(e=>e.id)).Concat(world.doors.Select(e=>e.id)).Concat(world.actors.Select(e=>e.id)).ToArray();
            Require(ids.Length==495 && ids.Distinct(StringComparer.Ordinal).Count()==495 && world.containers.Count==210 && world.actors.Count==105,"Разные индексы разделяют ID/контент.");
            string before=StateJson.ToJson(world);foreach(string level in new[]{"factory:0","factory:6","factory:-27"}) Require(!RoomContentInstaller.Apply(world,RoomContentBatch.Read(room,level)),"Повторный вход пополнил контент.");
            Require(StateJson.ToJson(world)==before,"Повторный вход изменил сохранённые данные.");
            Debug.Log("Комната27: PASS — пять модулей, два прохода в обе стороны, замкнутые остальные края, полный контент и три независимых фабрики без повторного лута.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Подключение комнаты27: "+reason);}
    }
}
