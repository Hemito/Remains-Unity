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
        private string requestedDefinition;
        public event Action<string,string> Confirmed;
        public string Transaction=>claim.Transaction;
        public string SourceId=>claim.SourceId;
        public string ItemId=>claim.ItemId;
        public bool Pending=>claim.Pending;
        public LooseItemPickup(CoopSession session)
        {this.session=session;session.ClaimPrepared+=claim.Prepared;}
        public bool Request(InteractionTarget target)
        {
            if(Pending) {session.ShowNotice(Remains.Content.GameLocalization.UiText("source.LooseItemPickup.3119c3f574ed20d8", "Подбор выбранной вещи ещё подтверждается…", "RemainsUnity/Assets/Remains/Gameplay/LooseItemPickup.cs"));return false;}
            var actor=session.LocalActor;var source=InventorySource.Resolve(session.World,target.id);
            if(target.kind!=InteractionKind.Pickup || source?.IsLooseItem!=true)
            {session.ShowNotice(Remains.Content.GameLocalization.UiText("source.LooseItemPickup.cc8d4e72c3ff8878", "Свободная вещь больше недоступна.", "RemainsUnity/Assets/Remains/Gameplay/LooseItemPickup.cs"));return false;}
            if(actor==null || session.Travel.Pending || session.Pause.IsPaused(actor.level)) return false;
            string reason=ContainerAccess.Rejection(actor,source,session.Rules.Loot);
            if(reason!=null) {session.ShowNotice(reason);return false;}
            // A dropped source carries one actual stack. Never substitute another item on a stale E.
            if(source.items.Count!=1)
            {
                bool reserved=session.World.pendingGrants.Exists(grant=>grant.containerId==source.id) ||
                    session.World.pendingDeposits.Exists(deposit=>deposit.containerId==source.id);
                if(reserved) session.ShowNotice(Remains.Content.GameLocalization.UiText("source.LooseItemPickup.c903b60f420be2bf", "Вещь уже ожидает подтверждения общей операции.", "RemainsUnity/Assets/Remains/Gameplay/LooseItemPickup.cs"));
                else
                {
                    UnityEngine.Debug.LogWarning($"Нельзя подобрать свободную вещь: source={source.id}, actor={actor.id}, count={source.items.Count}; ожидалась одна стопка.");
                    session.ShowNotice(Remains.Content.GameLocalization.UiText("source.LooseItemPickup.3fb9c909f13dad1c", "Данные свободной вещи повреждены. Подбор отменён.", "RemainsUnity/Assets/Remains/Gameplay/LooseItemPickup.cs"));
                }
                return false;
            }
            var item=source.items[0];reason=ContainerAccess.ClaimRejection(actor,source,item,session.Rules.Loot);
            if(reason!=null) {session.ShowNotice(reason);return false;}
            string transaction=session.RequestTrackedClaim(source.id,item.id,0);
            if(transaction==null || !claim.Capture(transaction,actor,source.id,item.id,session.World.worldId)) return false;
            requestedDefinition=item.definition;
            session.ShowNotice(Remains.Content.GameLocalization.UiText("source.LooseItemPickup.86834ee1c83cebbd", "Подбор подтверждается…", "RemainsUnity/Assets/Remains/Gameplay/LooseItemPickup.cs"));return true;
        }
        public void Step()=>Step(Mirror.NetworkClient.isConnected);
        // Explicit connection state also lets isolated verification use the real gate/event.
        public void Step(bool connected)
        {
            string transaction=claim.Transaction,definition=requestedDefinition;
            bool confirmed=claim.Step(session.World,session.LocalActor,connected);
            if(!claim.Pending) requestedDefinition=null;
            if(confirmed) Confirmed?.Invoke(definition,transaction);
        }
        public void Dispose() {session.ClaimPrepared-=claim.Prepared;claim.Clear();}
    }
}
