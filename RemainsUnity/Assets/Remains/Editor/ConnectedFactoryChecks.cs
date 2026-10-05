using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ConnectedFactoryChecks
    {
        public static void Run()=>Check(RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId));
        public static void Check(RoomAuthoring room)
        {
            var layout=room.CreateLayout();var batch=RoomContentBatch.Read(room,"factory:0");
            int exterior=FactoryBoundaryChecks.Check(room);
            Require(layout.right==76.8f && layout.top==20 && layout.platforms.Length==127+exterior && layout.ladders.Length==8 && layout.artwork.Length==325+exterior,
                "Потеряны границы или геометрия модулей.");
            Require(batch.doors.doors.Length==6 && batch.physical.props.Length==6 && batch.physical.containers.Length==14 &&
                batch.physical.loot.Length==12 && batch.npcs.actors.Length==1 && batch.travelPoints.Length==1,
                "Потеряно содержимое модулей или размножена выходная дверь.");
            var lights=room.GetComponent<RoomLightingAuthoring>().CreateProfile(room);
            Require(lights.lights.Length==15 && lights.lights.Select(l=>l.id).Distinct().Count()==15 &&
                lights.lights.Count(l=>l.x>38.4f)==2,"Потеряны или не смещены источники света цеха.");
            var record=room.GetComponentInChildren<RoomImportAudit>().Report;
            Require(record.Records.Length==24 && record.Records.Count(r=>!r.installed)==12,"Пропуски исходного содержимого скрыты или потеряны.");
            var ammo=batch.physical.containers.Single(c=>c.id.EndsWith(":sjN3zN7dP8aCZGe6",StringComparison.Ordinal));
            Require(Mathf.Abs(ammo.x-40.4f)<.001f && Mathf.Abs(ammo.y-10.4f)<.001f,"Контейнер соседней комнаты не получил смещение модуля.");
            foreach(float width in new[]{.2f,1.6f})
            foreach(int direction in new[]{-1,1})
            {
                var hull=new MovementHull {width=width,height=2};var start=new Vector2(direction>0?37:40,10.4f);
                for(int i=0;i<60;i++)
                {
                    start=ActorTerrainMotion.Move(layout,hull,start,new Vector2(direction*.05f,-.01f),false);
                    Require(Mathf.Abs(start.y-10.4f)<.001f && ActorTerrainMotion.IsClear(layout,hull,start) && ActorTerrainMotion.Supported(layout,hull,start),
                        "Стык комнат содержит стену, провал или теряет опору.");
                }
                Require(direction>0?start.x>39.9f:start.x<37.1f,"Пересечение стыка заблокировано.");
            }
            var world=new WorldState {worldId="connected-factory-content-check"};RoomContentInstaller.Apply(world,batch);
            Require(world.containers.Count==14 && world.doors.Count==6 && world.actors.Count==1,"Расстановка связного мира потеряла объекты.");
            var container=world.containers.Single(c=>c.id==ammo.id);container.items.Clear();container.x+=.2f;
            var restored=StateJson.Copy(world);var before=StateJson.ToJson(restored);
            Require(!RoomContentInstaller.Apply(restored,batch) && StateJson.ToJson(restored)==before,"Повторный вход пополнил лут или сбросил положение.");
            Debug.Log("Связный завод: PASS — сохранённые модули/контент/свет, проход стыка в обе стороны с разной шириной тела, устойчивые ID и повторная загрузка без сброса лута.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Связный завод: "+reason);}
    }
}
