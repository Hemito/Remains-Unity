using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct DepositRequest : NetworkMessage
    {public string transaction,container,item,level;public int quantity,generation,lifecycle;public bool toss;public long selectionRevision;public float x,y,vx,vy,angle;public bool mirrored;}
    public struct DepositReply : NetworkMessage {public string transaction,reason;public bool accepted;}
    public sealed class SessionDeposits
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,DepositRequest> requests=new();
        private readonly Dictionary<string,DepositReply> replies=new();
        private float nextRetry;
        public bool Pending=>requests.Count>0;
        public event Action Reconciled;
        public SessionDeposits(CoopSession session) {this.session=session;session.Changed+=Reconcile;}
        public void RegisterServer() {replies.Clear();NetworkServer.RegisterHandler<DepositRequest>(Remains.Networking.MessageTiming.Server<DepositRequest>(Receive));}
        public void RegisterClient()
        {
            requests.Clear();
            NetworkClient.RegisterHandler<DepositReply>(Remains.Networking.MessageTiming.Client<DepositReply>(ReceiveReply));
        }
        private void ReceiveReply(DepositReply reply)
        {
            if(!requests.ContainsKey(reply.transaction)) return;
            if(!reply.accepted) {requests.Remove(reply.transaction);Reconcile(true);}
            session.ShowNotice(reply.reason);
        }
        public bool Request(string containerId,string itemId,int quantity=0)
            =>Send(new DepositRequest {container=containerId,item=itemId,quantity=quantity});
        public bool Toss(string itemId,Vector2 aim,int quantity=1)
        {
            var actor=session.LocalActor;if(actor==null) return false;
            var selected=HeldItemSelection.Selected(actor);
            if(selected==null || selected.id!=InventoryStacks.Find(actor,itemId)?.id || !actor.heldItemPoseValid)
            {session.ShowNotice("Сначала возьмите предмет и укажите курсором направление броска.");return false;}
            var launch=Remains.Gameplay.HeldItemPose.Capture(actor,aim);
            var origin=launch.origin;var velocity=launch.velocity;
            return Send(new DepositRequest {toss=true,item=itemId,quantity=quantity,x=origin.x,y=origin.y,vx=velocity.x,vy=velocity.y,angle=launch.angle,mirrored=launch.mirrored});
        }
        private bool Send(DepositRequest request)
        {
            var actor=session.LocalActor;var item=InventoryStacks.Find(actor,request.item);
            if(actor==null || item==null || !NetworkClient.isConnected) return false;
            if(session.Pause.IsPaused(actor.level)) {session.ShowNotice(InventoryTransactions.PausedReason);return false;}
            if(request.toss && !DroppedItemTransfer.HasPreparedShape(item.definition))
            {
                Debug.LogWarning($"Бросок не отправлен: actor={actor.id}, item={item.id}, definition={item.definition}; нет подготовленной формы изображения.");
                session.ShowNotice("Бросок недоступен: у предмета не подготовлена физическая форма. Подробности — в журнале.");return false;
            }
            request.transaction=Guid.NewGuid().ToString("N");
            InventorySource container=request.toss?DroppedItemTransfer.Preview(actor,request.transaction,item.definition,new Vector2(request.x,request.y),new Vector2(request.vx,request.vy),request.angle,request.mirrored):
                InventorySource.Resolve(session.World,request.container);
            if(container==null) return false;
            int amount=request.quantity==0?InventoryAvailability.Count(actor,item):request.quantity;
            string reason=amount<=0 || amount>InventoryAvailability.Count(actor,item)?"Это количество уже зарезервировано или недоступно.":
                session.Rules.Deposit.AccessRejection(new LootAccessContext(actor,container))??session.Rules.Deposit.Rejection(new DepositContext(actor,container,item,amount,
                    request.toss?InventoryTransactions.TossPurpose:InventoryTransactions.DepositPurpose));
            if(reason!=null) {session.ShowNotice(reason);return false;}
            request.item=item.id;request.quantity=amount;
            request.generation=actor.generation;request.lifecycle=actor.lifecycleSequence;request.level=actor.level;request.selectionRevision=actor.weaponSelectionRevision;
            // Publish prior client debits before the reservation on the same reliable channel.
            session.PublishActor(actor);requests.Add(request.transaction,request);Reconcile();
            NetworkClient.Send(request);nextRetry=Time.unscaledTime+2;
            session.ShowNotice(request.toss?"Бросаем предмет… Количество зарезервировано.":
                "Внесение ожидает сохранения копий мира. Количество зарезервировано.");return true;
        }
        public void Step()
        {
            if(!NetworkClient.isConnected || requests.Count==0 || Time.unscaledTime<nextRetry) return;
            nextRetry=Time.unscaledTime+2;
            foreach(var request in requests.Values) NetworkClient.Send(request);
        }
        private void Reconcile()=>Reconcile(false);
        private void Reconcile(bool forceNotification)
        {
            var actor=session.LocalActor;
            if(actor==null) {requests.Clear();Reconciled?.Invoke();return;}
            // World subscribers already receive Changed; preserve explicit rejection notifications.
            if(!forceNotification && requests.Count==0 && actor.inventoryReservations.Count==0) return;
            actor.inventoryReservations.Clear();
            foreach(var pair in requests.ToArray())
            {
                if(session.World.completedTransactions.Contains(pair.Key))
                {requests.Remove(pair.Key);session.ShowNotice(pair.Value.toss?"Предмет брошен.":"Внесение в контейнер сохранено.");continue;}
                // Once the canonical debit arrived, the remainder is safe to spend.
                if(actor.inventoryUses.Any(use=>use.id==pair.Key)) continue;
                string item=InventoryStacks.ResolveId(actor.inventoryAliases,pair.Value.item);
                actor.inventoryReservations.TryGetValue(item,out int existing);
                actor.inventoryReservations[item]=existing+pair.Value.quantity;
            }
            Reconciled?.Invoke();
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
            {
                bool paused=session.Pause.IsServerPaused(actor.level);
                if(request.toss && actor.level==request.level && request.selectionRevision==actor.weaponSelectionRevision &&
                    HeldItemSelection.Selected(actor)?.id==request.item)
                    accepted=DroppedItemTransfer.Prepare(session.ServerWorld,actor,request.transaction,request.item,request.quantity,
                        new Vector2(request.x,request.y),new Vector2(request.vx,request.vy),session.Rules.Deposit,out reason,paused,request.angle,request.mirrored);
                else if(!request.toss)
                    accepted=new SharedDeposits(session.ServerWorld,session.Rules.Deposit).Prepare(request.transaction,actor,request.container,request.item,request.quantity,out reason,paused);
            }
            var reply=new DepositReply {transaction=request.transaction,accepted=accepted,reason=accepted?
                request.toss?"Бросаем предмет…":"Внесение подтверждается копиями мира…":reason};
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
