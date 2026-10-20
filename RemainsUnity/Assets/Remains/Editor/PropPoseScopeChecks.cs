using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropPoseScopeChecks
    {
        public static void Run()
        {
            CheckDormantOwnership();
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
        private static void CheckDormantOwnership()
        {
            var world=new WorldState();
            var a=new ActorState {id="dormant-a",owner="connection:a",player=true,connected=true,level="factory:0"};
            var b=new ActorState {id="dormant-b",owner="connection:b",player=true,connected=true,level="hub"};
            world.actors.AddRange(new[]{a,b});
            var service=new OwnershipService();var owners=new[]{a.owner,b.owner};
            for(int i=0;i<12;i++)
            {
                world.props.Add(new PropState {id="dormant-prop-"+i,owner=a.owner,level=a.level,generation=5,sequence=8});
                world.containers.Add(new ContainerState {id="dormant-container-"+i,owner=a.owner,level=a.level,generation=5,sequence=8});
            }
            var before=new System.Collections.Generic.Dictionary<string,string>();
            foreach(var prop in WorldProps.All(world)) before.Add(prop.id,Remains.Serialization.StateJson.ToJson(prop));
            for(int cycle=0;cycle<3;cycle++)
            {
                a.level="hub";WorldChanges.Clear(world);
                Require(!service.ReassignAndReportChanges(world,owners),"Уход последнего игрока массово переписывает свободные тела");
                foreach(var prop in WorldProps.All(world))
                {
                    Require(!PropPoseScope.CanPublish(world,a,prop,false),"Спящий владелец публикует движение пустого уровня");
                    Require(!WorldChanges.IsMarked(world,prop) && Remains.Serialization.StateJson.ToJson(prop)==before[prop.id],
                        "Сон изменил позу, поколение или dirty-метку: "+prop.id);
                }
                a.level="factory:0";
                Require(!service.ReassignAndReportChanges(world,owners),"Возвращение прежнего клиента массово переписывает тела");
                foreach(var prop in WorldProps.All(world))
                    Require(PropPoseScope.CanPublish(world,a,prop,false) && !WorldChanges.IsMarked(world,prop) &&
                        Remains.Serialization.StateJson.ToJson(prop)==before[prop.id],"Пробуждение изменило неизменное тело: "+prop.id);
            }
            var cargo=world.containers[0];
            Require(!PropReplication.Accept(cargo,5,8,9,9,0,0) && !PropReplication.Accept(cargo,4,9,9,9,0,0),
                "Спящий lifecycle принимает старые sequence/generation");
            cargo.held=true;a.level="hub";WorldChanges.Clear(world);
            Require(service.ReassignAndReportChanges(world,owners) && cargo.owner==a.owner && !cargo.held && cargo.generation==6 &&
                WorldChanges.IsMarked(world,cargo),"Уход держателя не отпустил груз один раз с сохранением dormant-владельца");
            Require(!service.ReassignAndReportChanges(world,owners) && cargo.generation==6,"Повтор сна снова меняет поколение отпущенного груза");
            b.level="factory:0";WorldChanges.Clear(world);
            Require(service.ReassignAndReportChanges(world,owners),"Другой клиент не получил активный уровень");
            foreach(var prop in WorldProps.All(world))
                Require(prop.owner==b.owner && prop.generation==(ReferenceEquals(prop,cargo)?7:6) && WorldChanges.IsMarked(world,prop),
                    "Реальная передача не обновила владельца/поколение/dirty: "+prop.id);
            Require(!PropReplication.Accept(cargo,6,9,9,9,0,0),"Прежнее поколение проходит после реальной передачи");
            cargo.held=true;service.Reassign(world,owners);
            Require(cargo.held && cargo.generation==7,"Присутствующий сознательный держатель теряет груз");
            b.level="hub";service.Reassign(world,owners);
            Require(cargo.owner==b.owner && !cargo.held && cargo.generation==8,"Уход нового держателя не освобождает груз");
            b.connected=false;WorldChanges.Clear(world);
            Require(service.ReassignAndReportChanges(world,new[]{a.owner}),"Отключение dormant-владельца не обработано");
            foreach(var prop in WorldProps.All(world))
                Require(prop.owner==null && !prop.held && WorldChanges.IsMarked(world,prop),"Отключённый клиент сохранил dormant-владение: "+prop.id);
            a.level="factory:0";service.Reassign(world,new[]{a.owner});cargo.held=true;
            int generation=cargo.generation;service.BeginSession(world);
            Require(cargo.owner==null && !cargo.held && cargo.generation==generation+1,
                "Новая сессия унаследовала dormant connection ID или удержание");
        }
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Поза объектов: "+detail);}
    }
}
