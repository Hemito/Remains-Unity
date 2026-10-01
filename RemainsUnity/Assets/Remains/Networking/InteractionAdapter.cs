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
        public event Action<GrenadeState> GrenadeUpdated;
        public InteractionAdapter(CoopSession session)
        {
            this.session=session;
            session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<PropGrabMessage>(Grab);
                NetworkServer.RegisterHandler<PropPoseMessage>(Pose);
                NetworkServer.RegisterHandler<ThrowRequest>(Throw);
                NetworkServer.RegisterHandler<GrenadePoseMessage>(GrenadePose);
            };
            session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PropPoseMessage>(message=>
                {
                    var prop=session.World?.props.Find(p=>p.id==message.prop);
                    if(prop==null || prop.owner==session.LocalOwner || prop.generation!=message.generation) return;
                    Apply(prop,message);
                });
                NetworkClient.RegisterHandler<GrenadePoseMessage>(message=>
                {
                    var grenade=session.World?.grenades.Find(g=>g.id==message.id);
                    if(grenade==null || grenade.generation!=message.generation || unchecked((int)(message.sequence-grenade.sequence))<=0) return;
                    Apply(grenade,message);GrenadeUpdated?.Invoke(grenade);
                });
            };
        }

        private void Grab(NetworkConnectionToClient connection,PropGrabMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var prop=session.ServerWorld.props.Find(p=>p.id==message.prop);
            if(actor==null || prop==null || actor.down || actor.level!=prop.level) return;
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
            actor.revision++;
            session.ServerWorld.grenades.Add(new GrenadeState {id=message.transaction,attacker=actor.id,level=actor.level,owner=actor.owner,
                sourcePlayer=actor.player,generation=1,x=origin.x,y=origin.y,vx=velocity.x,vy=velocity.y,remaining=2});
            session.CommitWorld();
        }
        private static void Apply(GrenadeState state,GrenadePoseMessage message)
        {state.x=message.x;state.y=message.y;state.vx=message.vx;state.vy=message.vy;state.remaining=message.remaining;state.sequence=message.sequence;}
        private void GrenadePose(NetworkConnectionToClient connection,GrenadePoseMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var grenade=session.ServerWorld?.grenades.Find(g=>g.id==message.id);
            if(actor==null || grenade==null || grenade.owner!=actor.owner || grenade.generation!=message.generation ||
                unchecked((int)(message.sequence-grenade.sequence))<=0) return;
            if(!float.IsFinite(message.x) || !float.IsFinite(message.y) || !float.IsFinite(message.vx) || !float.IsFinite(message.vy) ||
                !float.IsFinite(message.remaining) || message.remaining<0 || message.remaining>grenade.remaining+.001f)
            {Debug.LogWarning($"Некорректное состояние гранаты: id={message.id}, generation={message.generation}, remaining={message.remaining}.");return;}
            Apply(grenade,message);
            if(message.detonate)
            {
                var explosion=GrenadeRules.Detonate(session.ServerWorld,grenade,message.enemyHits);
                if(explosion==null) return;
                session.CommitWorld();
                NetworkServer.SendToAll(new AttackMessage {json=JsonUtility.ToJson(explosion.attack)});
            }
            else NetworkServer.SendToAll(message);
        }
        public void SendGrenade(GrenadeState state,bool detonate,string[] enemyHits=null)
        {
            state.sequence++;
            NetworkClient.Send(new GrenadePoseMessage {id=state.id,generation=state.generation,sequence=state.sequence,
                x=state.x,y=state.y,vx=state.vx,vy=state.vy,remaining=state.remaining,detonate=detonate,enemyHits=enemyHits});
        }
        public void GrabProp(string prop,bool release) => NetworkClient.Send(new PropGrabMessage { prop=prop,release=release });
        public void SendProp(PropState prop) => NetworkClient.Send(new PropPoseMessage { prop=prop.id,generation=prop.generation,x=prop.x,y=prop.y,vx=prop.vx,vy=prop.vy,held=prop.held },Channels.Unreliable);
        public void ThrowGrenade(Vector2 aim)=>NetworkClient.Send(new ThrowRequest { transaction=Guid.NewGuid().ToString("N"),aimX=aim.x,aimY=aim.y });
    }
}
