using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct WarehouseLowerObserved : NetworkMessage
    {public string actor,source,json;public int generation,lifecycle,phase;}
    public struct WarehouseLowerDecision : NetworkMessage {public int phase;public string json;}
    [Serializable] public sealed class WarehouseLowerClaim
    {public string item,definition,recipient,transaction;public int count;}
    [Serializable] public sealed class WarehouseLowerBaseline
    {public string actor;public int generation;public int[] counts;}
    [Serializable] public sealed class WarehouseLowerLootEvidence
    {
        public string source,recipient;
        public ItemStack[] items;
        public WarehouseLowerBaseline[] players;
        public WarehouseLowerClaim[] claims;
    }

    // Opt-in route over installed geometry. Never writes player, NPC, cargo or loot state.
    internal sealed class FactoryWarehouseLowerRoute
    {
        private enum Stage { None,Approach,Open,Viewed,Claim,Granted,Loop,Returned,Complete,Exit }
        private const string Level="factory:0";
        public const string AmmoId=Level+":original:plant:40:bqLt39CNwjwI8aUX";
        private readonly PrototypeRuntime runtime;
        private readonly Dictionary<string,WarehouseLowerBaseline> viewers=new(StringComparer.Ordinal);
        private readonly HashSet<string> granted=new(StringComparer.Ordinal),returned=new(StringComparer.Ordinal);
        private readonly List<WarehouseLowerClaim> claims=new();
        private readonly Dictionary<int,string> decisions=new();
        private readonly Dictionary<string,PendingGrant> observedReservations=new(StringComparer.Ordinal);
        private Stage stage;
        private RoomLayout room;
        private WarehouseLowerRouteMotion motion;
        private WarehouseLowerLootEvidence plan;
        private WarehouseLowerBaseline baseline;
        private string actorId,owner,capture,claimTransaction;
        private int generation,lifecycle,serverPhase,claimIndex;
        private bool openSent,viewSent,grantSent,returnSent,decisionSent,clicking,clickDone;
        public bool Started=>stage!=Stage.None;
        public bool Complete=>stage==Stage.Complete;
        public string Failure {get;private set;}
        public bool CriticalMotion=>Started && !Complete && (motion.Critical || stage is Stage.Open or Stage.Viewed or Stage.Claim or Stage.Granted);
        public IReadOnlyList<WarehouseLowerClaim> ClaimedStacks=>claims.AsReadOnly();
        public string Progress=>$"warehousePhase={stage}/{motion?.Current}, actor={Actor?.id}, generation={Actor?.generation}/{generation}, life={Actor?.lifecycleSequence}/{lifecycle}, body={Actor?.Position}, velocity={Actor?.Velocity}, climbing={Actor?.climbing}, source={AmmoId}/{Target?.Position}, UI={runtime.Hud.ContainerOpen}/{runtime.Hud.ShownInventorySourceId}, views={viewers.Count}, grants={granted.Count}, returns={returned.Count}, claim={claimIndex}/{plan?.items?.Length}, tx={claimTransaction}, click={clicking}/{clickDone}";
        private ActorState Actor=>runtime.Session.LocalActor;
        private ContainerState Target=>runtime.Session.World?.containers.SingleOrDefault(c=>c.id==AmmoId);
        private bool Host=>runtime.Options.Role=="host";

        // Construct before session startup so every reliable handler exists before Begin.
        public FactoryWarehouseLowerRoute(PrototypeRuntime value)
        {
            runtime=value??throw new ArgumentNullException(nameof(value));
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<WarehouseLowerObserved>(Observe);
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<WarehouseLowerDecision>(AcceptDecision);
            value.Session.ClaimPrepared+=(transaction,accepted)=>
            {
                if(stage!=Stage.Claim || !Host || (!clicking && !clickDone)) return;
                if(claimTransaction!=null && claimTransaction!=transaction)
                {Failure="Склад: при одном UI-клике получены две разные транзакции.";return;}
                if(!accepted) {Failure="Склад: координатор отклонил UI-подбор: transaction="+transaction;return;}
                claimTransaction=transaction;
            };
            value.Session.Changed+=()=>
            {
                if(stage!=Stage.Claim || !Host || (!clicking && !clickDone)) return;
                // Recovery quorum requires each reservation snapshot before commit.
                // Capture it on that explicit event, not via an invented UI receipt.
                foreach(var grant in value.Session.World.pendingGrants)
                    if(grant.containerId==AmmoId && grant.recipient==actorId && !observedReservations.ContainsKey(grant.transaction))
                        observedReservations.Add(grant.transaction,StateJson.Copy(grant));
            };
        }
        public void Begin()
        {
            if(Started) throw new InvalidOperationException("Нижний маршрут склада нельзя запускать повторно.");
            if(Actor==null || runtime.Hud==null || runtime.Options.ProbePeers is <1 or >2)
                throw new InvalidOperationException("Нижний маршрут склада требует HUD и одного/двух участников.");
            actorId=Actor.id;owner=Actor.owner;generation=Actor.generation;lifecycle=Actor.lifecycleSequence;
            room=runtime.Geometry.Get(Level);
            if(Actor.level!=Level || WorldRooms.Definition(runtime.Session.World,Level)!=NewGameWorld.FactoryDefinitionId)
                throw new InvalidOperationException("Нижний маршрут склада требует установленный связный завод.");
            if(Target==null || Target.definition!="ammobox" || Target.level!=Level || !Target.hasPhysicalBody ||
                Mathf.Abs(Target.x-117.2f)>.001f || Mathf.Abs(Target.y-7.2f)>.001f ||
                Mathf.Abs(Target.width-.72f)>.0001f || Mathf.Abs(Target.height-.54f)>.0001f || Target.anchorHeight!=0)
                throw new InvalidOperationException($"Склад: не совпадает исходный ammo25: id={AmmoId}, definition={Target?.definition}, bounds={((InventorySource)Target)?.Bounds}.");
            motion=new WarehouseLowerRouteMotion(room,Actor);
            runtime.Hud.CloseContainer();stage=Stage.Approach;
            Debug.Log("Начат обычный нижний маршрут склада: "+Progress);
        }
        // The parent forwards only its explicit, journal-confirmed self-heal event.
        public void SynchronizeLifecycle(int previous,int next)
        {
            if(!Started || Complete) return;
            if(lifecycle!=previous || next!=previous+1) {Failure="Склад: неверное подтверждённое изменение жизненного цикла.";return;}
            lifecycle=next;
        }
        public PlayerCommand Command()
        {
            if(!Started || Complete || Failure!=null || runtime.Session.Pause.IsPaused(Level)) return default;
            try
            {
                if(stage is Stage.Approach or Stage.Loop or Stage.Exit) return motion.Command(Actor);
                if(stage!=Stage.Open || openSent) return default;
                var aim=((InventorySource)Target).Bounds.center;
                var selected=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,aim,
                    runtime.Session.Travel.Points(Level),runtime.MechanismInteraction.Points);
                if(selected.kind!=InteractionKind.Container || selected.id!=AmmoId)
                    throw new InvalidOperationException($"Склад: E выбрал {selected.kind}/{selected.id}, ожидался {AmmoId}, cursor={aim}.");
                openSent=true;return new PlayerCommand {interact=true,hasAim=true,aim=aim};
            }
            catch(Exception error) {Failure=error.Message+": "+Progress;return default;}
        }
        public void Tick()
        {
            if(!Started || Complete || Failure!=null) return;
            try
            {
                // The parent owns health/healing checks. This helper guards identity and geometry only.
                if(Actor==null || Actor.id!=actorId || Actor.owner!=owner || Actor.owner!=runtime.Session.LocalOwner ||
                    Actor.generation!=generation || Actor.lifecycleSequence!=lifecycle || Actor.level!=Level ||
                    !Actor.connected || runtime.Session.Travel.Pending || !ReferenceEquals(room,runtime.Geometry.Get(Level)))
                    throw new InvalidOperationException("Склад: изменились игрок, владелец, поколение, локация или геометрия.");
                if(runtime.Session.Pause.IsPaused(Level)) return;
                if(stage==Stage.Approach)
                {
                    motion.Tick(Actor);
                    if(motion.Arrived) {stage=Stage.Open;capture="warehouse-entry";}
                }
                if(stage==Stage.Open && runtime.Hud.ContainerOpen && runtime.Hud.ContainerWindowRect.gameObject.activeInHierarchy &&
                    !runtime.Combat.Reload.Active)
                {
                    RequireWindow();
                    if(!viewSent)
                    {
                        var items=Target.items.OrderBy(i=>i.id,StringComparer.Ordinal).Select(i=>i.Copy()).ToArray();
                        if(items.Length!=2 || items.Select(i=>i.definition).Distinct().Count()!=2 ||
                            !items.All(i=>i.count>0 && i.definition is "p10" or "batt"))
                            throw new InvalidOperationException("Склад: ammo25 не содержит две настоящие стопки p10/batt; содержимое не будет подменено.");
                        plan=new WarehouseLowerLootEvidence {source=AmmoId,items=items};
                        baseline=new WarehouseLowerBaseline {actor=actorId,generation=generation,
                            counts=items.Select(i=>InventoryJournal.Count(Actor,i.definition)).ToArray()};
                        plan.players=new[]{baseline};viewSent=true;stage=Stage.Viewed;capture="warehouse-ammo-open";
                        SendObserved(0,StateJson.ToJson(plan));
                    }
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
                            !WarehouseLowerLootProof.MatchesReservation(reserved,AmmoId,candidate))
                            throw new InvalidOperationException("Склад: UI receipt не связан с точным recoverable резервом: transaction="+claimTransaction);
                        var proposed=claims.Concat(new[]{candidate}).ToArray();
                        if(!WarehouseLowerLootProof.Confirms(runtime.Session.World,plan,proposed,out _)) return;
                        if(!WarehouseLowerLootProof.Confirms(runtime.Session.ServerWorld,plan,proposed,out var canonicalReason))
                            throw new InvalidOperationException("Склад: клиент подтвердил выдачу, канонический мир разошёлся: "+canonicalReason);
                        claims.Add(candidate);claimIndex++;clickDone=false;claimTransaction=null;
                    }
                    if(claimIndex==plan.items.Length)
                    {
                        plan.claims=claims.ToArray();decisionSent=true;
                        NetworkServer.SendToAll(new WarehouseLowerDecision {phase=1,json=StateJson.ToJson(plan)});
                    }
                    else
                    {
                        int index=Target.items.FindIndex(i=>i.id==plan.items[claimIndex].id);
                        if(index<0) throw new InvalidOperationException("Склад: стопка исчезла до единственного UI-клика: "+plan.items[claimIndex].id);
                        clicking=true;runtime.StartCoroutine(Click(runtime.Hud.ContainerSlotRect(index)));
                    }
                }
                if(stage==Stage.Granted && !grantSent)
                {
                    if(!WarehouseLowerLootProof.Confirms(runtime.Session.World,plan,claims.ToArray(),out _)) return;
                    if(InventoryJournal.Count(Actor,plan.items[0].definition)!=baseline.counts[0]+
                        claims.Where(c=>c.recipient==actorId && c.definition==plan.items[0].definition).Sum(c=>c.count) ||
                        InventoryJournal.Count(Actor,plan.items[1].definition)!=baseline.counts[1]+
                        claims.Where(c=>c.recipient==actorId && c.definition==plan.items[1].definition).Sum(c=>c.count))
                        throw new InvalidOperationException("Склад: локальный инвентарь не подтвердил явного получателя целых стопок.");
                    grantSent=true;SendObserved(1);capture="warehouse-ammo-granted";
                }
                if(stage==Stage.Loop)
                {
                    var previous=motion.Current;motion.Tick(Actor);
                    if(previous!=motion.Current)
                    {
                        Debug.Log("Нижний маршрут склада: "+Progress);
                        if(motion.Current==WarehouseLowerRouteMotion.Phase.L2Up) capture="warehouse-bottom";
                    }
                    if(motion.Complete) {stage=Stage.Returned;capture="warehouse-lower-return";}
                }
                if(stage==Stage.Returned && !returnSent)
                {
                    if(Target.items.Count!=0 || runtime.Session.World.pendingGrants.Any(g=>g.containerId==AmmoId) ||
                        claims.Any(c=>!runtime.Session.World.completedTransactions.Contains(c.transaction)))
                        throw new InvalidOperationException("Склад: источник наполнился повторно или утратил подтверждённые транзакции.");
                    returnSent=true;SendObserved(2);
                }
                if(stage==Stage.Exit)
                {
                    motion.Tick(Actor);
                    if(motion.OfficeReturned) {stage=Stage.Complete;Debug.Log("Нижний маршрут склада вернулся на опору офиса: "+Progress);}
                }
            }
            catch(Exception error) {Failure=error.Message+": "+Progress;}
        }
        private void RequireWindow()
        {
            if(!runtime.Hud.ContainerOpen || !runtime.Hud.ContainerWindowRect.gameObject.activeInHierarchy ||
                runtime.Hud.ShownInventorySourceId!=AmmoId || runtime.Hud.InventoryOpen)
                throw new InvalidOperationException("Склад: UI не показывает выбранный ammo25.");
        }
        private IEnumerator Click(RectTransform slot)
        {
            var pointer=UiPointerProbe.Click(slot,followTarget:true);
            try
            {
                while(Failure==null)
                {
                    bool next;
                    try {next=pointer.MoveNext();} catch(Exception error) {Failure="Склад: UI-клик не завершён: "+error.Message;yield break;}
                    if(!next) break;yield return pointer.Current;
                }
            }
            finally {(pointer as IDisposable)?.Dispose();clicking=false;clickDone=true;}
        }
        private void SendObserved(int phase,string json=null)=>NetworkClient.Send(new WarehouseLowerObserved
            {actor=actorId,source=AmmoId,generation=generation,lifecycle=lifecycle,phase=phase,json=json});
        private void Observe(NetworkConnectionToClient connection,WarehouseLowerObserved message)
        {
            if(Failure!=null || message.phase!=serverPhase) return;
            try
            {
                var actor=runtime.Session.ConnectedActor(connection);
                if(actor==null || actor.id!=message.actor || actor.generation!=message.generation ||
                    actor.lifecycleSequence!=message.lifecycle || actor.level!=Level || message.source!=AmmoId)
                    throw new InvalidOperationException("Склад: барьер не соответствует текущему игроку/источнику.");
                if(message.phase==0)
                {
                    var observed=StateJson.FromJson<WarehouseLowerLootEvidence>(message.json);
                    if(observed.source!=AmmoId || observed.players.Length!=1 || observed.players[0].actor!=actor.id ||
                        observed.players[0].generation!=actor.generation || !WarehouseLowerLootProof.SameItems(observed.items,Target.items))
                        throw new InvalidOperationException("Склад: два окна читают разные стопки/игрока.");
                    viewers[actor.id]=observed.players[0];
                    if(viewers.Count==runtime.Options.ProbePeers)
                    {
                        var cohort=runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected && a.level==Level).ToArray();
                        if(cohort.Length!=viewers.Count || cohort.Any(a=>!viewers.ContainsKey(a.id)))
                            throw new InvalidOperationException("Склад: изменился состав группы перед UI-подбором.");
                        var agreed=new WarehouseLowerLootEvidence {source=AmmoId,recipient=runtime.Session.LocalActorId,items=observed.items,
                            players=cohort.OrderBy(a=>a.id,StringComparer.Ordinal).Select(a=>viewers[a.id]).ToArray()};
                        serverPhase=1;NetworkServer.SendToAll(new WarehouseLowerDecision {phase=0,json=StateJson.ToJson(agreed)});
                    }
                }
                else if(message.phase==1)
                {
                    granted.Add(actor.id);
                    if(granted.Count==runtime.Options.ProbePeers)
                    {
                        if(!WarehouseLowerLootProof.Confirms(runtime.Session.ServerWorld,plan,claims.ToArray(),out var reason))
                            throw new InvalidOperationException("Склад: общий барьер выдачи не подтверждён: "+reason);
                        serverPhase=2;NetworkServer.SendToAll(new WarehouseLowerDecision {phase=2});
                    }
                }
                else if(message.phase==2)
                {
                    returned.Add(actor.id);
                    if(returned.Count==runtime.Options.ProbePeers) {serverPhase=3;NetworkServer.SendToAll(new WarehouseLowerDecision {phase=3});}
                }
            }
            catch(Exception error) {Failure=error.Message+": "+Progress;}
        }
        private void AcceptDecision(WarehouseLowerDecision message)
        {
            if(Failure!=null) return;
            try
            {
                if(decisions.TryGetValue(message.phase,out var previous))
                {
                    if(previous!=message.json) throw new InvalidOperationException("Склад: повторный сетевой этап изменил подтверждение: "+message.phase);
                    return;
                }
                switch(message.phase)
                {
                    case 0:
                        if(stage!=Stage.Viewed) throw new InvalidOperationException("Склад: план выдачи пришёл до реального открытия окна.");
                        var agreed=StateJson.FromJson<WarehouseLowerLootEvidence>(message.json);
                        if(agreed.source!=AmmoId || !WarehouseLowerLootProof.SameItems(agreed.items,plan.items) ||
                            agreed.players.Length!=runtime.Options.ProbePeers || !agreed.players.Any(p=>p.actor==agreed.recipient) ||
                            agreed.players.Single(p=>p.actor==actorId).generation!=generation ||
                            !agreed.players.Single(p=>p.actor==actorId).counts.SequenceEqual(baseline.counts))
                            throw new InvalidOperationException("Склад: план не совпал с локальным источником/исходным инвентарём.");
                        plan=agreed;if(Host) stage=Stage.Claim;break;
                    case 1:
                        if(stage is not (Stage.Claim or Stage.Viewed) || decisionSent && !Host)
                            throw new InvalidOperationException("Склад: подтверждение выдачи пришло вне UI-этапа.");
                        var finished=StateJson.FromJson<WarehouseLowerLootEvidence>(message.json);
                        if(finished.source!=AmmoId || !WarehouseLowerLootProof.SameItems(finished.items,plan.items) ||
                            finished.recipient!=plan.recipient || finished.claims.Length!=plan.items.Length ||
                            finished.claims.Any(c=>c.recipient!=plan.recipient))
                            throw new InvalidOperationException("Склад: неверная полная выдача/получатель.");
                        claims.Clear();claims.AddRange(finished.claims);plan.claims=finished.claims;stage=Stage.Granted;break;
                    case 2:
                        if(stage!=Stage.Granted || !grantSent) throw new InvalidOperationException("Склад: петля началась до подтверждения локальной выдачи.");
                        runtime.Hud.CloseContainer();motion.BeginLoop(Actor);stage=Stage.Loop;break;
                    case 3:
                        if(stage!=Stage.Returned || !returnSent) throw new InvalidOperationException("Склад: возврат подтверждён вне входной опоры.");
                        motion.BeginOfficeReturn(Actor);stage=Stage.Exit;break;
                    default:throw new InvalidOperationException("Склад: неизвестный сетевой этап: "+message.phase);
                }
                decisions.Add(message.phase,message.json);
            }
            catch(Exception error) {Failure=error.Message+": "+Progress;}
        }
        public void Capture()
        {
            if(capture==null) return;
            if(runtime.Options.Smoke.Contains("-frames-"))
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                NetworkSceneRecord.Save(runtime,200+(int)stage);
            }
            capture=null;
        }
    }

    // Read-only evidence. Production SharedInventory performs reservation and durable grant.
    internal static class WarehouseLowerLootProof
    {
        internal static bool MatchesReservation(PendingGrant grant,string source,WarehouseLowerClaim claim)=>grant!=null && claim!=null &&
            grant.transaction==claim.transaction && grant.containerId==source && grant.recipient==claim.recipient &&
            grant.item?.id==claim.item && grant.item.definition==claim.definition && grant.item.count==claim.count;
        internal static bool SameItems(IEnumerable<ItemStack> first,IEnumerable<ItemStack> second)
        {
            if(first==null || second==null) return false;
            var a=first.OrderBy(i=>i.id,StringComparer.Ordinal).ToArray();var b=second.OrderBy(i=>i.id,StringComparer.Ordinal).ToArray();
            return a.Length==b.Length && a.Select((i,n)=>i.id==b[n].id && i.count==b[n].count && InventoryStacks.Compatible(i,b[n])).All(v=>v);
        }
        internal static bool Confirms(WorldState world,WarehouseLowerLootEvidence plan,WarehouseLowerClaim[] claims,out string reason)
        {
            reason="Нет полного наблюдения мира/выдачи.";
            if(world==null || plan?.items==null || plan.players==null || claims==null || plan.source!=FactoryWarehouseLowerRoute.AmmoId ||
                plan.items.Select(i=>i.id).Distinct().Count()!=plan.items.Length ||
                plan.players.Select(p=>p.actor).Distinct().Count()!=plan.players.Length) return false;
            var source=world.containers.SingleOrDefault(c=>c.id==plan.source);
            if(source==null || world.pendingGrants.Any(g=>g.containerId==plan.source)) {reason="Источник отсутствует или выдача ещё зарезервирована.";return false;}
            if(claims.Select(c=>c.item).Distinct().Count()!=claims.Length || claims.Select(c=>c.transaction).Distinct().Count()!=claims.Length)
            {reason="Повторный ID стопки или транзакции.";return false;}
            foreach(var claim in claims)
            {
                var item=plan.items.SingleOrDefault(i=>i.id==claim.item);
                if(item==null || claim.definition!=item.definition || claim.count!=item.count || string.IsNullOrEmpty(claim.transaction) ||
                    !world.completedTransactions.Contains(claim.transaction) || !plan.players.Any(p=>p.actor==claim.recipient))
                {reason="Нет точного подтверждения целой стопки: item="+claim.item;return false;}
                var recipients=plan.players.Where(p=>InventoryStacks.Find(world.Find(p.actor),claim.item)!=null).ToArray();
                if(recipients.Length!=1 || recipients[0].actor!=claim.recipient)
                {reason="Стопка не имеет единственного явного получателя: item="+claim.item;return false;}
                if(!InventoryStacks.Compatible(item,InventoryStacks.Find(world.Find(claim.recipient),claim.item)))
                {reason="Полученная стопка изменила определение/расширения: item="+claim.item;return false;}
            }
            if(!SameItems(source.items,plan.items.Where(i=>!claims.Any(c=>c.item==i.id))))
            {reason="Содержимое источника не соответствует удалённым целым стопкам.";return false;}
            foreach(var player in plan.players)
            {
                var actor=world.Find(player.actor);
                if(actor==null || actor.generation!=player.generation || player.counts?.Length!=plan.items.Length)
                {reason="Изменилась личность/поколение участника: actor="+player.actor;return false;}
                for(int i=0;i<plan.items.Length;i++)
                {
                    var definition=plan.items[i].definition;
                    int expected=player.counts[i]+claims.Where(c=>c.recipient==player.actor && c.definition==definition).Sum(c=>c.count);
                    if(InventoryJournal.Count(actor,definition)!=expected)
                    {reason=$"Не совпала подтверждённая сумма: actor={player.actor}, definition={definition}, expected={expected}.";return false;}
                }
            }
            reason=null;return true;
        }
    }
}
