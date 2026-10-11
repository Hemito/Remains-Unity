using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using Remains.Content;
using Remains.Diagnostics;
using UnityEngine;

namespace Remains.Networking
{
    public struct TravelRequest : NetworkMessage
    {
        public string id, source, destination,point;
        public int generation, lifecycle;
    }
    public struct TravelReply : NetworkMessage
    {
        public string id, reason;
        public bool accepted;
    }

    public sealed class SessionTravel
    {
        private readonly CoopSession session;
        private readonly Dictionary<string, TravelReply> replies = new();
        private float deadline;
        private string pending;
        private readonly Dictionary<string,TravelPoint[]> points=new(StringComparer.Ordinal);
        public bool Pending => pending != null;
        public event Action<TravelReply> Replied;

        public SessionTravel(CoopSession session) => this.session = session;
        public void RegisterServer()
        {
            replies.Clear();points.Clear();
            NetworkServer.RegisterHandler<TravelRequest>(ReceiveRequest);
        }
        public void RegisterClient()
        {
            pending = null;points.Clear();
            NetworkClient.RegisterHandler<TravelReply>(ReceiveReply);
        }
        public void ResetClient() => pending = null;

        public void RequestFactory() => Request("factory:0");
        public void RequestHub() => Request("hub");
        public TravelPoint[] Points(string level,bool server=false)
        {
            if(level==null) return Array.Empty<TravelPoint>();
            var world=server?session.ServerWorld:session.World;
            string definition=WorldRooms.Definition(world,level),key=definition+":"+level;
            if(!points.TryGetValue(key,out var result))
                points.Add(key,result=RoomTravelPointAuthoring.ReadAll(RoomAuthoring.Load(level,world),level));
            return result;
        }
        public TravelPoint Find(string id,string level,bool server=false)=>Array.Find(Points(level,server),p=>p.id==id);
        public void RequestVia(string id)
        {
            var point=Find(id,session.LocalActor?.level);
            if(point==null) {session.ShowNotice("Точка перехода больше недоступна.");return;}
            string reason=point.Rejection(session.LocalActor);
            if(reason!=null) {session.ShowNotice(reason);return;}
            Request(point.destination,id);
        }
        private void Request(string destination,string pointId=null)
        {
            if (Pending) return;
            var actor = session.LocalActor;
            if (actor == null || !NetworkClient.isConnected)
            { session.ShowNotice("Переход невозможен: нет подключения к игре."); return; }
            string reason = TravelRules.Rejection(actor, actor.level, destination, actor.generation, actor.lifecycleSequence,session.Pause.IsPaused(destination));
            if (reason != null) { session.ShowNotice(reason); return; }
            if(pointId==null && !session.DebugTravelAllowed)
            {session.ShowNotice(actor.level=="hub"?"Выберите место на карте в хабе.":"Вернуться в хаб можно через выходную дверь.");return;}
            pending = Guid.NewGuid().ToString("N");
            deadline = Time.unscaledTime + 10;
            // Reliable ordering publishes pending health changes before the request.
            session.PublishActor(actor);
            NetworkClient.Send(new TravelRequest { id = pending, source = actor.level, destination = destination,point=pointId,
                generation = actor.generation, lifecycle = actor.lifecycleSequence });
            Debug.Log($"Переход отправлен: request={pending}, actor={actor.id}, destination={destination}, time={Time.unscaledTime}.");
            session.ShowNotice("Ожидание подтверждения перехода…");
        }

        public void Step()
        {
            if (!Pending || Time.unscaledTime < deadline) return;
            Debug.LogWarning($"Не получено подтверждение перехода: actor={session.LocalActorId}, request={pending}.");
            pending = null;
            session.ShowNotice("Подтверждение перехода не получено. Проверьте подключение и повторите попытку.");
        }

