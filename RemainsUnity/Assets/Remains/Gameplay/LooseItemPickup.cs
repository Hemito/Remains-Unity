using System;
using Remains.Domain;
using Remains.Networking;

namespace Remains.Gameplay
{
    // Local request identity only; the shared inventory protocol reserves and awards the real stack.
    public sealed class LooseItemPickup : IDisposable
    {
        private readonly CoopSession session;
        private readonly PendingItemClaim claim=new();
        public string Transaction=>claim.Transaction;
        public string SourceId=>claim.SourceId;
        public string ItemId=>claim.ItemId;
        public bool Pending=>claim.Pending;
        public LooseItemPickup(CoopSession session)
        {this.session=session;session.ClaimPrepared+=claim.Prepared;}
        public bool Request(InteractionTarget target)
        {
            if(Pending) {session.ShowNotice("Подбор выбранной вещи ещё подтверждается…");return false;}
            var actor=session.LocalActor;var source=InventorySource.Resolve(session.World,target.id);
            if(target.kind!=InteractionKind.Pickup || source?.IsLooseItem!=true)
            {session.ShowNotice("Свободная вещь больше недоступна.");return false;}
            if(actor==null || session.Travel.Pending || session.Pause.IsPaused(actor.level)) return false;
            string reason=ContainerAccess.Rejection(actor,source,session.Rules.Loot);
            if(reason!=null) {session.ShowNotice(reason);return false;}
            // A dropped source carries one actual stack. Never substitute another item on a stale E.
            if(source.items.Count!=1)
            {
                bool reserved=session.World.pendingGrants.Exists(grant=>grant.containerId==source.id) ||
                    session.World.pendingDeposits.Exists(deposit=>deposit.containerId==source.id);
                if(reserved) session.ShowNotice("Вещь уже ожидает подтверждения общей операции.");
                else
                {
                    UnityEngine.Debug.LogWarning($"Нельзя подобрать свободную вещь: source={source.id}, actor={actor.id}, count={source.items.Count}; ожидалась одна стопка.");
                    session.ShowNotice("Данные свободной вещи повреждены. Подбор отменён.");
                }
                return false;
            }
            var item=source.items[0];reason=ContainerAccess.ClaimRejection(actor,source,item,session.Rules.Loot);
            if(reason!=null) {session.ShowNotice(reason);return false;}
            string transaction=session.RequestTrackedClaim(source.id,item.id,0);
            if(transaction==null || !claim.Capture(transaction,actor,source.id,item.id)) return false;
            session.ShowNotice("Подбор подтверждается…");return true;
        }
        public void Step()=>claim.Step(session.World,session.LocalActor,Mirror.NetworkClient.isConnected);
        public void Dispose() {session.ClaimPrepared-=claim.Prepared;claim.Clear();}
    }
}
