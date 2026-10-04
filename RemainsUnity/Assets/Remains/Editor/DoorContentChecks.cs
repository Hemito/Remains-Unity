using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorContentChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var door=new DoorState {id="authored",definition="door1",level="factory:0",x=1,y=0,width=.8f,height=1.6f,surfaceTexture="door:wood"};
            var layout=new DoorLayout {patch="test:doors:v1",level=door.level,doors=new[]{door}};
            Require(DoorContentInstaller.Apply(world,layout) && world.doors.Count==1 && !ReferenceEquals(world.doors[0],door),"Установка не скопировала расстановку.");
            world.doors[0].open=true;world.doors[0].revision=4;
            var loaded=StateJson.FromJson<WorldState>(StateJson.ToJson(world));long revision=loaded.revision;
            Require(!DoorContentInstaller.Apply(loaded,layout) && loaded.doors[0].open && loaded.doors[0].revision==4 && loaded.revision==revision,
                "Повторный вход/загрузка сбросил состояние двери.");
            loaded.contentPatches.Clear();DoorContentInstaller.Apply(loaded,layout);
            Require(loaded.doors.Count==1 && loaded.doors[0].open && loaded.doors[0].revision==4,"Принятие существующей двери потеряло состояние.");
            loaded.doors.Clear();Require(!DoorContentInstaller.Apply(loaded,layout) && loaded.doors.Count==0,"Установленная расстановка воскресила удалённую дверь.");
            var conflict=new WorldState();conflict.props.Add(new PropState {id="occupied"});
            var second=StateJson.FromJson<DoorState>(StateJson.ToJson(door));second.id="occupied";
            var broken=new DoorLayout {patch="test:conflict",level=door.level,doors=new[]{door,second}};
            string before=StateJson.ToJson(conflict);bool rejected=false;
            try {DoorContentInstaller.Apply(conflict,broken);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && StateJson.ToJson(conflict)==before,"Конфликт частично изменил мир.");
            world.contentPatches.Clear();door.x=2;before=StateJson.ToJson(world);rejected=false;
            try {DoorContentInstaller.Apply(world,layout);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && StateJson.ToJson(world)==before,"Неявная миграция перенесла сохранённую дверь.");
            Debug.Log("Расстановка дверей: PASS — однократная установка, сохранение/загрузка, принятие существующих, удаление и атомарный отказ конфликтов.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
