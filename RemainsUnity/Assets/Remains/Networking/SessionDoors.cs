using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct DoorRequest : NetworkMessage
    {public string request,world,door,actor;public int generation,lifecycle;public long revision;public bool open;}
    public struct DoorReply : NetworkMessage
    {public string request,reason;public bool accepted;}

    public sealed class SessionDoors
    {
        private readonly CoopSession session;
        private string pending,pendingDoor;
        private readonly Dictionary<string,(string actor,string door)> npcRequests=new();
        private readonly HashSet<string> pendingNpcs=new();
        private readonly Dictionary<string,float> nextNpcRequest=new();
        public bool Pending=>pending!=null;
        public event Action<DoorReply> Finished;
        public event Action<string,string,DoorReply> NpcFinished;
        public SessionDoors(CoopSession session)=>this.session=session;
        public void RegisterServer()=>NetworkServer.RegisterHandler<DoorRequest>(Remains.Networking.MessageTiming.Server<DoorRequest>(Receive));
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<DoorReply>(Remains.Networking.MessageTiming.Client<DoorReply>(reply=>
            {
                if(npcRequests.Remove(reply.request,out var context))
                {
                    pendingNpcs.Remove(context.actor);
                    if(!reply.accepted) session.RejectPrediction("doors:"+context.door,reply.reason);
                    if(!reply.accepted) Debug.LogWarning($"NPC не открыл дверь: actor={context.actor}, door={context.door}, request={reply.request}, reason={reply.reason}.");
                    NpcFinished?.Invoke(context.actor,context.door,reply);return;
                }
                if(reply.request!=pending) return;
                if(!reply.accepted && pendingDoor!=null) session.RejectPrediction("doors:"+pendingDoor,reply.reason);
                pending=null;pendingDoor=null;session.ShowNotice(reply.reason);Finished?.Invoke(reply);
            }));
        }
        public void ResetClient() {pending=null;pendingDoor=null;npcRequests.Clear();pendingNpcs.Clear();nextNpcRequest.Clear();}
        public bool RequestNpcOpen(ActorState actor,string doorId)
        {
            if(actor==null || actor.player || actor.owner!=session.LocalOwner || !NetworkClient.isConnected || pendingNpcs.Contains(actor.id) ||
                nextNpcRequest.TryGetValue(actor.id,out float next) && Time.unscaledTime<next) return false;
            var door=session.World.doors.Find(d=>d.id==doorId);
            if(door==null || door.open || session.Pause.IsPaused(actor.level) || session.Rules.DoorAccess.Rejection(actor,door)!=null) return false;
            string request=Guid.NewGuid().ToString("N");npcRequests.Add(request,(actor.id,doorId));pendingNpcs.Add(actor.id);
            nextNpcRequest[actor.id]=Time.unscaledTime+.5f;
            Send(actor,door,request,true);return true;
        }
        public bool Request(string doorId,bool open)
        {
            var actor=session.LocalActor;
            if(actor==null || !NetworkClient.isConnected || Pending) return false;
            var door=session.World.doors.Find(d=>d.id==doorId);
            string reason=door==null?"Дверь больше недоступна.":session.Rules.DoorAccess.Rejection(actor,door);
            if(session.Pause.IsPaused(actor.level)) reason="Локация на паузе: взаимодействие с дверью недоступно.";
            if(reason!=null) {session.ShowNotice(reason);return false;}
            pending=Guid.NewGuid().ToString("N");pendingDoor=door.id;
            Send(actor,door,pending,open);
            return true;
        }
        private void Send(ActorState actor,DoorState door,string request,bool open)
        {
            // Position update precedes the command on the reliable channel.
            session.PublishActor(actor);
            NetworkClient.Send(new DoorRequest {request=request,world=session.World.worldId,door=door.id,actor=actor.id,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,revision=door.revision,open=open});
            PredictOpen(actor.id,door.id,door.revision,open);
        }
        // The requesting client opens or closes the door at once (decisions.md, 2026-10-24) with the host's own rule and
        // revision step; the host's tick confirms it, a rejection or a missing confirmation restores the host's door.
        private void PredictOpen(string actorId,string doorId,long revision,bool open)
        {
            session.Predict("doors:"+doorId,WorldChangeKind.Doors,
                live=>
                {
                    var door=live.doors.Find(d=>d.id==doorId);var actor=live.Find(actorId);
                    bool occupied=door!=null && door.open && !open && DoorOccupancy.IsOccupied(live,door);
                    return DoorOperations.SetOpen(door,actor,revision,open,session.Rules.DoorAccess,occupied,out _)==DoorChange.Changed;
                },
                canonical=>{var door=canonical.doors.Find(d=>d.id==doorId);return door!=null && door.revision>revision;},
                (canonical,live)=>RestoreDoor(canonical,live,doorId));
        }
        internal static void RestoreDoor(WorldState canonical,WorldState live,string doorId)
        {
            int at=live.doors.FindIndex(d=>d.id==doorId);var source=canonical.doors.Find(d=>d.id==doorId);
            if(at>=0 && source!=null) live.doors[at]=Remains.Serialization.StateJson.Copy(source);
        }
        private void Receive(NetworkConnectionToClient connection,DoorRequest request)
        {
            var actor=DoorRequestRules.ResolveActor(session.ServerWorld,session.ConnectedActor(connection),request.actor,out string rejected);
            if(actor==null) {connection.Send(new DoorReply {request=request.request,reason=rejected,accepted=false});return;}
            var change=DoorRequestRules.Apply(session.ServerWorld,actor,request.world,request.door,request.generation,
                request.lifecycle,request.revision,request.open,actor!=null && session.Pause.IsServerPaused(actor.level),
                session.Rules.DoorAccess,out string reason);
            if(change==DoorChange.Changed) session.CommitWorld();
            connection.Send(new DoorReply {request=request.request,accepted=change!=DoorChange.Rejected,
                reason=reason??(request.open?"Дверь открыта.":"Дверь закрыта.")});
        }
    }
}
