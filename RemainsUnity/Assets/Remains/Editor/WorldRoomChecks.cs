using System;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WorldRoomChecks
    {
        public static void InstallCatalog()
        {
            const string path="Assets/Remains/Resources/Content/RoomCatalog.asset";
            if(AssetDatabase.LoadAssetAtPath<RoomCatalogAsset>(path)!=null)
                throw new InvalidOperationException("Каталог комнат уже существует; авторские изменения не заменены.");
            RoomAuthoring Read(string source)=>AssetDatabase.LoadAssetAtPath<GameObject>(source).GetComponent<RoomAuthoring>();
            var catalog=ScriptableObject.CreateInstance<RoomCatalogAsset>();
            catalog.Configure(new[]{
                new RoomCatalogAsset.Entry {id="base:hub",room=Read("Assets/Remains/Resources/Levels/hub.prefab")},
                new RoomCatalogAsset.Entry {id="base:factory-prototype",room=Read("Assets/Remains/Resources/Levels/factory.prefab")},
                new RoomCatalogAsset.Entry {id="base:factory-entry-46",room=Read("Assets/Remains/RoomDrafts/FactoryEntry46.prefab")}});
            AssetDatabase.CreateAsset(catalog,path);AssetDatabase.SaveAssets();
            ContractChecks.Run();
        }
        public static void Run()
        {
            var legacy=StateJson.FromJson<WorldState>("{\"schema\":1,\"worldId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}");
            Require(WorldRooms.Definition(legacy,"factory:0")=="base:factory-prototype","Старое сохранение изменило комнату.");
            var world=new WorldState();world.rooms.Add(new WorldRoom {level="factory:0",definition="base:factory-entry-46"});
            var restored=new RecoverySnapshot(StateJson.ToJson(world)).CreateLiveWorld();
            var room=RoomAuthoring.Load("factory:0",restored);
            Require(room==RoomCatalogAsset.Load().Resolve("base:factory-entry-46"),"Снимок потерял выбор комнаты.");
            var layout=RoomLayout.Load("factory:0",restored);var old=RoomLayout.Load("factory:0",legacy);
            Require(layout.right==38.4f && layout.top==20 && old.left==-10 && old.right==12,"Смешаны геометрии старого и нового заводов.");
            var actor=new ActorState();TravelRules.Apply(actor,"factory:0",layout);
            Require(actor.x==layout.spawnX && actor.y==layout.spawnY,"Вход использует координаты другой комнаты.");
            RoomContentInstaller.Apply(restored,RoomContentBatch.Read(room,"factory:0"));
            Require(restored.doors.Count==3 && restored.containers.Count==8 && restored.props.Count==4 && restored.actors.Count==1,"Содержимое не соответствует выбранной комнате.");
            restored.rooms[0].definition="missing:room";
            bool rejected=false;try {RoomAuthoring.Load("factory:0",restored);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Неизвестное определение комнаты молча заменено.");
            world.rooms.Add(new WorldRoom {level="factory:0",definition="base:factory-prototype"});
            rejected=false;try {WorldRooms.Definition(world,"factory:0");} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Два определения одной локации приняты.");
            Debug.Log("Выбор комнаты мира: PASS — старые сейвы, восстановительный снимок, новая геометрия/вход/контент, отказ неизвестного и повторного ID.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
