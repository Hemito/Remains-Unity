using System;
using Remains.Domain;
using Remains.Runtime;
using UnityEngine;

namespace Remains.Editor
{
    public static class TelekinesisProbeChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var target=new PropState {id="prop:crate-1"};
            var container=new ContainerState();
            for(int i=0;i<300;i++)
            {
                world.props.Add(new PropState {id="other-prop:"+i});
                world.containers.Add(new ContainerState {id="other-container:"+i});
            }
            world.props.Insert(150,target);world.containers.Insert(150,container);
            Require(ReferenceEquals(NetworkTelekinesisProbe.FindTarget(world,false),target),"Выбран чужой предмет среди тел завода.");
            Require(ReferenceEquals(NetworkTelekinesisProbe.FindTarget(world,true),container),"Выбран чужой контейнер.");
            world.props.Reverse();world.containers.Reverse();
            Require(ReferenceEquals(NetworkTelekinesisProbe.FindTarget(world,false),target) &&
                ReferenceEquals(NetworkTelekinesisProbe.FindTarget(world,true),container),"Выбор зависит от порядка тел.");
            world.props.Remove(target);world.containers.Remove(container);
            Require(NetworkTelekinesisProbe.FindTarget(world,false)==null && NetworkTelekinesisProbe.FindTarget(world,true)==null,
                "Отсутствующая fixture молча заменена чужим объектом.");
            Require(NetworkTelekinesisProbe.FindTarget(null,false)==null,"Нет ожидания мира до подключения.");
            world.props.Add(target);world.props.Add(new PropState {id=target.id});
            bool duplicateRejected=false;
            try {NetworkTelekinesisProbe.FindTarget(world,false);} catch(InvalidOperationException) {duplicateRejected=true;}
            Require(duplicateRejected,"Неоднозначный ID тестового предмета принят.");
            Debug.Log("Проверка цели телекинеза: PASS — множество тел, явные ID, перестановка, отсутствие и дубликат.");
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Проверка цели телекинеза: "+message);}
    }
}
