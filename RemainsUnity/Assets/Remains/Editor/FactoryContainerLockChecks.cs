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
            // Locks and mines do not block access while there is no mechanic (NearbyLootRule.SecurityEnforced, 2026-10-14).
            Require(!NearbyLootRule.SecurityEnforced && ContainerAccess.Rejection(actor,container)==null,"Запертый контейнер блокирует доступ, хотя механики взлома нет.");
            var copy=StateJson.FromJson<ContainerState>(StateJson.ToJson(container));
            Require(copy.lockLevel==25 && copy.mineLevel==0 && copy.lootGenerationVersion==container.lootGenerationVersion && copy.lootCategory=="weapon",
                "Сохранение потеряло состояние замка или происхождение лута.");
            var rules=GameplayRuleSet.CreateDefault();
            var source=(InventorySource)container;
            Require(InteractionSelection.Select(world,actor,rules,source.Bounds.center).id==container.id,"Запертый контейнер не доступен для подсказки.");
            actor.x=5;
            Require(InteractionSelection.Select(world,actor,rules,source.Bounds.center).kind==InteractionKind.None,"Подсказка замка обошла дальность.");
            actor.x=0;container.lockLevel=0;container.mineLevel=3;
            Require(ContainerAccess.Rejection(actor,container)==null,"Заминированный контейнер блокирует доступ, хотя механики обезвреживания нет.");
            copy=StateJson.FromJson<ContainerState>(StateJson.ToJson(container));
            Require(copy.mineLevel==3,"Сохранение потеряло мину.");
            container.mineLevel=0;
            Require(ContainerAccess.Rejection(actor,container)==null,"Обычный контейнер заблокирован.");
            Debug.Log("FactoryContainerLockChecks: PASS — замки и мины сохраняются и пока не блокируют доступ, дальность подсказки.");
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException(message);}
    }
}
