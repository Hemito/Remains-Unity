namespace Remains.Domain
{
    // Local intent identity. A preparation reply never changes the real inventory.
    public sealed class PendingItemClaim
    {
        private string actorId;
        private int generation,lifecycle;
        public string Transaction {get;private set;}
        public string SourceId {get;private set;}
        public string ItemId {get;private set;}
        public bool Pending=>Transaction!=null;
        public bool ReservationAccepted {get;private set;}
        public bool Capture(string transaction,ActorState actor,string source,string item)
        {
            if(Pending || string.IsNullOrEmpty(transaction) || actor==null || string.IsNullOrEmpty(source) || string.IsNullOrEmpty(item)) return false;
            actorId=actor.id;generation=actor.generation;lifecycle=actor.lifecycleSequence;
            SourceId=source;ItemId=item;Transaction=transaction;return true;
        }
        public void Prepared(string transaction,bool accepted)
        {
            if(!Pending || transaction!=Transaction) return;
            if(accepted) ReservationAccepted=true;
            else if(!ReservationAccepted) Clear();
        }
        public void Step(WorldState world,ActorState actor,bool connected)
        {
            if(!Pending) return;
            if(!connected || actor==null || actor.id!=actorId || actor.generation!=generation || actor.lifecycleSequence!=lifecycle ||
                world==null || world.completedTransactions.Contains(Transaction)) Clear();
        }
        public void Clear() {Transaction=null;SourceId=null;ItemId=null;actorId=null;ReservationAccepted=false;}
    }
}
