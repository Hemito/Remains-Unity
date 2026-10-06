using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct ContainerInspectRequest : NetworkMessage
    {public string request,json;}
    public struct ContainerInspectReply : NetworkMessage
    {public string request,source,reason;public bool accepted,changed;public long appearanceRevision;}

    // No viewer lock or inventory transaction: the durable world owns one monotonic flag.
    public sealed class SessionContainerInspection
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,(ContainerInspectionContext context,double deadline)> pending=new(StringComparer.Ordinal);
        private string clientWorld;
        public event Action<ContainerInspectReply> Finished;
        public event Action<ContainerInspectRequest> Requested;
        public SessionContainerInspection(CoopSession session)=>this.session=session;
        public void RegisterServer()=>NetworkServer.RegisterHandler<ContainerInspectRequest>(Receive);
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<ContainerInspectReply>(reply=>
            {
                if(reply.request==null || !pending.Remove(reply.request,out var captured)) return;
                var context=captured.context;
                if(!context.Matches(session.World,session.LocalActor) || context.source!=reply.source) return;
                // The reply never changes graphics. Only the confirmed world can do so.
                if(!reply.accepted) session.ShowNotice(reply.reason);
                Finished?.Invoke(reply);
            });
        }
        public void ResetClient() {pending.Clear();clientWorld=null;}
        public bool Request(string sourceId)
        {
            var actor=session.LocalActor;var world=session.World;
            if(actor==null || world==null || !NetworkClient.isConnected) return false;
            if(clientWorld!=world.worldId) {ResetClient();clientWorld=world.worldId;}
            var container=world.containers.Find(value=>value.id==sourceId);
            if(container==null || container.presentationItem!=null || session.Pause.IsPaused(actor.level)) return false;
            // An explicit later E can retry; no lookup/retry work in Update.
            foreach(var key in new List<string>(pending.Keys))
                if(!pending[key].context.Matches(world,actor)) pending.Remove(key);
                else if(Time.realtimeSinceStartupAsDouble>=pending[key].deadline)
                {Debug.LogWarning($"Подтверждение просмотра не получено: world={world.worldId}, source={pending[key].context.source}, request={key}; новый E повторит запрос.");pending.Remove(key);}
            foreach(var waiting in pending.Values)
                if(waiting.context.source==sourceId && waiting.context.Matches(world,actor)) return true;
            if(pending.Count>=32)
            {Debug.LogWarning($"Просмотр контейнера не отправлен: слишком много неподтверждённых запросов, world={world.worldId}, source={sourceId}.");return false;}
            string request=Guid.NewGuid().ToString("N");var captured=new ContainerInspectionContext(world,actor,container);
            pending.Add(request,(captured,Time.realtimeSinceStartupAsDouble+10));
            var message=new ContainerInspectRequest {request=request,json=Remains.Serialization.StateJson.ToJson(captured)};
            session.PublishActor(actor);NetworkClient.Send(message);Requested?.Invoke(message);return true;
        }
        private void Receive(NetworkConnectionToClient connection,ContainerInspectRequest message)
        {
            var actor=session.ConnectedActor(connection);var world=session.ServerWorld;
            ContainerInspectionContext context=null;string reason;
            ContainerInspectChange change=ContainerInspectChange.Rejected;
            try
            {
                if(!Guid.TryParseExact(message.request,"N",out _) || message.json==null || message.json.Length>8192)
                    throw new InvalidOperationException("Неполный/слишком большой запрос просмотра контейнера.");
                context=Remains.Serialization.StateJson.FromJson<ContainerInspectionContext>(message.json);
                change=ContainerInspectionRules.Apply(world,actor,context,actor!=null && session.Pause.IsServerPaused(actor.level),
                    session.Rules.Loot,session.Rules.Deposit,out reason);
            }
            catch(Exception error) when(error is InvalidOperationException || error is System.IO.InvalidDataException)
            {reason="Просмотр контейнера не подтверждён: "+error.Message;Debug.LogWarning($"world={world?.worldId}, source={context?.source}, request={message.request}: {reason}");change=ContainerInspectChange.Rejected;}
            // Persistence failures propagate visibly; never send acceptance without a recoverable snapshot.
            if(change==ContainerInspectChange.Changed) session.CommitWorld();
            var result=world?.containers.Find(container=>container.id==context?.source);
            connection.Send(new ContainerInspectReply {request=message.request,source=context?.source,accepted=change!=ContainerInspectChange.Rejected,
                changed=change==ContainerInspectChange.Changed,appearanceRevision=result?.appearance?.revision??0,
                reason=reason??"Просмотр контейнера подтверждён."});
        }
    }
}
