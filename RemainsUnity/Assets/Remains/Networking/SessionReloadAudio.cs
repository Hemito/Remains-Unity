using System;
using Mirror;
using Remains.Domain;

namespace Remains.Networking
{
    public struct ReloadSoundMessage : NetworkMessage
    {
        public string actor,token,weapon,weaponItem,level;
        public long selectionRevision;
        public int generation,lifecycle;
        public bool started;
    }

    // Relays cosmetic intent only. Reload timing and ammunition remain client-owned.
    public sealed class SessionReloadAudio
    {
        private readonly CoopSession session;
        private readonly ReloadSoundGate serverGate=new(),clientGate=new();
        public event Action<ReloadSoundMessage> Received;
        public event Action<ReloadSoundMessage> Sent;
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
                var actor=session.World?.Find(message.actor);
                if(!Matches(actor,message) || message.started && (actor.down || !actor.connected)) return;
                if(clientGate.Accept(message.actor,message.token,message.started)) Received?.Invoke(message);
            });
        }
        public static bool Matches(ActorState actor,ReloadSoundMessage message)=>actor!=null &&
            actor.id==message.actor && actor.generation==message.generation && actor.lifecycleSequence==message.lifecycle && actor.level==message.level &&
            (!message.started || actor.weapon==message.weapon && actor.equippedWeaponItem==message.weaponItem &&
                actor.weaponSelectionRevision==message.selectionRevision);
        public void Send(ReloadSoundMessage message)
        {
            if(!NetworkClient.isConnected) return;
            NetworkClient.Send(message);Sent?.Invoke(message);
        }
    }
}
