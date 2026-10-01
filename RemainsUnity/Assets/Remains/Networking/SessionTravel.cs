using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct TravelRequest : NetworkMessage
    {
        public string id, source, destination;
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
        private RoomLayout factory, hub;
        private float deadline;
        private string pending;
        public bool Pending => pending != null;

        public SessionTravel(CoopSession session) => this.session = session;
        public void RegisterServer()
        {
            replies.Clear();
            factory = RoomLayout.Load("factory:0");
            hub = RoomLayout.Load("hub");
            NetworkServer.RegisterHandler<TravelRequest>(ReceiveRequest);
        }
        public void RegisterClient()
        {
            pending = null;
            NetworkClient.RegisterHandler<TravelReply>(ReceiveReply);
        }
        public void ResetClient() => pending = null;

        public void RequestFactory() => Request("factory:0");
        public void RequestHub() => Request("hub");
        private void Request(string destination)
        {
            if (Pending) return;
            var actor = session.LocalActor;
            if (actor == null || !NetworkClient.isConnected)
            { session.ShowNotice("Переход невозможен: нет подключения к игре."); return; }
            string reason = TravelRules.Rejection(actor, actor.level, destination, actor.generation, actor.lifecycleSequence);
            if (reason != null) { session.ShowNotice(reason); return; }
            pending = Guid.NewGuid().ToString("N");
            deadline = Time.unscaledTime + 10;
            // Reliable ordering publishes pending health changes before the request.
            session.PublishActor(actor);
            NetworkClient.Send(new TravelRequest { id = pending, source = actor.level, destination = destination,
                generation = actor.generation, lifecycle = actor.lifecycleSequence });
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
            var actor = session.ConnectedActor(connection);
            string key = connection.connectionId + ":" + request.id;
            if (replies.TryGetValue(key, out var previous)) { connection.Send(previous); return; }
            string reason = TravelRules.Rejection(actor, request.source, request.destination, request.generation, request.lifecycle);
            if (string.IsNullOrEmpty(request.id)) reason = "Не задан идентификатор перехода.";
            var reply = new TravelReply { id = request.id, accepted = reason == null, reason = reason };
            if (reply.accepted)
            {
                TravelRules.Apply(actor, request.destination, request.destination == "hub" ? hub : factory);
                session.CommitWorld();
                Debug.Log($"Переход подтверждён: actor={actor.id}, request={request.id}, level={actor.level}, lifecycle={actor.lifecycleSequence}.");
            }
            else Debug.LogWarning($"Переход отклонён: actor={actor?.id}, request={request.id}, source={request.source}, destination={request.destination}, причина={reason}.");
            replies[key] = reply;
            connection.Send(reply);
        }

        private void ReceiveReply(TravelReply reply)
        {
            if (pending != reply.id) return;
            pending = null;
            session.ShowNotice(reply.accepted ? "Переход завершён." : reply.reason);
        }
    }
}
