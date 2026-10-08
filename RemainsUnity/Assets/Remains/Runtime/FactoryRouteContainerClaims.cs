using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // One real E/grid/whole-stack protocol. SharedInventory remains the sole writer.
    public sealed class FactoryRouteContainerClaims
    {
        private enum Stage {None,Open,Viewed,Claim,Granted,Complete}
        private readonly PrototypeRuntime runtime;
        private readonly FactoryRouteContainerChannel channel;
        private readonly string sourceId,label,capturePrefix;
        private readonly Action<ContainerState> validate;
        private readonly Dictionary<string,WarehouseLowerBaseline> viewers=new(StringComparer.Ordinal);
        private readonly Dictionary<string,string> observedViews=new(StringComparer.Ordinal);
        private readonly HashSet<string> granted=new(StringComparer.Ordinal);
        private readonly List<WarehouseLowerClaim> claims=new();
        private readonly IReadOnlyList<WarehouseLowerClaim> claimView;
        private readonly Dictionary<int,string> decisions=new();
        private readonly Dictionary<string,PendingGrant> observedReservations=new(StringComparer.Ordinal);
        private Stage stage;
        private RoomLayout room;
        private WarehouseLowerLootEvidence plan;
        private WarehouseLowerBaseline baseline;
        private string worldId,level,actorId,owner,capture,claimTransaction,failure;
        private int generation,lifecycle,serverPhase,claimIndex;
        private bool openSent,viewSent,grantSent,decisionSent,clicking,clickDone;
        public bool Started=>stage!=Stage.None;
        public bool Complete=>stage==Stage.Complete;
        public string Failure=>failure??channel.Failure;
        public string Progress=>$"source={sourceId}, phase={stage}, actor={Actor?.id}, generation={Actor?.generation}/{generation}, life={Actor?.lifecycleSequence}/{lifecycle}, position={Actor?.Position}, UI={runtime.Hud?.ContainerOpen}/{runtime.Hud?.ShownInventorySourceId}, views={viewers.Count}, grants={granted.Count}, claim={claimIndex}/{plan?.items?.Length}, tx={claimTransaction}, click={clicking}/{clickDone}";
        public IReadOnlyList<WarehouseLowerClaim> Claims=>claimView;
        public WarehouseLowerLootEvidence Evidence=>plan;
        private ActorState Actor=>runtime.Session.LocalActor;
        private ContainerState Target=>runtime.Session.World?.containers.SingleOrDefault(c=>c.id==sourceId);
        private bool Host=>runtime.Options.Role=="host";

        public FactoryRouteContainerClaims(PrototypeRuntime runtime,FactoryRouteContainerChannel channel,string sourceId,
            string label,Action<ContainerState> validate,string capturePrefix)
        {
            this.runtime=runtime??throw new ArgumentNullException(nameof(runtime));
            this.channel=channel??throw new ArgumentNullException(nameof(channel));
            this.sourceId=!string.IsNullOrEmpty(sourceId)?sourceId:throw new ArgumentException("Не указан источник контейнерного маршрута.",nameof(sourceId));
            this.label=!string.IsNullOrEmpty(label)?label:throw new ArgumentException("Не указан контекст контейнерного маршрута.",nameof(label));
            this.validate=validate??throw new ArgumentNullException(nameof(validate));
            this.capturePrefix=!string.IsNullOrEmpty(capturePrefix)?capturePrefix:throw new ArgumentException("Не указан префикс кадров контейнерного маршрута.",nameof(capturePrefix));
            claimView=claims.AsReadOnly();channel.Register(sourceId,this);
        }
        public void Begin()
        {
            if(Started) throw new InvalidOperationException(label+": контейнерный маршрут нельзя запускать повторно.");
            if(Actor==null || runtime.Hud==null || runtime.Options.ProbePeers is <1 or >2)
                throw new InvalidOperationException(label+": требуются HUD и один/два участника.");
            validate(Target);
            if(Target==null || Target.level!=Actor.level)
                throw new InvalidOperationException(label+": контейнер и игрок находятся в разных локациях.");
            worldId=runtime.Session.World.worldId;level=Actor.level;
            actorId=Actor.id;owner=Actor.owner;generation=Actor.generation;lifecycle=Actor.lifecycleSequence;
            room=runtime.Geometry.Get(level);channel.Activate(this);runtime.Hud.CloseContainer();stage=Stage.Open;
        }
        public void SynchronizeLifecycle(int previous,int next)
        {
            if(!Started || Complete) return;
            if(lifecycle!=previous || next!=previous+1) {Fail("Неверное подтверждённое изменение жизненного цикла.");return;}
            lifecycle=next;
        }
        public PlayerCommand Command()
        {
            if(!Started || Complete || Failure!=null || runtime.Session.Pause.IsPaused(level)) return default;
            try
            {
                RequireContext();
                if(stage!=Stage.Open || openSent) return default;
                var aim=((InventorySource)Target).Bounds.center;
                var selected=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,aim,
                    runtime.Session.Travel.Points(level),runtime.MechanismInteraction.Points);
                if(selected.kind!=InteractionKind.Container || selected.id!=sourceId)
                    throw new InvalidOperationException($"E выбрал {selected.kind}/{selected.id}, ожидался {sourceId}, cursor={aim}.");
                openSent=true;return new PlayerCommand {interact=true,hasAim=true,aim=aim};
            }
            catch(Exception error) {Fail(error.Message);return default;}
        }
        public void Tick()
        {
            if(!Started || Complete || Failure!=null) return;
            try
            {
                RequireContext();if(runtime.Session.Pause.IsPaused(level)) return;
                if(stage==Stage.Open && runtime.Hud.ContainerOpen && runtime.Hud.ContainerWindowRect.gameObject.activeInHierarchy &&
                    !runtime.Combat.Reload.Active)
                {
                    RequireWindow();validate(Target);
                    var items=Target.items.OrderBy(i=>i.id,StringComparer.Ordinal).Select(i=>i.Copy()).ToArray();
                    if(items.Length==0 || !FactoryRouteLootProof.SameItems(items,items))
                        throw new InvalidOperationException("Нет валидных настоящих целых стопок; содержимое не будет подменено.");
                    baseline=new WarehouseLowerBaseline {actor=actorId,generation=generation,
                        counts=items.Select(i=>InventoryJournal.Count(Actor,i.definition)).ToArray()};
                    plan=new WarehouseLowerLootEvidence {source=sourceId,items=items,players=new[]{baseline}};
                    stage=Stage.Viewed;capture=capturePrefix+"-open";
                }
                if(stage==Stage.Claim && Host && !clicking && !decisionSent)
                {
                    RequireWindow();
                    if(clickDone)
                    {
                        if(claimTransaction==null || !runtime.Session.World.completedTransactions.Contains(claimTransaction)) return;
                        var item=plan.items[claimIndex];
                        var candidate=new WarehouseLowerClaim {item=item.id,definition=item.definition,count=item.count,
                            recipient=actorId,transaction=claimTransaction};
                        if(!observedReservations.TryGetValue(claimTransaction,out var reserved) ||
                            !FactoryRouteLootProof.MatchesReservation(reserved,sourceId,candidate,item))
                            throw new InvalidOperationException("UI receipt не связан с полным recoverable резервом: transaction="+claimTransaction);
                        var proposed=claims.Concat(new[]{candidate}).ToArray();
                        if(!FactoryRouteLootProof.Confirms(runtime.Session.World,sourceId,plan,proposed,out _)) return;
                        if(!FactoryRouteLootProof.Confirms(runtime.Session.ServerWorld,sourceId,plan,proposed,out var reason))
                            throw new InvalidOperationException("Локальная выдача разошлась с каноническим миром: "+reason);
                        claims.Add(candidate);claimIndex++;clickDone=false;claimTransaction=null;
                    }
                    if(claimIndex==plan.items.Length)
                    {
                        plan.claims=claims.ToArray();decisionSent=true;Broadcast(1,StateJson.ToJson(plan));
                    }
                    else
                    {
                        int index=Target.items.FindIndex(i=>i.id==plan.items[claimIndex].id);
                        if(index<0) throw new InvalidOperationException("Стопка исчезла до единственного UI-клика: "+plan.items[claimIndex].id);
                        clicking=true;runtime.StartCoroutine(Click(runtime.Hud.ContainerSlotRect(index)));
                    }
                }
                if(stage==Stage.Granted && !grantSent && capture==null)
                {
                    if(!FactoryRouteLootProof.Confirms(runtime.Session.World,sourceId,plan,claims.ToArray(),out _)) return;
                    // The generic proof binds every participant's exact baseline and definition sums.
                    capture=capturePrefix+"-granted";
                }
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void RequireContext()
        {
            if(runtime.Session.World?.worldId!=worldId || Actor==null || Actor.id!=actorId || Actor.owner!=owner ||
                Actor.owner!=runtime.Session.LocalOwner || Actor.generation!=generation || Actor.lifecycleSequence!=lifecycle ||
                Actor.level!=level || !Actor.connected || runtime.Session.Travel.Pending || !ReferenceEquals(room,runtime.Geometry.Get(level)))
                throw new InvalidOperationException("Изменились мир, игрок, владелец, поколение, локация или геометрия.");
        }
        private void RequireWindow()
        {
            if(!runtime.Hud.ContainerOpen || !runtime.Hud.ContainerWindowRect.gameObject.activeInHierarchy ||
                runtime.Hud.ShownInventorySourceId!=sourceId || runtime.Hud.InventoryOpen)
                throw new InvalidOperationException("UI не показывает выбранный источник: "+sourceId);
        }
        private IEnumerator Click(RectTransform slot)
        {
            var pointer=UiPointerProbe.Click(slot,followTarget:true);
            try
            {
                while(Failure==null)
                {
                    bool next;
                    try {next=pointer.MoveNext();} catch(Exception error) {Fail("UI-клик не завершён: "+error.Message);yield break;}
                    if(!next) break;yield return pointer.Current;
                }
            }
            finally {(pointer as IDisposable)?.Dispose();clicking=false;clickDone=true;}
        }
        internal void ClaimPrepared(string transaction,bool accepted)
        {
            if(stage!=Stage.Claim || !Host || (!clicking && !clickDone) || Failure!=null) return;
            if(claimTransaction!=null && claimTransaction!=transaction) {Fail("При одном UI-клике получены две разные транзакции.");return;}
            if(!accepted) {Fail("Координатор отклонил UI-подбор: transaction="+transaction);return;}
            claimTransaction=transaction;
        }
        internal void CaptureReservations()
        {
            if(stage!=Stage.Claim || !Host || (!clicking && !clickDone) || Failure!=null) return;
            foreach(var grant in runtime.Session.World.pendingGrants)
                if(grant.containerId==sourceId && grant.recipient==actorId && !observedReservations.ContainsKey(grant.transaction))
                    observedReservations.Add(grant.transaction,StateJson.Copy(grant));
        }
        private void SendObserved(int phase,string json=null)=>channel.Send(new FactoryRouteContainerObserved
            {world=worldId,source=sourceId,actor=actorId,generation=generation,lifecycle=lifecycle,phase=phase,json=json});
        private void Broadcast(int phase,string json=null)=>channel.Broadcast(new FactoryRouteContainerDecision
            {world=runtime.Session.ServerWorld.worldId,source=sourceId,phase=phase,json=json});
        internal void Observe(NetworkConnectionToClient connection,FactoryRouteContainerObserved message)
        {
            if(Failure!=null) return;
            try
            {
                var actor=runtime.Session.ConnectedActor(connection);
                var source=runtime.Session.ServerWorld.containers.SingleOrDefault(c=>c.id==sourceId);
                if(actor==null || !actor.connected || actor.id!=message.actor || actor.generation!=message.generation ||
                    actor.lifecycleSequence!=message.lifecycle || source==null || actor.level!=source.level)
                    throw new InvalidOperationException("Барьер получил чужого/изменившегося участника.");
                if(message.phase==0)
                {
                    if(observedViews.TryGetValue(actor.id,out var previous))
                    {if(previous!=message.json) throw new InvalidOperationException("Повторный просмотр изменил исходное свидетельство.");return;}
                    if(serverPhase!=0) throw new InvalidOperationException("Новый просмотр пришёл после начала выдачи.");
                    validate(source);
                    var observed=StateJson.FromJson<WarehouseLowerLootEvidence>(message.json);
                    if(observed?.source!=sourceId || observed.players?.Length!=1 || observed.players[0]?.actor!=actor.id ||
                        observed.players[0].generation!=actor.generation || observed.players[0].counts?.Length!=observed.items?.Length ||
                        observed.players[0].counts.Any(c=>c<0) || !FactoryRouteLootProof.SameItems(observed.items,source.items))
                        throw new InvalidOperationException("Окна читают разные стопки/игрока/исходные суммы.");
                    viewers.Add(actor.id,observed.players[0]);observedViews.Add(actor.id,message.json);
                    if(viewers.Count==runtime.Options.ProbePeers)
                    {
                        var cohort=runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected && a.level==source.level).ToArray();
                        if(cohort.Length!=viewers.Count || cohort.Any(a=>!viewers.ContainsKey(a.id)))
                            throw new InvalidOperationException("Изменился состав группы перед UI-подбором.");
                        var agreed=new WarehouseLowerLootEvidence {source=sourceId,recipient=runtime.Session.LocalActorId,items=observed.items,
                            players=cohort.OrderBy(a=>a.id,StringComparer.Ordinal).Select(a=>viewers[a.id]).ToArray()};
                        serverPhase=1;Broadcast(0,StateJson.ToJson(agreed));
                    }
                }
                else if(message.phase==1)
                {
                    if(granted.Contains(actor.id)) return;
                    if(serverPhase!=1 || plan?.claims?.Length!=plan?.items?.Length ||
                        !FactoryRouteLootProof.Confirms(runtime.Session.ServerWorld,sourceId,plan,claims.ToArray(),out var reason))
                        throw new InvalidOperationException("Общий барьер выдачи не подтверждён: "+(plan==null?"нет плана":Progress));
                    granted.Add(actor.id);
                    if(granted.Count==runtime.Options.ProbePeers) {serverPhase=2;Broadcast(2);}
                }
                else throw new InvalidOperationException("Неизвестный сетевой этап контейнерного маршрута: "+message.phase);
            }
            catch(Exception error) {Fail(error.Message);}
        }
        internal void AcceptDecision(FactoryRouteContainerDecision message)
        {
            if(Failure!=null) return;
            try
            {
                if(decisions.TryGetValue(message.phase,out var previous))
                {if(previous!=message.json) throw new InvalidOperationException("Повторный сетевой этап изменил подтверждение: "+message.phase);return;}
                switch(message.phase)
                {
                    case 0:
                        if(stage!=Stage.Viewed || !viewSent) throw new InvalidOperationException("План пришёл до реального открытия и кадра окна.");
                        var agreed=StateJson.FromJson<WarehouseLowerLootEvidence>(message.json);
                        if(agreed?.source!=sourceId || !FactoryRouteLootProof.SameItems(agreed.items,plan.items) ||
                            agreed.players?.Length!=runtime.Options.ProbePeers || agreed.players.Any(p=>p==null) ||
                            agreed.players.Select(p=>p.actor).Distinct(StringComparer.Ordinal).Count()!=agreed.players.Length ||
                            !agreed.players.Any(p=>p.actor==agreed.recipient) ||
                            agreed.players.Single(p=>p.actor==actorId).generation!=generation ||
                            !agreed.players.Single(p=>p.actor==actorId).counts.SequenceEqual(baseline.counts))
                            throw new InvalidOperationException("План не совпал с источником/исходным инвентарём.");
                        plan=agreed;if(Host) stage=Stage.Claim;break;
                    case 1:
                        if(stage is not (Stage.Claim or Stage.Viewed) || !decisions.ContainsKey(0))
                            throw new InvalidOperationException("Подтверждение выдачи пришло вне UI-этапа.");
                        var finished=StateJson.FromJson<WarehouseLowerLootEvidence>(message.json);
                        if(finished?.source!=sourceId || !FactoryRouteLootProof.SameItems(finished.items,plan.items) ||
                            finished.recipient!=plan.recipient || finished.claims?.Length!=plan.items.Length ||
                            finished.claims.Any(c=>c==null || c.recipient!=plan.recipient) ||
                            StateJson.ToJson(finished.players)!=StateJson.ToJson(plan.players) ||
                            Host && StateJson.ToJson(finished.claims)!=StateJson.ToJson(claims.ToArray()))
                            throw new InvalidOperationException("Неверная полная выдача/получатель/исходные суммы.");
                        claims.Clear();claims.AddRange(finished.claims);plan.claims=finished.claims;stage=Stage.Granted;break;
                    case 2:
                        if(stage!=Stage.Granted || !grantSent) throw new InvalidOperationException("Завершение пришло до локального подтверждения выдачи и кадра.");
                        if(!FactoryRouteLootProof.Confirms(runtime.Session.World,sourceId,plan,claims.ToArray(),out var reason))
                            throw new InvalidOperationException("Выдача изменилась после локального барьера: "+reason);
                        runtime.Hud.CloseContainer();stage=Stage.Complete;channel.Release(this);break;
                    default:throw new InvalidOperationException("Неизвестное решение контейнерного маршрута: "+message.phase);
                }
                decisions.Add(message.phase,message.json);
            }
            catch(Exception error) {Fail(error.Message);}
        }
        public void Capture()
        {
            if(capture==null || Failure!=null) return;
            try
            {
                RequireContext();RequireWindow();
                if(!runtime.LevelPresentationReady || runtime.WorldCamera==null || !runtime.WorldCamera.enabled || runtime.WorldCamera.cullingMask==0) return;
                if(runtime.Options.Smoke.Contains("-frames-"))
                {
                    NetworkFrameCapture.Save(runtime.WorldCamera,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                    NetworkSceneRecord.Save(runtime,210+(int)stage);
                }
                capture=null;
                if(stage==Stage.Viewed && !viewSent) {viewSent=true;SendObserved(0,StateJson.ToJson(plan));}
                else if(stage==Stage.Granted && !grantSent)
                {
                    if(!FactoryRouteLootProof.Confirms(runtime.Session.World,sourceId,plan,claims.ToArray(),out var reason))
                        throw new InvalidOperationException("Кадр выдачи не подтвердил точный мир: "+reason);
                    grantSent=true;SendObserved(1);
                }
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void Fail(string reason)=>failure=label+": "+reason+": "+Progress;
    }
}
