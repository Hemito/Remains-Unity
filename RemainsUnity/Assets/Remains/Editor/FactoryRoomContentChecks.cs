using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryRoomContentChecks
    {
        public static void Run()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();PropCatalog.Load();
            var room=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/FactoryEntry46.prefab").GetComponent<RoomAuthoring>();
            var world=new WorldState {worldId="factory-room-content-check"};
            var main=RoomContentBatch.Read(room,"factory:0");var other=RoomContentBatch.Read(room,"factory:6");
            RoomContentInstaller.Apply(world,main);RoomContentInstaller.Apply(world,other);
            Require(world.doors.Count==6 && world.props.Count==8 && world.containers.Count==16 && world.actors.Count==2 && world.contentPatches.Count==6,
                "Не установлены два полных независимых экземпляра комнаты.");
            var objectIds=world.doors.Select(d=>d.id).Concat(world.props.Select(p=>p.id)).Concat(world.containers.Select(c=>c.id)).Concat(world.actors.Select(a=>a.id)).ToArray();
            Require(objectIds.Distinct().Count()==objectIds.Length,"Разные экземпляры комнаты разделяют ID объекта.");
            var items=world.containers.SelectMany(c=>c.items).ToArray();
            Require(items.Length>0 && items.Select(i=>i.id).Distinct().Count()==items.Length,"Начальные предметы имеют общие ID.");
            world.actors.Add(new ActorState {id="a",player=true,connected=true,owner="client-a",level="factory:0",x=30,y=5.6f});
            world.actors.Add(new ActorState {id="b",player=true,connected=true,owner="client-b",level="factory:6",x=30,y=5.6f});
            new OwnershipService().Reassign(world,new[]{"client-a","client-b"});
            Require(world.actors.Where(a=>!a.player).All(a=>a.owner==(a.level=="factory:0"?"client-a":"client-b")),"Владелец NPC назначен из другой фабрики.");
            world.doors[0].open=true;world.doors[0].revision=4;
            world.props[0].x+=1;world.containers.First(c=>c.items.Count>0).items.Clear();
            world.actors[0].health=0;world.actors[0].down=true;
            var loaded=StateJson.Copy(world);string before=StateJson.ToJson(loaded);
            Require(!RoomContentInstaller.Apply(loaded,main) && !RoomContentInstaller.Apply(loaded,other) && before==StateJson.ToJson(loaded),
                "Повторный вход/загрузка изменил двери, ящики, лут или тело.");
            Debug.Log($"Полное содержимое входа завода: PASS — 2 фабрики, 6 дверей, 8 физических ящиков, 16 контейнеров, 2 NPC, {items.Length} начальных стопок; независимость и сохранение проверены.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
