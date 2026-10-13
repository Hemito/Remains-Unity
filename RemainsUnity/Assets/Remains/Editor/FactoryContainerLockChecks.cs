using System;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContainerLockChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var actor=new ActorState {id="lock-check-actor",connected=true,level="factory:0",x=0,y=0};
            var container=new ContainerState {id="lock-check-box",level=actor.level,x=.5f,y=0,lockLevel=25,
                mineLevel=0,lootGenerationVersion="factory-loot-v1",lootCategory="weapon"};
            container.items.Add(new ItemStack {id="loot",definition="heal",count=2});
            actor.inventory.Add(new ItemStack {id="deposit",definition="heal",count=1});
            world.containers.Add(container);
            Require(ContainerAccess.Rejection(actor,container)?.Contains("25")==true,"Замок не блокирует доступ с указанием сложности.");
            Require(!new SharedInventory(world).PrepareClaim("lock-claim",container.id,"loot",actor,out _),"Выдача обошла замок.");
            Require(!new SharedDeposits(world).Prepare("lock-deposit",actor,container.id,"deposit",1,out _),"Внесение обошло замок.");
            Require(container.items[0].count==2 && actor.inventory[0].count==1,"Запрещённая операция изменила вещи.");
            var copy=StateJson.FromJson<ContainerState>(StateJson.ToJson(container));
            Require(copy.lockLevel==25 && copy.mineLevel==0 && copy.lootGenerationVersion==container.lootGenerationVersion && copy.lootCategory=="weapon",
                "Сохранение потеряло состояние замка или происхождение лута.");
            var rules=GameplayRuleSet.CreateDefault();
            var source=(InventorySource)container;
            Require(InteractionSelection.Select(world,actor,rules,source.Bounds.center).id==container.id,"Запертый контейнер не доступен для подсказки.");
            actor.x=5;
            Require(InteractionSelection.Select(world,actor,rules,source.Bounds.center).kind==InteractionKind.None,"Подсказка замка обошла дальность.");
            actor.x=0;container.lockLevel=0;container.mineLevel=3;
            Require(ContainerAccess.Rejection(actor,container)?.Contains("заминирован")==true,"Мина не блокирует доступ.");
            copy=StateJson.FromJson<ContainerState>(StateJson.ToJson(container));
            Require(copy.mineLevel==3,"Сохранение потеряло мину.");
            container.mineLevel=0;
            Require(ContainerAccess.Rejection(actor,container)==null,"Обычный контейнер заблокирован.");
            Debug.Log("FactoryContainerLockChecks: PASS — замки, мины, выдача, внесение, дальность и сохранение.");
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException(message);}
    }
}
