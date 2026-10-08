using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;

namespace Remains.Runtime
{
    // Opt-in diagnostic messages. They neither grant items nor alter the game protocol.
    public struct FactoryRouteContainerObserved : NetworkMessage
    {public string world,actor,source,json;public int generation,lifecycle,phase;}
    public struct FactoryRouteContainerDecision : NetworkMessage
    {public string world,source,json;public int phase;}

    public sealed class FactoryRouteContainerChannel
    {
        private readonly PrototypeRuntime runtime;
        private readonly Dictionary<string,FactoryRouteContainerClaims> sources=new(StringComparer.Ordinal);
        private FactoryRouteContainerClaims active;
        public string Failure {get;private set;}

        // One channel is constructed before SessionReady, shared by all route sources.
        public FactoryRouteContainerChannel(PrototypeRuntime value)
        {
            runtime=value??throw new ArgumentNullException(nameof(value));
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<FactoryRouteContainerObserved>(Observe);
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<FactoryRouteContainerDecision>(Decide);
            value.Session.ClaimPrepared+=ClaimPrepared;
            value.Session.Changed+=Changed;
        }
        internal void Register(string source,FactoryRouteContainerClaims service)
        {
            if(string.IsNullOrEmpty(source) || service==null || sources.ContainsKey(source))
                throw new InvalidOperationException("Контейнерный маршрут повторил или не указал источник: source="+source);
            sources.Add(source,service);
        }
        internal void Activate(FactoryRouteContainerClaims service)
        {
            if(active!=null && !ReferenceEquals(active,service))
                throw new InvalidOperationException("Два контейнерных маршрута одновременно управляют обычным UI.");
            active=service;
        }
        internal void Release(FactoryRouteContainerClaims service)
        {if(ReferenceEquals(active,service)) active=null;}
        private void ClaimPrepared(string transaction,bool accepted)=>active?.ClaimPrepared(transaction,accepted);
        private void Changed()=>active?.CaptureReservations();
        private void Observe(NetworkConnectionToClient connection,FactoryRouteContainerObserved message)
        {
            if(Failure!=null || message.world!=runtime.Session.ServerWorld?.worldId) return;
            if(string.IsNullOrEmpty(message.source) || !sources.TryGetValue(message.source,out var service))
            {Failure="Контейнерный маршрут получил неизвестный источник: source="+message.source+", world="+message.world;return;}
            service.Observe(connection,message);
        }
        private void Decide(FactoryRouteContainerDecision message)
        {
            if(Failure!=null || message.world!=runtime.Session.World?.worldId) return;
            if(string.IsNullOrEmpty(message.source) || !sources.TryGetValue(message.source,out var service))
            {Failure="Контейнерный маршрут получил решение для неизвестного источника: source="+message.source+", world="+message.world;return;}
            service.AcceptDecision(message);
        }
        internal void Send(FactoryRouteContainerObserved message)=>NetworkClient.Send(message);
        internal void Broadcast(FactoryRouteContainerDecision message)=>NetworkServer.SendToAll(message);
    }
}
