using System;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Editor
{
    public static class BodyInventoryChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var body=new ActorState {id="body-owner",player=true,connected=false,down=true};
            var recipient=new ActorState {id="recipient",player=true,connected=true};
            world.actors.Add(body);world.actors.Add(recipient);
            var item=SharedInventory.Create("heal",5);body.inventory.Add(item);
            var oldOwner=body.Copy();string id=InventorySource.BodyId(body.id);
            var source=InventorySource.Resolve(world,id);var transfers=new SharedInventory(world);
            Require(ReferenceEquals(source.items,body.inventory) && world.containers.Count==0,"Тело использует единственный инвентарь без контейнерной копии");
            Require(ContainerAccess.Nearest(world,recipient)?.id==id,"Отключённое тело обнаруживается рядом");
            Require(transfers.PrepareClaim("claim",id,item.id,recipient,out _,2) && body.inventory.Single().count==3 && recipient.inventory.Count==0,"Часть инвентаря тела резервируется до выдачи");
            Require(InventoryJournal.Merge(body,oldOwner,out var merged,out _) && merged.Single().count==3,"Старый владелец не возвращает забранную часть");
            body.connected=true;body.down=false;body.lifecycleSequence++;
            Require(!transfers.PrepareClaim("awake",id,item.id,recipient) && ContainerAccess.Nearest(world,recipient)==null,"Поднятый персонаж больше не доступен для новых запросов");
            var restored=StateJson.Copy(world);var resumed=new SharedInventory(restored);
            resumed.CommitGrant(restored.pendingGrants.Single());
            Require(restored.Find(recipient.id).inventory.Single().count==2 && restored.Find(body.id).inventory.Single().count==3 && restored.containers.Count==0,"Резерв завершается после поднятия и перезапуска без копии тела");
            body.down=true;
            Require(transfers.PrepareClaim("return",id,item.id,recipient,out _,1),"Второй независимый резерв тела");
            var pending=world.pendingGrants.Single(p=>p.transaction=="return");world.actors.Remove(recipient);
            transfers.CommitGrant(pending);transfers.CommitGrant(pending);
            Require(InventoryJournal.Count(body,"heal")==3,"Исчезнувший получатель возвращает часть в тело один раз");
            body.connected=false;body.level="hub";
            Require(!ActorPresence.IsPresent(body) && ContainerAccess.Rejection(new ActorState {connected=true,level="hub"},source)!=null,"Отсутствующая запись хаба не становится лут-контейнером");
            body.level="factory:0";
            var donor=new ActorState {id="donor",player=true,connected=true};world.actors.Add(donor);
            var gift=SharedInventory.Create("grenade",2);donor.inventory.Add(gift);
            var deposits=new SharedDeposits(world);
            Require(deposits.Prepare("gift",donor,id,gift.id,1,out _),"В тело можно внести предмет");
            body.connected=true;body.down=false;
            Require(deposits.Commit("gift") && InventoryJournal.Count(body,"grenade")==1 && InventoryJournal.Count(donor,"grenade")==1,"Поднятие после резерва не теряет внесение в тот же инвентарь");
            UnityEngine.Debug.Log("BodyInventoryChecks PASS: единый инвентарь, резерв, поднятие, старый пакет, возврат и внесение.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("BodyInventoryChecks: "+reason);}
    }
}
