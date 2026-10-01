using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class LevelPauseChecks
    {
        private sealed class NeverPause : ILevelPauseRule {public bool IsPaused(LevelPauseContext context)=>false;}
        public static void Run()
        {
            var world=new WorldState();
            var first=new ActorState {id="first",player=true,connected=true,level="factory:0"};
            var second=new ActorState {id="second",player=true,connected=true,level="factory:0"};
            var hub=new ActorState {id="hub-player",player=true,connected=true,level="hub"};
            world.actors.Add(first);world.actors.Add(second);world.actors.Add(hub);
            var pause=new LevelPauseCoordinator(new CooperativePauseRule());
            Require(pause.SetIntent(first,0,0,true,true)==null && !pause.IsPaused(world,first.level),"Меню/инвентарь по умолчанию не останавливают уровень");
            pause.PauseOnInventory=true;
            Require(pause.IsPaused(world,first.level) && !pause.IsPaused(world,hub.level),"Инвентарь останавливает только свой уровень");
            pause.SetIntent(first,0,0,true,false);
            Require(!pause.IsPaused(world,first.level),"Настройка инвентаря независима от меню");
            pause.PauseOnMenu=true;
            Require(pause.IsPaused(world,first.level),"Меню имеет отдельную настройку");
            pause.SetIntent(second,0,0,true,false);pause.SetIntent(first,0,0,false,false);
            Require(pause.IsPaused(world,first.level),"Закрытие одного меню не удаляет чужую причину");
            second.connected=false;pause.Reconcile(world);
            Require(!pause.IsPaused(world,first.level),"Отключение удаляет причину меню");
            second.connected=true;
            pause.SetManual(first,0,0,true);
            Require(TravelRules.Rejection(first,first.level,"hub",0,0,pause.IsPaused(world,"hub"))==null,"Выход из остановленного завода в хаб разрешён");
            first.level="hub";first.lifecycleSequence++;pause.Reconcile(world);
            Require(pause.IsPaused(world,"factory:0") && !pause.IsPaused(world,"hub"),"Ручная пауза остаётся у оставшегося игрока, не переносится в хаб");
            Require(TravelRules.Rejection(first,"hub","factory:0",0,1,pause.IsPaused(world,"factory:0"))!=null,"Вход в остановленный завод запрещён");
            Require(pause.SetIntent(first,0,0,true,true)!=null && pause.SetManual(first,0,0,true)!=null && !pause.IsPaused(world,"hub"),"Поздние запросы старого перехода не останавливают новое место");
            pause.SetManual(second,0,0,false);
            Require(!pause.IsPaused(world,"factory:0"),"Оставшийся игрок может снять ручную паузу");
            pause.SetIntent(first,0,1,true,true);first.generation++;pause.Reconcile(world);
            Require(!pause.IsPaused(world,"hub"),"Смена поколения удаляет старое намерение");
            pause.SetManual(second,0,0,true);second.connected=false;pause.Reconcile(world);second.connected=true;
            Require(!pause.IsPaused(world,"factory:0"),"Пустой уровень не сохраняет ручную паузу для следующего визита");
            pause.SetManual(first,1,1,true);pause.Reset();
            Require(!pause.IsPaused(world,"hub") && !pause.PauseOnInventory && !pause.PauseOnMenu,"Новая сессия очищает причины и настройки");
            var custom=new LevelPauseCoordinator(new NeverPause());custom.SetManual(first,1,1,true);
            Require(!custom.IsPaused(world,"hub"),"Правило паузы заменяется через композицию");
            Debug.Log("LevelPauseChecks PASS: причины, независимые настройки, переходы, поколения, отключение и замена правила.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("LevelPauseChecks: "+reason);}
    }
}
