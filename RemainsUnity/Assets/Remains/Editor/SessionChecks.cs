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
            Require(TravelRules.Rejection(bystander, "hub", "factory:0", 0, 0)!=null, "Нокдаун блокирует самостоятельный переход с причиной");

            var world = new WorldState();
            var npc = new ActorState { id="npc", owner="gone", attackBusy=true, attackRemaining=.4f };
            var player = new ActorState { id="p", owner="present", player=true, connected=true };
            world.actors.Add(npc); world.actors.Add(player);
            new OwnershipService().Reassign(world, new[]{"present"});
            Require(npc.owner=="present" && !npc.attackBusy && npc.attackRemaining==0, "Потеря владельца отменяет атаку до передачи");
            Debug.Log("Проверки сессии: PASS — порядок пакетов, независимые переходы, отмена атаки при отключении.");
        }
        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Проверка сессии: " + detail);
        }
    }
}
