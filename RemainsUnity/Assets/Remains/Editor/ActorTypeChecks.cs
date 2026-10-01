using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;

namespace Remains.Editor
{
    public static class ActorTypeChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var player=new ActorState {id="player",player=true,connected=true,owner="client-a",level="hub"};
            var dummy=new ActorState {id="dummy",actorType="training-dummy",level="hub",health=500,x=2};
            world.actors.Add(player);world.actors.Add(dummy);
            var ownership=new OwnershipService();ownership.Reassign(world,new[]{"client-a"});
            Require(dummy.owner=="client-a" && dummy.target==null,"Мишень должна иметь владельца, но не боевую цель");
            var receiver=new DamageReceiver(new PrototypeDamageRule());int results=0;float total=0;
            receiver.Applied+=result=>{results++;total+=result.amount;};
            var shot=new AttackEvent {id="first",attacker=player.id,level="hub",weapon="pistol"};
            Require(receiver.Apply(dummy,shot,true) && dummy.health==500 && !dummy.down,"Мишень не восстановилась после попадания");
            Require(!receiver.Apply(dummy,shot,true) && results==1 && total==11,"Повторное попадание изменило результат мишени");
            shot.id="second";Require(receiver.Apply(dummy,shot,true) && results==2 && total==22,"Второе независимое попадание потеряно");
            var copy=dummy.Copy();Require(copy.actorType=="training-dummy" && copy.resolvedAttacks.Count==2,"Копия потеряла тип или журнал");
            Require(InventorySource.Resolve(world,InventorySource.BodyId(dummy.id))==null && InventorySource.All(world).All(s=>s.Body?.id!=dummy.id),"У мишени появился доступный инвентарь");
            var definition=ActorTypes.Get(dummy.actorType);
            Require(!definition.combatAi && !definition.hasWeapon && definition.presentation=="static","Неверные возможности мишени");
            bool rejected=false;try {ActorTypes.Get("missing-check");} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Неизвестный тип молча подменён другим");
            UnityEngine.Debug.Log("Типы актёров: PASS — учебная цель, владелец, восстановление, повтор, сохранение, отсутствие лута.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
