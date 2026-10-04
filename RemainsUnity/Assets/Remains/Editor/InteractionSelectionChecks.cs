using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class InteractionSelectionChecks
    {
        public static void Run()
        {
            Remains.Content.ActorTypes.Load();
            var world=new WorldState();var rules=GameplayRuleSet.CreateDefault();
            var actor=new ActorState {id="player",player=true,connected=true,x=0,y=0};world.actors.Add(actor);
            var container=new ContainerState {id="box",level=actor.level,x=1.5f,y=0};world.containers.Add(container);
            var door=new DoorState {id="door",definition="test",level=actor.level,x=1,y=0,width=.2f,height=2,surfaceTexture="floor"};
            world.doors.Add(door);
            Require(InteractionSelection.Select(world,actor,rules).id==door.id,"Ближайшая дверь не выбрана.");
            container.x=.5f;Require(InteractionSelection.Select(world,actor,rules).id==container.id,"Ближайший контейнер не выбран.");
            container.x=1;Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.Container,"Равная дистанция не сохраняет выбор контейнера.");
            actor.inventory.Add(new ItemStack {id="heal",definition="heal",count=1});
            var ally=new ActorState {id="ally",player=true,connected=true,down=true,x=1.8f,y=0};world.actors.Add(ally);
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.Revival,"Окно или дверь перехватывает поднятие союзника.");
            actor.inventory.Clear();Require(InteractionSelection.Select(world,actor,rules).kind!=InteractionKind.Revival,"Недоступное лечение блокирует E.");
            world.actors.Remove(ally);container.level="hub";door.level="hub";
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.None,"Выбрана цель из другой локации.");
            door.level=actor.level;actor.down=true;
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.None,"Тело может взаимодействовать.");
            Debug.Log("Выбор E: PASS — расстояние, контейнер/дверь, приоритет доступного поднятия, локация и состояние игрока.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
