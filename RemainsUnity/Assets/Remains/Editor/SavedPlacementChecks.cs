using System;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class SavedPlacementChecks
    {
        public static void Run()
        {
            var room=RoomLayout.Load("hub");var hull=Anatomy.Movement("pony");
            var actor=new ActorState {id="embedded-body",level="hub",x=10.8f,y=4,health=16,mana=37,down=true};
            actor.inventory.Add(SharedInventory.Create("heal",7));actor.injuries.Add(new InjuryState{id="fracture"});
            actor.resolvedAttacks.Add("already-applied");
            var before=actor.Copy();var world=new WorldState();world.actors.Add(actor);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/saved-placement",Guid.NewGuid().ToString("N")));
            var saves=new SaveStore(directory);saves.SaveWorld(world);
            string raw=File.ReadAllText(Path.Combine(directory,"world.json"));
            Require(!ActorTerrainMotion.IsClear(room,hull,actor.Position),"Исходное тело действительно в стене");
            Require(SavedActorPlacement.Repair(actor,room,"контракт загрузки") &&
                ActorTerrainMotion.IsClear(room,hull,actor.Position) && ActorTerrainMotion.Supported(room,hull,actor.Position),
                "Тело перенесено на свободную опору");
            string backup=saves.PreserveRecoverySource();saves.SaveWorld(world);
            Require(File.ReadAllText(backup)==raw,"Исходный файл сохранён точно до перезаписи");
            var after=saves.LoadWorld(false).actors[0];
            before.Position=after.Position;before.vx=after.vx;before.vy=after.vy;
            before.revision=after.revision;before.pose=after.pose;before.poseTime=after.poseTime;
            Require(StateJson.ToJson(before)==StateJson.ToJson(after),"Не изменены здоровье, сознание, инвентарь, ранения и журналы");
            Require(!SavedActorPlacement.Repair(after,room,"повтор загрузки"),"Повторная загрузка не двигает тело");
            var ceiling=new ActorState {id="embedded-ceiling",level="hub",x=16,y=19.5f};
            Require(SavedActorPlacement.Repair(ceiling,room,"потолок") && ceiling.y+hull.height<=room.top &&
                ActorTerrainMotion.IsClear(room,hull,ceiling.Position),"Восстановление из потолка не выносит за верх комнаты");
            var free=new ActorState {id="airborne",level="hub",x=16,y=4.6f,vy=2};
            string unchanged=StateJson.ToJson(free);
            Require(!SavedActorPlacement.Repair(free,room,"полёт") && StateJson.ToJson(free)==unchanged,"Допустимый полёт не сбрасывается");
            var sealedRoom=new RoomLayout {id="too-narrow",left=0,right=.8f,platforms=new[]{
                new RoomSurface{x=0,y=-1,width=.8f,height=1}},ladders=Array.Empty<RoomLadder>()};
            var trapped=new ActorState {id="no-placement",x=.4f,y=0};
            bool rejected=false;
            try {SavedActorPlacement.Repair(trapped,sealedRoom,"нет свободного места");}
            catch(InvalidOperationException error) {rejected=error.Message.Contains("Нет свободной опоры");}
            Require(rejected && trapped.x==.4f && trapped.y==0,"Нет свободной опоры — явный отказ без выдуманной позиции");
            Debug.Log("Проверки восстановления позиций: PASS — свободная опора, точный исходник, сохранённые последствия, повторная загрузка и допустимый полёт.");
        }
        private static void Require(bool value,string detail)
        {if(!value) throw new InvalidOperationException("Восстановление позиции: "+detail);}
    }
}
