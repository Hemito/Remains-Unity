using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class SessionChecks
    {
        public static void Run()
        {
            var gate = new PoseSequenceGate();
            var replicatedProp=new PropState {generation=2,sequence=7,held=true};
            Require(PropReplication.Accept(replicatedProp,2,9,1,2,3,4),"Новый пакет предмета принимается");
            Require(!PropReplication.Accept(replicatedProp,2,8,99,2,3,4) && replicatedProp.x==1,"Перестановка не откатывает предмет");
            Require(!PropReplication.Accept(replicatedProp,2,9,99,2,3,4),"Повтор пакета предмета отклоняется");
            replicatedProp.generation++;replicatedProp.held=false;
            Require(!PropReplication.Accept(replicatedProp,2,10,99,2,3,4) && !replicatedProp.held,"Старое поколение после отпускания отклоняется");
            Require(!PropReplication.Accept(replicatedProp,3,10,float.NaN,2,3,4),"Неконечная координата предмета отклоняется");
            Require(PropReplication.IsNewer(0,uint.MaxValue),"Счётчик предмета допускает переполнение");
            Require(gate.Accept("a", 1, 1, 12), "Первый пакет принимается");
            Require(!gate.Accept("a", 1, 1, 11), "Переставленный пакет отклоняется");
            Require(!gate.Accept("a", 1, 1, 12), "Повторный пакет отклоняется");
            Require(gate.Accept("b", 1, 1, 1), "Счётчик другого персонажа независим");
            Require(gate.Accept("a", 1, 2, 1), "Новая локация имеет отдельный счётчик");
            Require(gate.Accept("a", 2, 1, uint.MaxValue) && gate.Accept("a", 2, 1, 0), "Переполнение счётчика допустимо");

            var traveler = new ActorState { id="traveler", player=true, connected=true, level="hub", generation=3, lifecycleSequence=4, health=58 };
            var bystander = new ActorState { id="bystander", player=true, connected=true, level="hub", x=10 };
            Require(TravelRules.Rejection(traveler, "hub", "factory:0", 3, 4)==null, "Живой персонаж может уйти из хаба");
            var stale = traveler.Copy();
            TravelRules.Apply(traveler, "factory:0", RoomLayout.Load("factory:0"));
            Require(traveler.level=="factory:0" && traveler.lifecycleSequence==5 && traveler.health==58, "Переход меняет локацию и эпоху, сохраняя здоровье");
            Require(bystander.level=="hub" && bystander.x==10, "Второй персонаж остаётся в хабе");
            Require(!ActorVersion.CanPublishOwnerState(stale,traveler), "Старое состояние не отменяет переход");
            Require(TravelRules.Rejection(traveler, "hub", "factory:0", 3, 4)!=null, "Повтор старого перехода не применяется");
            bystander.down=true;
            var explosion = new AttackEvent {id="blast",attacker=traveler.id,level="hub",weapon="grenade"};
            Require(AttackScope.Includes(explosion,bystander) && !AttackScope.Includes(explosion,traveler),
                "Взрыв остаётся в хабе после перехода бросившего на завод");
            var receiver = new DamageReceiver(new PrototypeDamageRule());
            Require(!receiver.Apply(traveler,explosion,true) && traveler.health==58,
                "Чужой уровень не получает урон даже при заявленном контакте");
            Require(TravelRules.Rejection(bystander, "hub", "factory:0", 0, 0)!=null, "Нокдаун блокирует самостоятельный переход с причиной");
            Require(TravelRules.Rejection(traveler,"factory:0","hub",3,5)==null,"Обратный маршрут доступен живому персонажу");
            var beforeReturn=traveler.Copy();
            var hub=RoomLayout.Load("hub");
            TravelRules.Apply(traveler,"hub",hub);
            Require(traveler.level=="hub" && traveler.x==hub.spawnX && traveler.y==hub.spawnY && traveler.health==58 && traveler.lifecycleSequence==6,
                "Возвращение использует точку появления хаба и сохраняет здоровье");
            Require(!ActorVersion.CanPublishOwnerState(beforeReturn,traveler),"Старый пакет завода не отменяет возвращение");
            Require(TravelRules.Rejection(traveler,"hub","hub",3,6)!=null && TravelRules.Rejection(traveler,"hub","factory:1",3,6)!=null,
                "Переход в текущую или неподдерживаемую локацию отклоняется");

            var world = new WorldState();
            var npc = new ActorState { id="npc", owner="gone", attackBusy=true, attackRemaining=.4f };
            var player = new ActorState { id="p", owner="present", player=true, connected=true };
            world.actors.Add(npc); world.actors.Add(player);
            new OwnershipService().Reassign(world, new[]{"present"});
            Require(npc.owner=="present" && !npc.attackBusy && npc.attackRemaining==0, "Потеря владельца отменяет атаку до передачи");
            npc.attackBusy=true;npc.attackId="old-session";npc.attackRemaining=.4f;
            var heldProp=new PropState {id="held",owner="present",held=true,x=4};world.props.Add(heldProp);
            new OwnershipService().BeginSession(world);
            Require(heldProp.owner==null && !heldProp.held && heldProp.x==4,"Перезапуск освобождает предмет без потери положения");
            player.connected=true;player.owner="present";
            new OwnershipService().Reassign(world,new[]{"present"});
            Require(npc.owner=="present" && !npc.attackBusy && npc.attackId==null && npc.attackCooldown==NpcAttackRules.CooldownSeconds,
                "Повтор номера соединения после перезапуска не продолжает старую атаку");
            Debug.Log("Проверки сессии: PASS — порядок пакетов, независимые переходы, отмена атаки при отключении.");
        }
        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Проверка сессии: " + detail);
        }
    }
}
