using System;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public sealed class InteractionAdapter
    {
        private readonly CoopSession session;
        public event Action<GrenadeMessage> GrenadeReceived;
        public InteractionAdapter(CoopSession session)
        {
            this.session=session;
            session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<PropGrabMessage>(Grab);
                NetworkServer.RegisterHandler<PropPoseMessage>(Pose);
                NetworkServer.RegisterHandler<ThrowRequest>(Throw);
            };
            session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PropPoseMessage>(message=>
                {
                    var prop=session.World?.props.Find(p=>p.id==message.prop);
                    if(prop==null || prop.owner==session.LocalOwner || prop.generation!=message.generation) return;
                    Apply(prop,message);
                });
                NetworkClient.RegisterHandler<GrenadeMessage>(message=>GrenadeReceived?.Invoke(message));
            };
        }

        private void Grab(NetworkConnectionToClient connection,PropGrabMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var prop=session.ServerWorld.props.Find(p=>p.id==message.prop);
            if(actor==null || prop==null || actor.down) return;
            if(message.release)
            {
                if(prop.owner!=actor.owner) return;
                prop.held=false;
            }
            else
            {
                if(prop.held && prop.owner!=actor.owner) return;
                prop.owner=actor.owner;prop.generation++;prop.held=true;
            }
            session.CommitWorld();
        }
        private void Pose(NetworkConnectionToClient connection,PropPoseMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var prop=session.ServerWorld.props.Find(p=>p.id==message.prop);
            if(actor==null || prop==null || prop.owner!=actor.owner || prop.generation!=message.generation) return;
            Apply(prop,message);
            NetworkServer.SendToAll(message,Channels.Unreliable);
        }
        private static void Apply(PropState prop,PropPoseMessage message)
        { prop.x=message.x;prop.y=message.y;prop.vx=message.vx;prop.vy=message.vy;prop.held=message.held; }

        private void Throw(NetworkConnectionToClient connection,ThrowRequest message)
        {
            var actor=session.ConnectedActor(connection);
            if(actor==null || actor.down || session.ServerWorld.completedTransactions.Contains(message.transaction) ||
                !SharedInventory.Consume(actor,"grenade")) return;
            session.ServerWorld.completedTransactions.Add(message.transaction);
            Vector2 origin=actor.Position+new Vector2(0,0.8f);
            var velocity=(new Vector2(message.aimX,message.aimY)-origin).normalized*7+Vector2.up*3;
            session.CommitWorld();
            NetworkServer.SendToAll(new GrenadeMessage { id=message.transaction,attacker=actor.id,
                x=origin.x,y=origin.y,vx=velocity.x,vy=velocity.y,fuse=2 });
        }
        public void GrabProp(string prop,bool release) => NetworkClient.Send(new PropGrabMessage { prop=prop,release=release });
        public void SendProp(PropState prop) => NetworkClient.Send(new PropPoseMessage { prop=prop.id,generation=prop.generation,x=prop.x,y=prop.y,vx=prop.vx,vy=prop.vy,held=prop.held },Channels.Unreliable);
        public void ThrowGrenade(Vector2 aim)=>NetworkClient.Send(new ThrowRequest { transaction=Guid.NewGuid().ToString("N"),aimX=aim.x,aimY=aim.y });
    }
}
