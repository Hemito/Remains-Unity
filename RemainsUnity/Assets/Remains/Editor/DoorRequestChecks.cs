using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorRequestChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var actor=new ActorState {id="requester",player=true,connected=true,x=0,y=0};
            var door=new DoorState {id="door",definition="test",level=actor.level,x=1,y=0,width=.4f,height=3,surfaceTexture="floor"};
            world.actors.Add(actor);world.doors.Add(door);
            var access=new NearbyDoorAccess();
            DoorChange Apply(bool open,long revision,bool paused=false,string worldId=null,int? generation=null,int? lifecycle=null)
                =>DoorRequestRules.Apply(world,actor,worldId??world.worldId,door.id,generation??actor.generation,
                    lifecycle??actor.lifecycleSequence,revision,open,paused,access,out _);
            Require(Apply(true,0,worldId:"other")==DoorChange.Rejected,"Запрос чужого мира принят.");
            Require(Apply(true,0,generation:actor.generation+1)==DoorChange.Rejected,"Старое поколение принято.");
            Require(Apply(true,0,lifecycle:actor.lifecycleSequence+1)==DoorChange.Rejected,"Старая жизнь принята.");
            Require(Apply(true,0,paused:true)==DoorChange.Rejected && door.revision==0,"Пауза изменила дверь.");
            Require(Apply(true,0)==DoorChange.Changed,"Открытие не принято.");
            Require(Apply(true,0)==DoorChange.Rejected && door.revision==1,"Повтор изменил дверь.");
            var body=new ActorState {id="body",level=actor.level,x=1.2f,y=0,down=true,connected=false};
            world.actors.Add(body);
            Require(Apply(false,1)==DoorChange.Rejected && door.open,"Дверь закрылась на отключённом теле.");
            body.level="hub";
            var prop=new PropState {id="held",level=actor.level,x=1.2f,y=.3f,held=true};world.props.Add(prop);
            Require(Apply(false,1)==DoorChange.Rejected,"Дверь закрылась на удерживаемом объекте.");
            prop.x=10;
            var container=new ContainerState {id="container",level=actor.level,x=1.2f,y=0};world.containers.Add(container);
            Require(Apply(false,1)==DoorChange.Rejected,"Дверь закрылась на контейнере.");
            container.x=10;
            Require(Apply(false,1)==DoorChange.Changed && !door.open && door.revision==2,"Свободная дверь не закрылась.");
            Require(Apply(true,0)==DoorChange.Rejected && !door.open,"Позднее открытие отменило закрытие.");
            Require(Apply(false,2)==DoorChange.Unchanged && door.revision==2,"Текущее состояние увеличило версию.");
            Require(DoorRequestRules.Apply(world,null,world.worldId,door.id,0,0,2,true,false,access,out _) == DoorChange.Rejected,
                "Неизвестный участник изменил дверь.");
            Debug.Log("Запросы дверей: PASS — мир, поколение, жизнь, пауза, повтор, тело, удерживаемый предмет и контейнер.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
