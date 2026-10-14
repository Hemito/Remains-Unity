using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct ActorHoldRequest : NetworkMessage
    {
        public string id,body,token;
        public int bodyGeneration,bodyLifecycle,holderGeneration,holderLifecycle;
        public bool release;
    }
    public struct ActorHoldReply : NetworkMessage {public string id,reason;public bool accepted;}
    public struct ActorHoldTargetMessage : NetworkMessage
    {public string body,holder;public int epoch;public uint sequence;public float x,y;}
    public sealed class SessionActorHolds
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,ActorHoldReply> replies=new();
        public event Action<ActorHoldReply> Replied;
        public SessionActorHolds(CoopSession session)=>this.session=session;
        public void RegisterServer()
        {replies.Clear();NetworkServer.RegisterHandler<ActorHoldRequest>(Remains.Networking.MessageTiming.Server<ActorHoldRequest>(Receive));NetworkServer.RegisterHandler<ActorHoldTargetMessage>(Remains.Networking.MessageTiming.Server<ActorHoldTargetMessage>(ReceiveTarget));}
        public void RegisterClient()
        {
            NetworkClient.RegisterHandler<ActorHoldReply>(Remains.Networking.MessageTiming.Client<ActorHoldReply>(reply=>
            {if(!reply.accepted) session.ShowNotice(reply.reason);Replied?.Invoke(reply);}));
            RegisterTargetClient();
        }
        public void Step()
        {
            if(!NetworkServer.active || session.ServerWorld==null) return;
            if(ActorHoldRules.Reconcile(session.ServerWorld,session.ServerWorld.actors
                .Where(a=>a.player && a.connected).Select(a=>a.owner).ToArray())) session.CommitWorld();
        }
        public string Grab(string body,string token=null,bool release=false)
        {
            var holder=session.LocalActor;var target=session.World?.Find(body);
            if(holder==null || target==null || !NetworkClient.isConnected) return null;
            string id=Guid.NewGuid().ToString("N");
            NetworkClient.Send(new ActorHoldRequest {id=id,body=body,token=token,release=release,
                bodyGeneration=target.generation,bodyLifecycle=target.lifecycleSequence,
                holderGeneration=holder.generation,holderLifecycle=holder.lifecycleSequence});
            return id;
        }
        private void Receive(NetworkConnectionToClient connection,ActorHoldRequest request)
        {
            string key=connection.connectionId+":"+request.id;
            if(replies.TryGetValue(key,out var previous)) {connection.Send(previous);return;}
            var holder=session.ConnectedActor(connection);var body=session.ServerWorld?.Find(request.body);
            string reason=null;
            if(string.IsNullOrEmpty(request.id) || holder==null || body==null) reason="Не найдены участники захвата тела.";
            else if(holder.generation!=request.holderGeneration || holder.lifecycleSequence!=request.holderLifecycle)
                reason="Состояние держателя изменилось. Повторите захват.";
            else if(request.release)
            {
                if(!ActorHoldRules.Release(body,holder.id,request.token)) reason="Этот захват уже завершён или принадлежит другому игроку.";
            }
            else if(session.Pause.IsServerPaused(holder.level)) reason="Уровень остановлен: новый захват тела недоступен.";
            else if(body.generation!=request.bodyGeneration || body.lifecycleSequence!=request.bodyLifecycle)
                reason="Состояние тела изменилось. Повторите захват.";
            else
            {
                reason=ActorHoldRules.Rejection(session.ServerWorld,holder,body);
                if(reason==null)
                {
                    if(session.Rules.Telekinesis is not IActorTelekinesisRule rules)
                        reason="Текущее правило телекинеза не поддерживает перенос актёров.";
                    else reason=rules.Rejection(holder,new TelekinesisTarget(body),false);
                }
                if(reason==null) ActorHoldRules.Begin(holder,body,request.id);
            }
            var reply=new ActorHoldReply {id=request.id,accepted=reason==null,reason=reason};
            if(reply.accepted) session.CommitWorld();
            replies.Add(key,reply);connection.Send(reply);
        }
        public void SendTarget(ActorState body,Vector2 target)
        {
            var holder=session.LocalActor;
            if(holder==null || body.hold?.holder!=holder.id) return;
            var message=new ActorHoldTargetMessage {body=body.id,holder=holder.id,epoch=body.holdEpoch,
                sequence=unchecked(body.hold.sequence+1),x=target.x,y=target.y};
            ActorHoldRules.SetTarget(body,holder.id,message.epoch,message.sequence,target);
            NetworkClient.Send(message,Channels.Unreliable);
        }
        private void ReceiveTarget(NetworkConnectionToClient connection,ActorHoldTargetMessage message)
        {
            var holder=session.ConnectedActor(connection);var body=session.ServerWorld?.Find(message.body);
            if(holder==null || holder.id!=message.holder || holder.down || holder.level!=body?.level ||
                session.Pause.IsServerPaused(holder.level)) return;
            if(!ActorHoldRules.SetTarget(body,holder.id,message.epoch,message.sequence,new Vector2(message.x,message.y))) return;
            NetworkServer.SendToAll(message,Channels.Unreliable);
        }
        public void RegisterTargetClient()=>NetworkClient.RegisterHandler<ActorHoldTargetMessage>(Remains.Networking.MessageTiming.Client<ActorHoldTargetMessage>(message=>
            ActorHoldRules.SetTarget(session.World?.Find(message.body),message.holder,message.epoch,message.sequence,new Vector2(message.x,message.y))));
    }
}
