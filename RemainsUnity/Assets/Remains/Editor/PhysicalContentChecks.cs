using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class PhysicalContentChecks
    {
        public static void Run()
        {
            var container=new ContainerState {id="test:chest",definition="test",level="factory:0"};
            container.items.Add(new ItemStack {id="test:loot",definition="medkit",count=3});
            var layout=new PhysicalLayout {patch="test:physical:v1",level="factory:0",containers=new[]{container},
                props=new[]{new PropState {id="test:prop",definition="test",level="factory:0",anchorHeight=0}}};
            var world=new WorldState();
            Require(PhysicalContentInstaller.Apply(world,layout),"Не установлена расстановка.");
            Require(!ReferenceEquals(world.containers[0].items[0],container.items[0]),"Содержимое не скопировано.");
            world.containers[0].items.Clear();world.containers[0].x=17;world.props[0].y=8;
            world=StateJson.Copy(world);long revision=world.revision;
            Require(!PhysicalContentInstaller.Apply(world,layout) && world.revision==revision && world.containers[0].items.Count==0 &&
                world.containers[0].x==17 && world.props[0].y==8,"Загрузка восстановила лут или переместила объект.");
            world.contentPatches.Clear();PhysicalContentInstaller.Apply(world,layout);
            Require(world.containers[0].items.Count==0 && world.containers[0].x==17,"Принятие существующей расстановки изменило состояние.");
            world.props.Clear();world.containers.Clear();PhysicalContentInstaller.Apply(world,layout);
            Require(world.props.Count==0 && world.containers.Count==0,"Удалённый объект воскрес.");
            var conflict=new WorldState();conflict.doors.Add(new DoorState {id="test:chest"});
            RejectUnchanged(conflict,layout);
            conflict=new WorldState();var actor=new ActorState {id="player"};actor.inventory.Add(container.items[0].Copy());conflict.actors.Add(actor);
            RejectUnchanged(conflict,layout);
            container.items.Add(container.items[0].Copy());RejectUnchanged(new WorldState(),layout);container.items.RemoveAt(1);
            Debug.Log("Физическая расстановка: PASS — сохранение перемещения и пустого контейнера, отсутствие повторного лута, удаление, атомарный отказ конфликтов.");
        }
        private static void RejectUnchanged(WorldState world,PhysicalLayout layout)
        {
            string before=StateJson.ToJson(world);bool rejected=false;
            try {PhysicalContentInstaller.Apply(world,layout);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && StateJson.ToJson(world)==before,"Некорректная расстановка частично изменила мир.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