        private void ReceiveRequest(NetworkConnectionToClient connection, TravelRequest request)
        {
            using var timing=TransitionTiming.Measure("travel.server-request",request.destination);
            var actor = session.ConnectedActor(connection);
            Debug.Log($"Переход получен: request={request.id}, connection={connection.connectionId}, actor={actor?.id}, time={Time.unscaledTime}.");
            string key = connection.connectionId + ":" + request.id;
            if (replies.TryGetValue(key, out var previous)) { connection.Send(previous); return; }
            string reason = TravelRules.Rejection(actor, request.source, request.destination, request.generation, request.lifecycle,session.Pause.IsServerPaused(request.destination));
            if (string.IsNullOrEmpty(request.id)) reason = "Не задан идентификатор перехода.";
            TravelPoint point=null;
            if(reason==null)
            {
                point=Find(request.point,request.source,true);
                if(point==null && !session.DebugTravelAllowed) reason="Переход требует карты или выходной двери.";
                else if(point!=null) reason=point.destination!=request.destination?"Назначение точки перехода изменилось.":point.Rejection(actor);
            }
            TravelPoint arrival=null;
            RoomLayout destination=null;
            if(reason==null) FactoryContentSources.Prepare(session.ServerWorld,request.destination);
            if(reason==null && string.IsNullOrEmpty(point?.arrivalPoint) && FactoryContentSources.IsGenerated(session.ServerWorld,request.destination))
            {
                destination=RoomLayout.Load(request.destination,session.ServerWorld);
                reason=FactoryArrival.Rejection(destination,Anatomy.Movement(actor),new Vector2(destination.spawnX,destination.spawnY));
            }
            if(reason==null && !string.IsNullOrEmpty(point?.arrivalPoint))
            {
                arrival=Find(point.arrivalPoint,request.destination,true);
                if(arrival==null) reason=$"Не найдена точка прибытия {point.arrivalPoint}.";
                else
                {
                    using(TransitionTiming.Measure("travel.arrival-layout",request.destination))
                        destination=RoomLayout.Load(request.destination,session.ServerWorld);
                    var hull=Anatomy.Movement(actor);
                    if(!ActorTerrainMotion.IsClear(destination,hull,arrival.Arrival) || !ActorTerrainMotion.Supported(destination,hull,arrival.Arrival))
                        reason=$"Точка прибытия {arrival.id} не имеет свободной физической опоры.";
                }
            }
            var reply = new TravelReply { id = request.id, accepted = reason == null, reason = reason };
            if (reply.accepted)
            {
                LampOperations.Install(session.ServerWorld,request.destination,LampContent.Read(session.ServerWorld,request.destination));
                RoomContentBatch content;
                DoorCapabilityLayout[] capabilities;
                using(TransitionTiming.Measure("travel.content-read",request.destination))
                {
                    var authoring=RoomAuthoring.Load(request.destination,session.ServerWorld);
                    content=RoomContentBatch.Read(authoring,request.destination);
                    capabilities=RoomDoorCapabilityContent.Read(authoring,request.destination);
                }
                using(TransitionTiming.Measure("travel.content-install",request.destination))
                    RoomContentInstaller.Apply(session.ServerWorld,content);
                // Rooms first visited after startup need the same explicit capability
                // journal as loaded rooms. Preserve the saved source before migration;
                // already damaged doors and recorded versions remain untouched.
                using(TransitionTiming.Measure("travel.door-capabilities",request.destination))
                    session.PrepareDoorCapabilities(capabilities,true);
                session.LevelTime.EnsureLevel(request.destination);
                using(TransitionTiming.Measure("travel.mechanisms",request.destination))
                    session.Mechanisms.ReconcileServer();
                // Reuse this request's authored layout already validated for arrival.
                // Content installation changes world entities, not the authored layout.
                using(TransitionTiming.Measure("travel.apply-layout",request.destination))
                    destination??=RoomLayout.Load(request.destination,session.ServerWorld);
                using(TransitionTiming.Measure("travel.apply",request.destination))
                {
                    TravelRules.Apply(actor, request.destination, destination);
                    if(arrival!=null) actor.Position=arrival.Arrival;
                }
                using(TransitionTiming.Measure("travel.commit",request.destination))
                    session.CommitWorld();
                Debug.Log($"Переход подтверждён: actor={actor.id}, request={request.id}, level={actor.level}, lifecycle={actor.lifecycleSequence}.");
            }
            else Debug.LogWarning($"Переход отклонён: actor={actor?.id}, request={request.id}, source={request.source}, destination={request.destination}, причина={reason}.");
            replies[key] = reply;
            connection.Send(reply);
        }

        private void ReceiveReply(TravelReply reply)
        {
            Replied?.Invoke(reply);
            if (pending != reply.id) return;
            pending = null;
            session.ShowNotice(reply.accepted ? "Переход завершён." : reply.reason);
        }
    }
}
