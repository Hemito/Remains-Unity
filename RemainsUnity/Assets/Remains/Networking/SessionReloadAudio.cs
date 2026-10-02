using System;
using Mirror;
using Remains.Domain;

namespace Remains.Networking
{
    public struct ReloadSoundMessage : NetworkMessage
    {
        public string actor,token,weapon,level;
        public int generation,lifecycle;
        public bool started;
    }

    // Relays cosmetic intent only. Reload timing and ammunition remain client-owned.
    public sealed class SessionReloadAudio
    {
        private readonly CoopSession session;
        private readonly ReloadSoundGate serverGate=new(),clientGate=new();
        public event Action<ReloadSoundMessage> Received;
        public SessionReloadAudio(CoopSession session)=>this.session=session;
        public void RegisterServer()
        {
            serverGate.Clear();
            NetworkServer.RegisterHandler<ReloadSoundMessage>((connection,message)=>
            {
                var actor=session.ServerWorld==null?null:session.ConnectedActor(connection);
                if(actor==null || actor.id!=message.actor || !Matches(actor,message)) return;
                if(message.started && (actor.down || !actor.connected)) return;
                if(serverGate.Accept(message.actor,message.token,message.started)) NetworkServer.SendToAll(message);
            });
        }
        public void RegisterClient()
        {
            clientGate.Clear();
            NetworkClient.RegisterHandler<ReloadSoundMessage>(message=>
            {
                if(message.actor==session.LocalActorId) return;
                if(clientGate.Accept(message.actor,message.token,message.started)) Received?.Invoke(message);
            });
        }
        public static bool Matches(ActorState actor,ReloadSoundMessage message)=>actor!=null &&
            actor.generation==message.generation && actor.lifecycleSequence==message.lifecycle && actor.level==message.level;
        public void Send(ReloadSoundMessage message)
        {if(NetworkClient.isConnected) NetworkClient.Send(message);}
    }
}
