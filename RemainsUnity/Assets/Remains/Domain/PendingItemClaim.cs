namespace Remains.Domain
{
    // Local intent identity. A preparation reply never changes the real inventory.
    public sealed class PendingItemClaim
    {
        private string actorId,worldId;
        private int generation,lifecycle;
        public string Transaction {get;private set;}
        public string SourceId {get;private set;}
        public string ItemId {get;private set;}
        public bool Pending=>Transaction!=null;
        public bool ReservationAccepted {get;private set;}
        public bool Capture(string transaction,ActorState actor,string source,string item,string world=null)
        {
            if(Pending || string.IsNullOrEmpty(transaction) || actor==null || string.IsNullOrEmpty(source) || string.IsNullOrEmpty(item)) return false;
            actorId=actor.id;worldId=world;generation=actor.generation;lifecycle=actor.lifecycleSequence;
            SourceId=source;ItemId=item;Transaction=transaction;return true;
        }
        public void Prepared(string transaction,bool accepted)
        {
            if(!Pending || transaction!=Transaction) return;
            if(accepted) ReservationAccepted=true;
            else if(!ReservationAccepted) Clear();
        }
        public bool Step(WorldState world,ActorState actor,bool connected)
        {
            if(!Pending) return false;
            if(!connected || actor==null || actor.id!=actorId || actor.generation!=generation || actor.lifecycleSequence!=lifecycle ||
                world==null || worldId!=null && worldId!=world.worldId) {Clear();return false;}
            if(!world.completedTransactions.Contains(Transaction)) return false;
            Clear();return true;
        }
        public void Clear() {Transaction=null;SourceId=null;ItemId=null;actorId=null;worldId=null;ReservationAccepted=false;}
    }
}
