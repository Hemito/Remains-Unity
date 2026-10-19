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
            CheckSourceCreatureLoot();
            CheckLivingCreatureUpgrade();
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
        private static void CheckSourceCreatureLoot()
        {
            var rat=CreatureLootAuthoring.Table("rat","rat");
            Require(CreatureLootAuthoring.Table("tarakan","tarakan").entries.Length==0,"Радтаракан получил выдуманную добычу.");
            int liver=0,empty=0;
            for(int i=0;i<2000;i++)
            {
                var items=rat.Generate("creature-check",i.ToString());
                if(items.Any(s=>s.definition=="item:ratliver"))liver++;
                if(items.Count==0)empty++;
                Require(StateJson.ToJson(items)==StateJson.ToJson(rat.Generate("creature-check",i.ToString())),"Добыча существа не воспроизводима.");
            }
            Require(liver>600 && liver<800 && empty>500,"Вероятности добычи крысы заменены гарантированной выдачей.");
            var turret=CreatureLootAuthoring.Table("turret0-1","turret");int battery=0,energy=0;
            for(int i=0;i<2000;i++)
            {
                var items=turret.Generate("creature-check",i.ToString());
                bool batt=items.Any(s=>s.definition=="item:batt"),energ=items.Any(s=>s.definition=="item:energ");
                Require(!batt || !energ,"Турель выдала батареи и энергоячейки из взаимоисключающих веток.");
                if(batt)battery++;if(energ)energy++;
            }
            Require(battery>300 && battery<500 && energy>230 && energy<410,"Условные вероятности добычи турели нарушены.");
            foreach(string category in new[]{"scorp","slime","bloodwing","molerat","bloat0","bloat1","bloat2","bloat3","bloat4","turret1"})
                CreatureLootAuthoring.Table(category,category).Validate();
        }
        private static void CheckLivingCreatureUpgrade()
        {
            const string level="factory:0";
            var table=new ContainerLootTable {id="old-creature",version=CreatureLootUpgrade.TableVersion,
                entries=new[]{new ContainerLootEntry {id="source-drop",definition="item:ratliver",minimum=1,maximum=1}}};
            ActorState Actor(string id)=>new() {id=id,actorType="rat",level=level,health=3,weapon=null,owner="kept-owner",x=2,y=5,generation=7};
            var alive=Actor("alive");var dead=Actor("dead");dead.health=0;dead.down=true;
            var edited=Actor("edited");edited.inventory.Add(new ItemStack {id="manual-item",definition="caps",count=17});
            var emptied=Actor("emptied");emptied.inventoryAliases.Add("old-item","kept-history");
            var layout=new NpcLayout {patch=level+":"+FactoryCreatureAdapter.Patch,level=level,
                actors=new[]{Actor("alive"),Actor("dead"),Actor("edited"),Actor("emptied"),Actor("removed")}};
            layout.loot=layout.actors.Select(a=>new NpcLootBinding {actor=a.id,table=table}).ToArray();
            var world=new WorldState {worldId="living-creature-upgrade"};world.actors.AddRange(new[]{alive,dead,edited,emptied});
            world.contentPatches.Add(layout.patch);
            string before=StateJson.ToJson(world),deadBefore=StateJson.ToJson(dead),editedBefore=StateJson.ToJson(edited),emptiedBefore=StateJson.ToJson(emptied);
            var plan=CreatureLootUpgrade.Prepare(world,layout);
            Require(plan!=null && before==StateJson.ToJson(world),"Подготовка обновления добычи изменила живой мир.");
            Require(plan.Apply(world) && ReferenceEquals(world.Find("alive"),alive) && alive.inventory.Single().definition=="item:ratliver" &&
                alive.x==2 && alive.y==5 && alive.health==3 && alive.generation==7 && alive.owner=="kept-owner" && WorldChanges.IsMarked(world,alive),
                "Обновление живого существа заменило ссылку или физическое/сетевое состояние.");
            Require(world.actors.Count==4 && world.Find("removed")==null && StateJson.ToJson(dead)==deadBefore &&
                StateJson.ToJson(edited)==editedBefore && StateJson.ToJson(emptied)==emptiedBefore,"Обновление вернуло удалённого NPC или изменило тело/ручной инвентарь/историю.");
            alive.inventory.Clear();alive.health=0;alive.down=true;world=StateJson.Copy(world);
            before=StateJson.ToJson(world);
            Require(CreatureLootUpgrade.Prepare(world,layout)==null && !plan.Apply(world) && before==StateJson.ToJson(world),"Загрузка повторно наполнила обобранное тело.");
            var stale=new WorldState {worldId="living-creature-upgrade"};var target=Actor("alive");stale.actors.Add(target);
            plan=CreatureLootUpgrade.Prepare(stale,layout);target.down=true;target.health=0;before=StateJson.ToJson(stale);
            bool rejected=false;try {plan.Apply(stale);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && before==StateJson.ToJson(stale),"Устаревший план частично изменил умершее существо.");
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
