using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class TravelPointChecks
    {
        public static void Run()
        {
            Anatomy.Load();ActorTypes.Load();
            var points=RoomTravelPointAuthoring.ReadAll(RoomAuthoring.Load("hub"),"hub");
            if(points.Length!=1 || points[0].kind!=TravelPointKind.Map || points[0].id!=TravelPointAuthoring.MapId)
                throw new InvalidOperationException("Хаб должен иметь одну исходную карту worldMap.");
            var map=points[0];var room=RoomLayout.Load("hub");
            var actor=new ActorState {id="travel-check",player=true,connected=true,level="hub",x=map.arrivalX,y=map.arrivalY};
            if(!ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),actor.Position) || !ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position) || map.Rejection(actor)!=null)
                throw new InvalidOperationException("Прибытие к карте не находится на свободной опоре.");
            var world=new WorldState();world.actors.Add(actor);
            if(InteractionSelection.Select(world,actor,GameplayRuleSet.CreateDefault(),points).kind!=InteractionKind.Map)
                throw new InvalidOperationException("E рядом с картой не выбирает карту.");
            actor.x+=10;if(map.Rejection(actor)==null) throw new InvalidOperationException("Разрешена карта вне досягаемости.");
            foreach(var entry in RoomCatalogAsset.Load().Entries.Where(e=>e.id!="base:hub"))
            {
                var exits=RoomTravelPointAuthoring.ReadAll(entry.room,"factory:0");
                if(exits.Length!=1 || exits[0].kind!=TravelPointKind.Exit || exits[0].destination!="hub" || exits[0].arrivalPoint!=map.id)
                    throw new InvalidOperationException("Нет выходной двери рядом с входом: "+entry.id);
                var layout=entry.room.CreateLayout();actor.level="factory:0";actor.Position=new Vector2(layout.spawnX,layout.spawnY);
                if(exits[0].Rejection(actor)!=null) throw new InvalidOperationException("Вход уровня не имеет доступного выхода: "+entry.id);
            }
            GameAssets.Load<WorldMapView>("Presentation/WorldMap").Validate();GameAssets.Load<SessionHudView>("Presentation/SessionHud").Validate();
            foreach(var entry in RoomCatalogAsset.Load().Entries)
                foreach(var point in entry.room.GetComponentsInChildren<RoomTravelPointAuthoring>(true))
                {
                    var sprite=point.GetComponent<SpriteRenderer>();
                    if(sprite==null || sprite.sprite==null || sprite.sortingOrder!=ActorVisualLayers.WallFixtureOrder)
                        throw new InvalidOperationException("Карта/выход должна лежать на стене позади составных актёров: "+entry.id+"/"+point.name);
                }
            foreach(string prefab in new[]{"Presentation/SessionHud","Presentation/PauseMenu"})
                if(GameAssets.Load<GameObject>(prefab).GetComponentsInChildren<UnityEngine.UI.Button>(true)
                    .Any(b=>b.GetComponentInChildren<UnityEngine.UI.Text>(true)?.text is "На завод 0" or "Вернуться в хаб"))
                    throw new InvalidOperationException("В интерфейсе сохранилась кнопка телепорта: "+prefab);
            Debug.Log("Карта и выход: PASS — авторские точки, поддержанная позиция прибытия, выбор E/дальность, оба завода и отсутствие кнопок телепорта.");
        }
    }
}
