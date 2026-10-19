using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class CreatureDeathLootChecks
    {
        private sealed class LethalDamage : IDamageRule {public float Calculate(string weapon,float speed)=>100000;}
        public static void RunBatch()
        {try {Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        private static ContainerLootTable Guaranteed(string definition="item:scrap",float probability=1)=>new()
        {id="turret-death-check",version="source-v1",entries=new[]{new ContainerLootEntry {id="source-scrap",definition=definition,minimum=1,maximum=1,probability=probability}}};
        public static void Run()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            CheckCatalog();CheckAcceptedAndReplay();CheckGuardsAndFailure();CheckNamespaceIndex();
            Debug.Log("Добыча принятой гибели турели: PASS — исходные таблицы, отдельное оружие, actual lethal state, life receipt/zero result/save/handoff, foreign/stale guards и атомарный preflight.");
        }
        private static void CheckCatalog()
        {
            var entries=CreatureCatalog.Load().Creatures.Where(c=>c.family==NpcCreatureBehaviours.Turret).ToArray();
            Require(entries.Length==9,"Не подготовлены девять вариантов турелей.");
            foreach(var entry in entries)
            {
                Require(entry.deathLoot!=null && !ActorTypes.Get(entry.actorType).bodyInventory,"Нет отдельной таблицы или открыт инвентарь установленного оружия: "+entry.actorType);
                var table=entry.deathLoot.Read();
                Require(table.entries.Length>=4 && table.entries.All(e=>e.definition.StartsWith("item:",StringComparison.Ordinal) && e.weaponState==null),"Установленное оружие включено в source loot: "+entry.actorType);
                Require(entry.spawn.ReadInventory().entries.Length==1 && entry.spawn.ReadInventory().entries[0].weaponState!=null,"Таблица добычи изменила внутренний mounted inventory.");
                foreach(var row in table.entries)DroppedItemShapeCatalog.Require(row.definition);
            }
        }
        private static (WorldState world,ActorState before) World(string id)
        {
            var source=CreatureCatalog.Load().Creatures.First(c=>c.family==NpcCreatureBehaviours.Turret);
            var actor=source.spawn.Create(new TrainingPlacement {id="turret",type=source.actorType,facing=1,x=2,y=3},"factory:0");
            string worldId;using(var sha=System.Security.Cryptography.SHA256.Create())worldId=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(id))).Replace("-","").Substring(0,32).ToLowerInvariant();
            var world=new WorldState {worldId=worldId};
            NpcContentInstaller.Apply(world,new NpcLayout {patch="death-check",level=actor.level,actors=new[]{actor},
                loot=new[]{new NpcLootBinding {actor=actor.id,table=source.spawn.ReadInventory()}}});
            actor=world.Find(actor.id);actor.owner="owner";actor.generation=3;actor.lifecycleSequence=4;actor.revision=9;
            CreatureDeathLootIds.PrepareWorld(world);
            return (world,actor);
        }
        private static ActorState Death(ActorState before)
        {
            var after=before.Copy();DamageResult outcome=default;
            var receiver=new DamageReceiver(new LethalDamage());receiver.Applied+=r=>outcome=r;
            Require(receiver.Apply(after,new AttackEvent {id="lethal:"+before.revision+":"+before.generation,attacker="source",weapon="pistol",level=before.level},true) && outcome.confirmedNpcDeath,"Фактический смертельный урон не дал состояние гибели NPC.");
            return after;
        }
        private static void CheckAcceptedAndReplay()
        {
            var (world,before)=World("accepted-death");var after=Death(before);var table=Guaranteed();
            string saved=StateJson.ToJson(world),gun=StateJson.ToJson(before.inventory);
            var plan=CreatureDeathLoot.Prepare(world,before,after,_=>table);
            Require(plan!=null && saved==StateJson.ToJson(world),"Подготовка гибели изменила канонический мир.");
            Require(plan.ApplyAccepted(world) && ReferenceEquals(world.Find(before.id),after) && world.containers.Count==1 &&
                world.completedTransactions.Contains(CreatureDeathLoot.Receipt(before)),"Принятая гибель не создала один источник добычи и квитанцию.");
            world.revision++;// The real UpdateActor advances revision before its existing death checkpoint.
            var drop=world.containers.Single();
            Require(drop.items.Single().definition=="item:scrap" && StateJson.ToJson(after.inventory)==gun && InventorySource.FromBody(after)==null,
                "Добыча изменила mounted inventory или открыла тело турели.");
            Require(drop.vx>=-3 && drop.vx<3 && drop.vy>3 && drop.vy<=6 && WorldChanges.IsMarked(world,drop),"Скорость source Loot(jump=true) не переведена через 30/50 или drop не отмечен.");
            Require(!plan.ApplyAccepted(world),"Повтор принятой гибели выдал добычу ещё раз.");
            var recovered=new RecoverySnapshot(StateJson.ToJson(world));
            world=StateJson.FromJson<WorldState>(recovered.Json);
            CreatureDeathLootIds.PrepareWorld(world);
            world.containers.Clear();// Picked up and pruned: the life receipt must outlive its output.
            var canonical=world.Find(before.id);
            Require(CreatureDeathLoot.Prepare(world,canonical,canonical.Copy(),_=>table)==null,"Мёртвый снимок повторил добычу.");
            canonical.down=false;canonical.health=10;canonical.generation++;canonical.owner="new-owner";canonical.revision++;
            var replay=Death(canonical);table.version="new-source-version";
            Require(CreatureDeathLoot.Prepare(world,canonical,replay,_=>table)==null && world.containers.Count==0,"Handoff или новая версия таблицы обнулили квитанцию жизни.");
            var (emptyWorld,emptyBefore)=World("empty-death");var emptyAfter=Death(emptyBefore);
            var emptyPlan=CreatureDeathLoot.Prepare(emptyWorld,emptyBefore,emptyAfter,_=>Guaranteed(probability:0));
            Require(emptyPlan.ApplyAccepted(emptyWorld) && emptyWorld.containers.Count==0 && emptyWorld.completedTransactions.Contains(CreatureDeathLoot.Receipt(emptyBefore)),"Пустой случайный результат не сохранил квитанцию жизни.");
            // Deterministic source content and motion, independent of the death owner's generation.
            var (repeatWorld,repeatBefore)=World("accepted-death");repeatBefore.generation=8;
            Require(CreatureDeathLoot.Prepare(repeatWorld,repeatBefore,Death(repeatBefore),_=>Guaranteed()).ApplyAccepted(repeatWorld) &&
                StateJson.ToJson(repeatWorld.containers)==StateJson.ToJson(StateJson.FromJson<WorldState>(recovered.Json).containers),"Смена поколения владельца изменила deterministic loot/motion.");
        }
        private static void CheckGuardsAndFailure()
        {
            for(int fault=0;fault<9;fault++)
            {
                var (world,before)=World("guard-"+fault);var after=Death(before);
                if(fault==0)after.id="foreign";if(fault==1)after.actorType="rat";if(fault==2)after.level="hub";
                if(fault==3)after.generation++;if(fault==4)after.lifecycleSequence++;if(fault==5)after.owner="foreign-owner";
                if(fault==6)after.revision=before.revision;if(fault==7)world.actors.Clear();if(fault==8)before.down=true;
                string saved=StateJson.ToJson(world);
                Require(CreatureDeathLoot.Prepare(world,before,after,_=>Guaranteed())==null && saved==StateJson.ToJson(world),"Foreign/stale/removed/dead состояние создало добычу: "+fault);
            }
            var (badWorld,badBefore)=World("shape-preflight");string snapshot=StateJson.ToJson(badWorld);bool failed=false;
            try {CreatureDeathLoot.Prepare(badWorld,badBefore,Death(badBefore),_=>Guaranteed("weapon:a_magic:grade:0"));}
            catch(InvalidOperationException){failed=true;}
            Require(failed && snapshot==StateJson.ToJson(badWorld),"Ошибка формы частично приняла гибель или потратила предмет.");
            var (collisionWorld,collisionBefore)=World("collision-preflight");var collisionTable=Guaranteed();
            var item=collisionTable.Generate(collisionWorld.worldId,CreatureDeathLoot.Receipt(collisionBefore)).Single();
            collisionWorld.containers.Add(new ContainerState {id=CreatureDeathLootIds.DropPrefix+CreatureDeathLootIds.ItemPrefix+item.id,level=collisionBefore.level});
            CreatureDeathLootIds.PrepareWorld(collisionWorld);snapshot=StateJson.ToJson(collisionWorld);failed=false;
            try {CreatureDeathLoot.Prepare(collisionWorld,collisionBefore,Death(collisionBefore),_=>collisionTable);}catch(InvalidOperationException){failed=true;}
            Require(failed && snapshot==StateJson.ToJson(collisionWorld),"Конфликт ID допустил частичную публикацию гибели.");
            var (staleWorld,staleBefore)=World("same-revision-mutation");var staleAfter=Death(staleBefore);
            var stalePlan=CreatureDeathLoot.Prepare(staleWorld,staleBefore,staleAfter,_=>Guaranteed());
            staleAfter.x+=1;snapshot=StateJson.ToJson(staleWorld);failed=false;
            try {stalePlan.ApplyAccepted(staleWorld);}catch(InvalidOperationException){failed=true;}
            Require(failed && snapshot==StateJson.ToJson(staleWorld),"Изменение актёра без новой ревизии не инвалидировало план гибели.");
            failed=false;try {CreatureDeathLootIds.RejectForeignCreation(new[]{CreatureDeathLootIds.ItemPrefix+"forged"},"regression");}catch(InvalidOperationException){failed=true;}
            Require(failed,"Сторонний writer получил зарезервированный namespace добычи.");
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        private static void CheckNamespaceIndex()
        {
            string itemId=CreatureDeathLootIds.ItemPrefix+"index-test",dropId=CreatureDeathLootIds.DropPrefix+"index-test";
            var world=new WorldState {worldId="index-validation"};
            var body=new ContainerState {id=dropId};body.items.Add(new ItemStack {id=itemId,definition="item:scrap",count=1});world.containers.Add(body);
            body.itemAliases.Add(CreatureDeathLootIds.ItemPrefix+"older",itemId);
            world.pendingGrants.Add(new PendingGrant {item=body.items[0].Copy()});
            world.pendingDeposits.Add(new PendingDeposit {item=body.items[0].Copy()});
            CreatureDeathLootIds.PrepareWorld(world);// Aliases and transfer references are not another physical stack.
            world.actors.Add(new ActorState {id="holder",inventory=new System.Collections.Generic.List<ItemStack> {body.items[0].Copy()}});
            bool failed=false;try {CreatureDeathLootIds.PrepareWorld(world);}catch(InvalidOperationException){failed=true;}
            Require(failed,"Две фактические стопки с одним reserved ID молча объединены в индексе.");
            world.actors.Clear();world.props.Add(new PropState {id=dropId});failed=false;
            try {CreatureDeathLootIds.PrepareWorld(world);}catch(InvalidOperationException){failed=true;}
            Require(failed,"Две фактические сущности с одним reserved ID молча объединены в индексе.");
            world.props.Clear();world.props.Add(new PropState {id=itemId});failed=false;
            try {CreatureDeathLootIds.PrepareWorld(world);}catch(InvalidOperationException){failed=true;}
            Require(failed,"Коллизия reserved ID между предметом и сущностью не диагностирована.");
        }
    }
}
