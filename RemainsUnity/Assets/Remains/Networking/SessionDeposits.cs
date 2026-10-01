using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct DepositRequest : NetworkMessage {public string transaction,container,item;public int quantity,generation,lifecycle;}
    public struct DepositReply : NetworkMessage {public string transaction,reason;public bool accepted;}
    public sealed class SessionDeposits
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,DepositRequest> requests=new();
        private readonly Dictionary<string,DepositReply> replies=new();
        private float nextRetry;
        public bool Pending=>requests.Count>0;
        public SessionDeposits(CoopSession session) {this.session=session;session.Changed+=Reconcile;}
        public void RegisterServer() {replies.Clear();NetworkServer.RegisterHandler<DepositRequest>(Receive);}
        public void RegisterClient()
        {
            requests.Clear();
            NetworkClient.RegisterHandler<DepositReply>(reply=>
            {
                if(!requests.ContainsKey(reply.transaction)) return;
                if(!reply.accepted) {requests.Remove(reply.transaction);Reconcile();}
                session.ShowNotice(reply.reason);
            });
        }
        public bool Request(string containerId,string itemId,int quantity=0)
        {
            var actor=session.LocalActor;var item=actor?.inventory.Find(i=>i.id==itemId);
            var container=session.World?.containers.Find(c=>c.id==containerId);
            if(actor==null || item==null || container==null || !NetworkClient.isConnected) return false;
            int amount=quantity==0?InventoryAvailability.Count(actor,item):quantity;
            string reason=amount<=0 || amount>InventoryAvailability.Count(actor,item)?"Это количество уже зарезервировано или недоступно.":
                session.Rules.Deposit.AccessRejection(new LootAccessContext(actor,container))??session.Rules.Deposit.Rejection(new DepositContext(actor,container,item,amount));
            if(reason!=null) {session.ShowNotice(reason);return false;}
            var request=new DepositRequest {transaction=Guid.NewGuid().ToString("N"),container=containerId,item=itemId,quantity=amount,generation=actor.generation,lifecycle=actor.lifecycleSequence};
            // Publish prior client debits before the reservation on the same reliable channel.
            session.PublishActor(actor);requests.Add(request.transaction,request);Reconcile();
            NetworkClient.Send(request);nextRetry=Time.unscaledTime+2;
            session.ShowNotice("Внесение ожидает сохранения копий мира. Количество зарезервировано.");return true;
        }
        public void Step()
        {
            if(!NetworkClient.isConnected || requests.Count==0 || Time.unscaledTime<nextRetry) return;
            nextRetry=Time.unscaledTime+2;
            foreach(var request in requests.Values) NetworkClient.Send(request);
        }
        private void Reconcile()
        {
            var actor=session.LocalActor;
            if(actor==null) {requests.Clear();return;}
            actor.inventoryReservations.Clear();
            foreach(var pair in requests.ToArray())
            {
                if(session.World.completedTransactions.Contains(pair.Key))
                {requests.Remove(pair.Key);session.ShowNotice("Внесение в контейнер сохранено.");continue;}
                // Once the canonical debit arrived, the remainder is safe to spend.
                if(actor.inventoryUses.Any(use=>use.id==pair.Key)) continue;
                actor.inventoryReservations.TryGetValue(pair.Value.item,out int existing);
                actor.inventoryReservations[pair.Value.item]=existing+pair.Value.quantity;
            }
        }
        private void Receive(NetworkConnectionToClient connection,DepositRequest request)
        {
            var actor=session.ConnectedActor(connection);if(actor==null) return;
            string key=actor.id+":"+request.transaction;
            if(replies.TryGetValue(key,out var previous)) {connection.Send(previous);return;}
            string reason="Состояние персонажа изменилось. Внесение отменено.";
            bool accepted=false;
            var pending=session.ServerWorld.pendingDeposits.Find(p=>p.transaction==request.transaction);
            if(pending!=null && pending.source==actor.id) accepted=true;
            else if(session.ServerWorld.completedTransactions.Contains(request.transaction))
                reason="Операция уже завершена; проверьте содержимое контейнера.";
            else if(actor.generation==request.generation && actor.lifecycleSequence==request.lifecycle)
                accepted=new SharedDeposits(session.ServerWorld,session.Rules.Deposit).Prepare(request.transaction,actor,request.container,request.item,request.quantity,out reason);
            var reply=new DepositReply {transaction=request.transaction,accepted=accepted,reason=accepted?"Внесение подтверждается копиями мира…":reason};
            replies[key]=reply;
            if(accepted) {session.CommitWorld();session.CompleteInventoryTransactions();}
            connection.Send(reply);
        }
        internal bool CompleteBackedUp()
        {
            bool changed=false;var deposits=new SharedDeposits(session.ServerWorld,session.Rules.Deposit);
            foreach(var pending in session.ServerWorld.pendingDeposits.ToArray())
                if(session.HasInventoryBackups(pending.preparedRevision)) changed|=deposits.Commit(pending.transaction);
            return changed;
        }
    }
}
