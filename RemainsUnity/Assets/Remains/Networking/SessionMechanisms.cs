using System;
using System.Collections.Generic;
using Mirror;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct MechanismRequest : NetworkMessage
    {
        public string operation,world,actor,button,definition,level,room,group,platform,repairState;
        public int generation,lifecycle,damageSequence;
        public long revision,platformRevision;
        public float x,y;
        public double heldSeconds;
        public bool repair,pressed;
    }
    public struct MechanismReply : NetworkMessage
    {public string operation,reason;public bool accepted;}

    // Shared mechanism changes are durable coordinated operations; actor motion is
    // still simulated by each entity owner against the same confirmed segment.
    public sealed class SessionMechanisms
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,MovingPlatformDefinition> types=new(StringComparer.Ordinal);
        private MechanismOperations serverOperations;
        private string pending;
        private double deadline;
        public bool Pending=>pending!=null;
        public event Action<MechanismReply> Finished;
        public SessionMechanisms(CoopSession session)=>this.session=session;
        public void RegisterServer()=>NetworkServer.RegisterHandler<MechanismRequest>(Receive);
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<MechanismReply>(reply=>
            {
                if(reply.operation!=pending) return;
                pending=null;session.ShowNotice(reply.reason);Finished?.Invoke(reply);
            });
        }
        public void ResetClient()=>pending=null;
        public void ResetServer() {types.Clear();serverOperations=null;}
        public void ReconcileServer()
        {
            if(session.ServerWorld==null) {types.Clear();return;}
            var catalog=MechanismCatalogAsset.Load();
            foreach(var platform in session.ServerWorld.movingPlatforms)
                if(!types.ContainsKey(platform.definition)) types.Add(platform.definition,catalog.Platform(platform.definition).CreateDefinition());
            MechanismPersistence.ValidateWorld(session.ServerWorld,id=>types[id]);
            serverOperations=new MechanismOperations(session.ServerWorld,session.Rules.ButtonRepair);
        }
        public void Step()
        {
            if(Pending && Time.unscaledTimeAsDouble>=deadline)
            {
                string operation=pending;pending=null;
                Debug.LogWarning($"Подтверждение механизма не получено: мир={session.World?.worldId}, operation={operation}.");
                session.ShowNotice("Подтверждение взаимодействия не получено. Проверьте подключение.");
                Finished?.Invoke(new MechanismReply {operation=operation,reason="Время ожидания подтверждения истекло."});
            }
            if(!NetworkServer.active || session.ServerWorld==null) return;
            bool changed=false;
            foreach(var platform in session.ServerWorld.movingPlatforms)
            {
                if(platform.removed || platform.segment==null) continue;
                double now=session.LevelTime.ReadServer(platform.level);
                if(now<platform.segment.EndElapsedSeconds) continue;
                var result=(serverOperations??throw new InvalidOperationException("Не загружен координатор механизмов.")).Complete(platform,types[platform.definition],now,platform.segment.sequence);
                if(result.Outcome==MechanismOperationOutcome.Rejected)
                    throw new InvalidOperationException($"Завершение платформы {platform.id} отклонено: {result.Reason}");
                changed|=result.Outcome==MechanismOperationOutcome.Changed;
            }
            if(changed) session.CommitWorld();
        }
        public bool RequestActivation(string buttonId)
        {
            var actor=session.LocalActor;var button=session.World?.mechanismButtons.Find(b=>b.id==buttonId);
            if(actor==null || button==null || !NetworkClient.isConnected || Pending) return false;
            var platform=session.World.movingPlatforms.Find(p=>p.id==button.platformId);
            if(platform==null) throw new InvalidOperationException("Нет платформы кнопки "+button.id);
            return Send(actor,button,new MechanismRequest {operation="mechanism:"+Guid.NewGuid().ToString("N"),platformRevision=platform.revision});
        }
        public bool RequestRepair(ButtonRepairCandidate candidate,double heldSeconds)
        {
            var actor=session.LocalActor;var button=session.World?.mechanismButtons.Find(b=>b.id==candidate?.ButtonId);
            if(actor==null || button==null || !NetworkClient.isConnected || Pending) return false;
            if(!candidate.MatchesActor(actor) || !candidate.MatchesTarget(button))
            {session.ShowNotice("Ремонт отменён: состояние персонажа или кнопки изменилось.");return false;}
            return Send(actor,button,new MechanismRequest {operation=candidate.AttemptId,repair=true,heldSeconds=heldSeconds});
        }
        private bool Send(ActorState actor,MechanismButtonState button,MechanismRequest request)
        {
            if(session.Pause.IsPaused(actor.level)) {session.ShowNotice("Локация на паузе: кнопка недоступна.");return false;}
            request.world=session.World.worldId;request.actor=actor.id;request.generation=actor.generation;
            request.lifecycle=actor.lifecycleSequence;request.damageSequence=actor.damageSequence;request.button=button.id;
            request.definition=button.definition;request.level=button.level;request.room=button.roomId;request.group=button.groupId;
            request.platform=button.platformId;request.revision=button.revision;request.repairState=button.repairState;
            request.pressed=button.pressed;request.x=button.x;request.y=button.y;
            pending=request.operation;deadline=Time.unscaledTimeAsDouble+10;
            session.PublishActor(actor);NetworkClient.Send(request);return true;
        }
        private void Receive(NetworkConnectionToClient connection,MechanismRequest request)
        {
            var world=session.ServerWorld;var actor=session.ConnectedActor(connection);
            string reason=null;
            if(world==null || request.world!=world.worldId || actor==null || request.actor!=actor.id ||
                actor.generation!=request.generation || actor.lifecycleSequence!=request.lifecycle)
                reason="Взаимодействие отменено: мир или персонаж изменился.";
            if(reason==null && request.operation!=null && request.operation.StartsWith("mechanism:",StringComparison.Ordinal) &&
                world.completedTransactions.Contains(request.operation))
            {connection.Send(new MechanismReply {operation=request.operation,accepted=true,reason="Взаимодействие уже подтверждено."});return;}
            var button=world?.mechanismButtons.Find(b=>b.id==request.button);
            if(reason==null && (button==null || button.removed)) reason="Кнопка больше недоступна.";
            if(reason==null && session.Pause.IsServerPaused(actor.level)) reason="Локация на паузе: кнопка недоступна.";
            var result=default(MechanismOperationResult);
            if(reason==null)
            {
                var type=MechanismCatalogAsset.Load().Button(button.definition).CreateDefinition();
                var operations=new MechanismOperations(world,session.Rules.ButtonRepair);
                if(request.repair)
                {
                    if(request.damageSequence!=actor.damageSequence || request.definition!=button.definition ||
                        request.level!=button.level || request.room!=button.roomId || request.group!=button.groupId ||
                        request.platform!=button.platformId || request.revision!=button.revision || request.repairState!=button.repairState ||
                        request.pressed!=button.pressed || request.x!=button.x || request.y!=button.y ||
                        !double.IsFinite(request.heldSeconds) || request.heldSeconds<type.repairDurationSeconds)
                        reason="Ремонт отменён: захваченное состояние или время удержания изменилось.";
                    else result=operations.ApplyRepair(actor,button,new ButtonRepairCandidate(request.operation,actor,button),type.interactionRadius,request.operation);
                }
                else
                {
                    var platform=world.movingPlatforms.Find(p=>p.id==button.platformId);
                    if(platform==null || platform.revision!=request.platformRevision) reason="Состояние платформы изменилось. Нажмите E ещё раз.";
                    else result=operations.TryActivate(actor,button,platform,types[platform.definition],session.LevelTime.ReadServer(platform.level),
                        request.revision,type.interactionRadius,request.operation);
                }
            }
            bool accepted=reason==null && result.Outcome!=MechanismOperationOutcome.Rejected;
            if(accepted && result.Outcome==MechanismOperationOutcome.Changed) session.CommitWorld();
            connection.Send(new MechanismReply {operation=request.operation,accepted=accepted,
                reason=reason??result.Reason??(request.repair?"Кнопка отремонтирована. Нажмите E для запуска платформы.":"Платформа запущена.")});
        }
    }
}
