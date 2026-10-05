using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropPoseScopeChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var owner=new ActorState {id="owner",owner="connection:0",player=true,connected=true,level="hub"};
            var body=new ActorState {id="body",owner="connection:1",player=true,connected=true,down=true,level="factory:0"};
            world.actors.AddRange(new[]{owner,body});
            var cargo=new ContainerState {id="cargo",owner=owner.owner,level="factory:0",x=2,y=4};
            world.containers.Add(cargo);
            Require(PropPoseScope.CanPublish(world,owner,cargo,false),"Свободный груз своего владельца доступен в другом активном уровне");
            Require(!PropPoseScope.CanPublish(world,body,cargo,false),"Другой клиент не публикует чужой груз");
            cargo.held=true;
            Require(!PropPoseScope.CanPublish(world,owner,cargo,false),"Удержание не разрешается из другой локации");
            cargo.held=false;
            Require(!PropPoseScope.CanPublish(world,owner,cargo,true),"Остановленный уровень не принимает даже позднюю позу");
            body.connected=false;
            Require(!PropPoseScope.CanPublish(world,owner,cargo,false),"Пустая локация не принимает позднее движение");
            body.connected=true;
            var ownership=new OwnershipService();
            ownership.Reassign(world,new[]{owner.owner,body.owner});
            Require(cargo.owner==body.owner && cargo.generation==1 && PropPoseScope.CanPublish(world,body,cargo,false),
                "Подключённое бессознательное тело получает симуляцию через настоящий client ID");
            Require(!PropReplication.Accept(cargo,0,1,2,3,0,-1),"Старый владелец не изменяет груз после передачи");
            cargo.held=true;
            ownership.Reassign(world,new[]{owner.owner,body.owner});
            Require(!cargo.held && cargo.owner==body.owner && cargo.generation==2,"Потеря управления снимает удержание, сохраняя симулятор");
            ownership.Reassign(world,new[]{owner.owner,body.owner});
            Require(cargo.generation==2,"Повтор reconcile не создаёт новый lifecycle");
            body.down=false;
            cargo.held=true;
            Require(PropPoseScope.CanPublish(world,body,cargo,false),"Живой держатель публикует в своём уровне");
            ownership.Reassign(world,new[]{owner.owner,body.owner});
            Require(cargo.held && cargo.generation==2,"Активный держатель сохраняет захват");
            body.connected=false;
            ownership.Reassign(world,new[]{owner.owner});
            Require(cargo.owner==null && !cargo.held && cargo.generation==3,"Пустой уровень освобождает симулятор, не назначает несуществующего host");
            body.connected=true;body.down=true;
            ownership.Reassign(world,new[]{owner.owner,body.owner});
            Require(cargo.owner==body.owner && cargo.generation==4,"Уровень возобновляется с единственным подключённым телом");
            owner.connected=false;
            Require(!PropPoseScope.CanPublish(world,owner,cargo,false),"Отключённый отправитель не имеет полномочий");
            Debug.Log("Полномочия позы объектов: разные уровни, пауза, пустой уровень, подключённое тело, удержание и передача проверены.");
        }
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Поза объектов: "+detail);}
    }
}
