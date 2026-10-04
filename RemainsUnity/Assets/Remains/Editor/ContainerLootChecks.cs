using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ContainerLootChecks
    {
        public static void Run()
        {
            var table=new ContainerLootTable {id="test",version="1",entries=new[] {
                new ContainerLootEntry {id="caps",definition="caps",minimum=5,maximum=20},
                new ContainerLootEntry {id="empty",definition="p10",minimum=0,maximum=0}}};
            var expected=table.Generate("world","chest");
            Require(expected.Count==1 && expected[0].count>=5 && expected[0].count<=20,"Диапазон/пустой результат нарушены.");
            Require(StateJson.ToJson(expected)==StateJson.ToJson(table.Generate("world","chest")),"Лут не воспроизводим.");
            Array.Reverse(table.entries);Require(StateJson.ToJson(expected)==StateJson.ToJson(table.Generate("world","chest")),"Перестановка строк изменила результат.");
            Require(expected[0].id!=table.Generate("other-world","chest")[0].id && expected[0].id!=table.Generate("world","other-chest")[0].id,"Миры/контейнеры разделяют ID предмета.");
            var layout=new PhysicalLayout {patch="loot-test",level="factory:0",containers=new[]{new ContainerState {id="chest",level="factory:0"}},
                loot=new[]{new ContainerLootBinding {container="chest",table=table}}};
            var world=new WorldState {worldId="world"};string authored=StateJson.ToJson(layout);
            PhysicalContentInstaller.Apply(world,layout);
            Require(world.containers[0].items.Single().id==expected[0].id && StateJson.ToJson(layout)==authored,"Установка изменила авторинг или результат.");
            world.containers[0].items.Clear();world=StateJson.Copy(world);PhysicalContentInstaller.Apply(world,layout);
            Require(world.containers[0].items.Count==0,"Загрузка восстановила лут.");
            world.contentPatches.Clear();PhysicalContentInstaller.Apply(world,layout);
            Require(world.containers[0].items.Count==0,"Существующий контейнер наполнен повторно.");
            world=new WorldState();layout.loot[0].container="missing";string before=StateJson.ToJson(world);bool rejected=false;
            try {PhysicalContentInstaller.Apply(world,layout);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && before==StateJson.ToJson(world),"Неверная привязка частично изменила мир.");
            Debug.Log("Лут контейнеров: PASS — диапазоны, пустой результат, независимость ID, воспроизводимость, загрузка и отсутствие повторной выдачи.");
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
