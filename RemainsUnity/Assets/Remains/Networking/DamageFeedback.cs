using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;

namespace Remains.Networking
{
    public struct DamageFeedbackMessage : NetworkMessage {public DamageResult result;}
    // Cosmetic notification only; never applies health changes or recovery data.
    public sealed class DamageFeedback
    {
        private readonly CoopSession session;
        private readonly HashSet<string> seen=new();
        private readonly Queue<string> recent=new();
        public event Action<DamageResult> Received;
        public int Accepted {get;private set;}
        public DamageFeedback(CoopSession session)
        {
            this.session=session;
            session.ServerReady+=()=>NetworkServer.RegisterHandler<DamageFeedbackMessage>(Relay);
            session.ClientReady+=()=>
            {
                seen.Clear();recent.Clear();Accepted=0;
                NetworkClient.RegisterHandler<DamageFeedbackMessage>(message=>Receive(message.result));
            };
        }
        public void Publish(DamageResult result)
        {
            // Health was already decided by this target's owner. Its own presentation
            // need not wait for a server echo; the shared receipt suppresses that echo.
            var target=session.World?.Find(result.actor);
            if(target!=null && target.owner==session.LocalOwner && target.generation==result.generation && target.level==result.level)
                Receive(result);
            NetworkClient.Send(new DamageFeedbackMessage {result=result});
        }
        private void Relay(NetworkConnectionToClient connection,DamageFeedbackMessage message)
        {
            var result=message.result;var sender=session.ConnectedActor(connection);
            var target=session.ServerWorld?.Find(result.actor);
            if(sender==null || target==null || sender.owner!=target.owner || target.generation!=result.generation || target.level!=result.level || !Valid(result)) return;
            NetworkServer.SendToAll(message);
        }
        public static bool Valid(DamageResult result)=>!string.IsNullOrEmpty(result.attack) && !string.IsNullOrEmpty(result.actor) &&
            !string.IsNullOrEmpty(result.level) && result.amount>0 && float.IsFinite(result.amount) && float.IsFinite(result.x) && float.IsFinite(result.y);
        private void Receive(DamageResult result)
        {
            if(!Valid(result) || !seen.Add(result.Key)) return;
            recent.Enqueue(result.Key);if(recent.Count>2048) seen.Remove(recent.Dequeue());
            Accepted++;Received?.Invoke(result);
        }
    }
}
