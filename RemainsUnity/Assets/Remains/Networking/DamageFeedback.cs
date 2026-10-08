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
        private readonly HashSet<string> relayed=new();
        private readonly Queue<string> recentRelayed=new();
        public event Action<DamageResult> Received;
        public event Action<string,DamageResult> DeliveryMeasured;
        public int Accepted {get;private set;}
        public DamageFeedback(CoopSession session)
        {
            this.session=session;
            session.ServerReady+=()=>
            {
                relayed.Clear();recentRelayed.Clear();
                NetworkServer.RegisterHandler<DamageFeedbackMessage>(Relay);
            };
            session.ClientReady+=()=>
            {
                seen.Clear();recent.Clear();Accepted=0;
                NetworkClient.RegisterHandler<DamageFeedbackMessage>((message,channel)=>
                {
                    DeliveryMeasured?.Invoke(channel==Channels.Unreliable?"damage-number-delivered-unreliable":"damage-number-delivered-reliable",message.result);
                    DeliveryMeasured?.Invoke("damage-number-delivered",message.result);
                    Receive(message.result);
                });
            };
        }
        public void Publish(DamageResult result)
        {
            result.world=session.World?.worldId;
            // Health was already decided by this target's owner. Its own presentation
            // need not wait for a server echo; the shared receipt suppresses that echo.
            var target=session.World?.Find(result.actor);
            if(target!=null && target.owner==session.LocalOwner && target.generation==result.generation && target.level==result.level)
                Receive(result);
            DeliveryMeasured?.Invoke("damage-number-sent",result);
            var message=new DamageFeedbackMessage {result=result};
            // Bypass reliable head-of-line waiting for presentation only. The reliable
            // copy still delivers the result if the expedited datagram is lost.
            NetworkClient.Send(message,Channels.Unreliable);
            NetworkClient.Send(message);
        }
        private void Relay(NetworkConnectionToClient connection,DamageFeedbackMessage message,int channel)
        {
            var result=message.result;var sender=session.ConnectedActor(connection);
            DeliveryMeasured?.Invoke(channel==Channels.Unreliable?"damage-number-arrived-unreliable":"damage-number-arrived-reliable",result);
            var target=session.ServerWorld?.Find(result.actor);
            string rejection=sender==null?"sender":target==null?"target":sender.owner!=target.owner?"owner":
                target.generation!=result.generation?"generation":target.level!=result.level?"level":!Valid(result)?"invalid":null;
            // Ownership decides health. The host's copied HP/damage sequence may
            // still be behind the cosmetic receipt; immutable identity and the
            // life scope are sufficient here and do not introduce hit validation.
            if(rejection==null && result.confirmedNpcDeath)
                rejection=result.world!=session.ServerWorld.worldId?"world":target.player?"player-death":
                    target.actorType!=result.actorType?"actor-type":target.lifecycleSequence!=result.lifecycleSequence?"lifecycle":null;
            if(rejection!=null)
            {
                DeliveryMeasured?.Invoke("damage-number-rejected-"+rejection,result);
                return;
            }
            if(!Remember(relayed,recentRelayed,result.Key)) return;
            DeliveryMeasured?.Invoke("damage-number-relayed",result);
            NetworkServer.SendToAll(message,Channels.Unreliable);
            NetworkServer.SendToAll(message);
        }
        public static bool Valid(DamageResult result)=>!string.IsNullOrEmpty(result.attack) && !string.IsNullOrEmpty(result.actor) &&
            !string.IsNullOrEmpty(result.level) && result.amount>0 && float.IsFinite(result.amount) && float.IsFinite(result.x) && float.IsFinite(result.y) &&
            (!result.confirmedNpcDeath || !string.IsNullOrWhiteSpace(result.world) && !string.IsNullOrWhiteSpace(result.actorType) &&
                result.generation>=0 && result.lifecycleSequence>=0 && result.damageSequence>0);
        private void Receive(DamageResult result)
        {
            if(!Valid(result) || !Remember(seen,recent,result.Key)) return;
            Accepted++;Received?.Invoke(result);
        }
        private static bool Remember(HashSet<string> known,Queue<string> order,string key)
        {
            if(!known.Add(key)) return false;
            order.Enqueue(key);if(order.Count>2048) known.Remove(order.Dequeue());
            return true;
        }
    }
}
