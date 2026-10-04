using System;
using Mirror;
using Remains.Domain;

namespace Remains.Networking
{
    public struct DoorRequest : NetworkMessage
    {public string request,world,door;public int generation,lifecycle;public long revision;public bool open;}
    public struct DoorReply : NetworkMessage
    {public string request,reason;public bool accepted;}

    public sealed class SessionDoors
    {
        private readonly CoopSession session;
        private string pending;
        public bool Pending=>pending!=null;
        public event Action<DoorReply> Finished;
        public SessionDoors(CoopSession session)=>this.session=session;
        public void RegisterServer()=>NetworkServer.RegisterHandler<DoorRequest>(Receive);
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<DoorReply>(reply=>
            {
                if(reply.request!=pending) return;
                pending=null;session.ShowNotice(reply.reason);Finished?.Invoke(reply);
            });
        }
        public void ResetClient()=>pending=null;
        public bool Request(string doorId,bool open)
        {
            var actor=session.LocalActor;
            if(actor==null || !NetworkClient.isConnected || Pending) return false;
            var door=session.World.doors.Find(d=>d.id==doorId);
            string reason=door==null?"Дверь больше недоступна.":session.Rules.DoorAccess.Rejection(actor,door);
            if(session.Pause.IsPaused(actor.level)) reason="Локация на паузе: взаимодействие с дверью недоступно.";
            if(reason!=null) {session.ShowNotice(reason);return false;}
            pending=Guid.NewGuid().ToString("N");
            // Position update precedes the command on the reliable channel.
            session.PublishActor(actor);
            NetworkClient.Send(new DoorRequest {request=pending,world=session.World.worldId,door=doorId,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,revision=door.revision,open=open});
            return true;
        }
        private void Receive(NetworkConnectionToClient connection,DoorRequest request)
        {
            var actor=session.ConnectedActor(connection);
            var change=DoorRequestRules.Apply(session.ServerWorld,actor,request.world,request.door,request.generation,
                request.lifecycle,request.revision,request.open,actor!=null && session.Pause.IsServerPaused(actor.level),
                session.Rules.DoorAccess,out string reason);
            if(change==DoorChange.Changed) session.CommitWorld();
            connection.Send(new DoorReply {request=request.request,accepted=change!=DoorChange.Rejected,
                reason=reason??(request.open?"Дверь открыта.":"Дверь закрыта.")});
        }
    }
}
