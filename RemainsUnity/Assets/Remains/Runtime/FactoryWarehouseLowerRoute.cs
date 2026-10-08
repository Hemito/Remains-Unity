using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
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

    // Only motion and return coordination. All E/grid/loot barriers use one service.
    internal sealed class FactoryWarehouseLowerRoute
    {
        private enum Stage {None=0,Approach=1,Claims=2,Loop=6,Returned=7,Complete=8,Exit=9}
        private const string Level="factory:0";
        public const string AmmoId=Level+":original:plant:40:bqLt39CNwjwI8aUX";
        private readonly PrototypeRuntime runtime;
        private readonly FactoryRouteContainerClaims ammo;
        private readonly FactoryContinuationRoute continuation;
        private readonly HashSet<string> returned=new(StringComparer.Ordinal);
        private Stage stage;
        private RoomLayout room;
        private WarehouseLowerRouteMotion motion;
        private string actorId,owner,worldId,capture,failure,returnDecision;
        private int generation,lifecycle;
        private bool returnSent,returnAccepted;
        public bool Started=>stage!=Stage.None;
        public bool Complete=>stage==Stage.Complete;
        public string Failure=>failure??ammo.Failure??continuation?.Failure;
        public bool CriticalMotion=>Started && !Complete && (ContinuationActive?continuation.Critical:
            motion.Critical || stage==Stage.Claims);
        public IReadOnlyList<WarehouseLowerClaim> ClaimedStacks=>ammo.Claims;
        public FactoryContinuationRoute Continuation=>continuation;
        public string Progress=>$"warehousePhase={stage}/{motion?.Current}, actor={Actor?.id}, generation={Actor?.generation}/{generation}, life={Actor?.lifecycleSequence}/{lifecycle}, body={Actor?.Position}, velocity={Actor?.Velocity}, climbing={Actor?.climbing}, returns={returned.Count}, loot=[{ammo.Progress}], continuation=[{continuation?.Progress}]";
        private ActorState Actor=>runtime.Session.LocalActor;
        private ContainerState Target=>runtime.Session.World?.containers.SingleOrDefault(c=>c.id==AmmoId);
        private bool ContinuationActive=>continuation?.Started==true && !continuation.Complete;

        // Every source and handler exists before session startup.
        public FactoryWarehouseLowerRoute(PrototypeRuntime value,Func<bool> beforeDestructionReady)
        {
            runtime=value??throw new ArgumentNullException(nameof(value));
            var channel=new FactoryRouteContainerChannel(value);
            ammo=new FactoryRouteContainerClaims(value,channel,AmmoId,"Склад ammo25",ValidateAmmo,"warehouse-ammo");
            if(value.Options.Smoke.Contains("-continuation-")) continuation=new FactoryContinuationRoute(value,channel,beforeDestructionReady);
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<WarehouseLowerObserved>(ObserveReturn);
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<WarehouseLowerDecision>(AcceptReturn);
        }
        private static void ValidateAmmo(ContainerState target)
        {
            if(target==null || target.id!=AmmoId || target.definition!="ammobox" || target.level!=Level || !target.hasPhysicalBody ||
                Mathf.Abs(target.x-117.2f)>.001f || Mathf.Abs(target.y-7.2f)>.001f ||
                Mathf.Abs(target.width-.72f)>.0001f || Mathf.Abs(target.height-.54f)>.0001f || target.anchorHeight!=0)
                throw new InvalidOperationException($"Склад: не совпадает исходный ammo25: id={AmmoId}, definition={target?.definition}, bounds={((InventorySource)target)?.Bounds}.");
            if(target.items.Count!=2 || target.items.Select(i=>i.definition).Distinct(StringComparer.Ordinal).Count()!=2 ||
                !target.items.All(i=>i.count>0 && i.definition is "p10" or "batt"))
                throw new InvalidOperationException("Склад: ammo25 не содержит две настоящие стопки p10/batt; содержимое не будет подменено.");
        }
        public void Begin()
        {
            if(Started) throw new InvalidOperationException("Нижний маршрут склада нельзя запускать повторно.");
            if(Actor==null || runtime.Hud==null || runtime.Options.ProbePeers is <1 or >2)
                throw new InvalidOperationException("Нижний маршрут склада требует HUD и одного/двух участников.");
            actorId=Actor.id;owner=Actor.owner;generation=Actor.generation;lifecycle=Actor.lifecycleSequence;
            worldId=runtime.Session.World.worldId;room=runtime.Geometry.Get(Level);
            if(Actor.level!=Level || WorldRooms.Definition(runtime.Session.World,Level)!=NewGameWorld.FactoryDefinitionId)
                throw new InvalidOperationException("Нижний маршрут склада требует установленный связный завод.");
            ValidateAmmo(Target);motion=new WarehouseLowerRouteMotion(room,Actor);
            runtime.Hud.CloseContainer();stage=Stage.Approach;
            Debug.Log("Начат обычный нижний маршрут склада: "+Progress);
        }
        // The parent forwards only its explicit, journal-confirmed self-heal event.
        public void SynchronizeLifecycle(int previous,int next)
        {
            if(!Started || Complete) return;
            if(lifecycle!=previous || next!=previous+1) {failure="Склад: неверное подтверждённое изменение жизненного цикла.";return;}
            lifecycle=next;ammo.SynchronizeLifecycle(previous,next);continuation?.SynchronizeLifecycle(previous,next);
        }
        public PlayerCommand Command()
        {
            if(!Started || Complete || Failure!=null || runtime.Session.Pause.IsPaused(Level)) return default;
            try
            {
                if(ContinuationActive) return continuation.Command();
                if(stage==Stage.Claims) return ammo.Command();
                return stage is Stage.Approach or Stage.Loop or Stage.Exit?motion.Command(Actor):default;
            }
            catch(Exception error) {failure=error.Message+": "+Progress;return default;}
        }
        public void Tick()
        {
            if(!Started || Complete || Failure!=null) return;
            try
            {
                if(runtime.Session.World?.worldId!=worldId || Actor==null || Actor.id!=actorId || Actor.owner!=owner ||
                    Actor.owner!=runtime.Session.LocalOwner || Actor.generation!=generation || Actor.lifecycleSequence!=lifecycle ||
                    Actor.level!=Level || !Actor.connected || runtime.Session.Travel.Pending || !ReferenceEquals(room,runtime.Geometry.Get(Level)))
                    throw new InvalidOperationException("Склад: изменились мир, игрок, владелец, поколение, локация или геометрия.");
                if(runtime.Session.Pause.IsPaused(Level)) return;
                if(ContinuationActive) {continuation.Tick();return;}
                if(stage==Stage.Approach)
                {
                    motion.Tick(Actor);
                    if(motion.Arrived) {ammo.Begin();stage=Stage.Claims;capture="warehouse-entry";}
                }
                if(stage==Stage.Claims)
                {
                    ammo.Tick();
                    if(ammo.Failure!=null) throw new InvalidOperationException(ammo.Failure);
                    if(ammo.Complete) {motion.BeginLoop(Actor);stage=Stage.Loop;}
                }
                if(stage==Stage.Loop)
                {
                    var previous=motion.Current;motion.Tick(Actor);
                    if(previous!=motion.Current)
                    {
                        Debug.Log("Нижний маршрут склада: "+Progress);
                        if(motion.Current==WarehouseLowerRouteMotion.Phase.L2Up)
                        {
                            capture="warehouse-bottom";
                            if(continuation!=null && !continuation.Started) {continuation.Begin();return;}
                        }
                    }
                    if(motion.Complete) {stage=Stage.Returned;capture="warehouse-lower-return";}
                }
                if(stage==Stage.Returned && !returnSent)
                {
                    if(Target.items.Count!=0 || runtime.Session.World.pendingGrants.Any(g=>g.containerId==AmmoId) ||
                        ammo.Claims.Any(c=>!runtime.Session.World.completedTransactions.Contains(c.transaction)))
                        throw new InvalidOperationException("Склад: источник наполнился повторно или утратил подтверждённые транзакции: "+Progress);
                    returnSent=true;NetworkClient.Send(new WarehouseLowerObserved
                        {actor=actorId,generation=generation,lifecycle=lifecycle,source=AmmoId,phase=2});
                }
                if(stage==Stage.Exit)
                {
                    motion.Tick(Actor);
                    if(motion.OfficeReturned) {stage=Stage.Complete;Debug.Log("Нижний маршрут склада вернулся на опору офиса: "+Progress);}
                }
            }
            catch(Exception error) {failure=error.Message+": "+Progress;}
        }
        // The old channel is now exclusively the existing all-peer return barrier.
        private void ObserveReturn(NetworkConnectionToClient connection,WarehouseLowerObserved message)
        {
            if(Failure!=null) return;
            try
            {
                var actor=runtime.Session.ConnectedActor(connection);
                if(actor==null || !actor.connected || actor.id!=message.actor || actor.generation!=message.generation ||
                    actor.lifecycleSequence!=message.lifecycle || actor.level!=Level || message.source!=AmmoId || message.phase!=2 ||
                    runtime.Session.ServerWorld?.worldId!=worldId || !ammo.Complete)
                    throw new InvalidOperationException("Склад: обратный барьер получил чужого/изменившегося участника или неполную выдачу.");
                if(returned.Contains(actor.id)) return;
                returned.Add(actor.id);
                if(returned.Count==runtime.Options.ProbePeers) NetworkServer.SendToAll(new WarehouseLowerDecision {phase=3});
            }
            catch(Exception error) {failure=error.Message+": "+Progress;}
        }
        private void AcceptReturn(WarehouseLowerDecision message)
        {
            if(Failure!=null) return;
            try
            {
                if(returnAccepted)
                {if(message.phase!=3 || returnDecision!=message.json) throw new InvalidOperationException("Склад: повторный возврат изменил подтверждение.");return;}
                if(message.phase!=3 || stage!=Stage.Returned || !returnSent)
                    throw new InvalidOperationException("Склад: возврат подтверждён вне входной опоры.");
                motion.BeginOfficeReturn(Actor);stage=Stage.Exit;returnAccepted=true;returnDecision=message.json;
            }
            catch(Exception error) {failure=error.Message+": "+Progress;}
        }
        public void Capture()
        {
            ammo.Capture();continuation?.Capture();
            if(capture==null || Failure!=null) return;
            try
            {
                if(!runtime.LevelPresentationReady || runtime.WorldCamera==null || !runtime.WorldCamera.enabled || runtime.WorldCamera.cullingMask==0) return;
                if(runtime.Options.Smoke.Contains("-frames-"))
                {
                    NetworkFrameCapture.Save(runtime.WorldCamera,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                    NetworkSceneRecord.Save(runtime,200+(int)stage);
                }
                capture=null;
            }
            catch(Exception error) {failure=error.Message+": "+Progress;}
        }
    }

    // Read-only evidence. Production SharedInventory performs reservation and durable grant.
    internal static class WarehouseLowerLootProof
    {
        internal static bool MatchesReservation(PendingGrant grant,string source,WarehouseLowerClaim claim)
            =>source==FactoryWarehouseLowerRoute.AmmoId && claim?.definition is "p10" or "batt" &&
                FactoryRouteLootProof.MatchesReservation(grant,source,claim);
        internal static bool SameItems(IEnumerable<ItemStack> first,IEnumerable<ItemStack> second)
            =>FactoryRouteLootProof.SameItems(first,second);
        internal static bool Confirms(WorldState world,WarehouseLowerLootEvidence plan,WarehouseLowerClaim[] claims,out string reason)
        {
            reason="Складской proof требует ammo25 и две целые стопки p10/batt.";
            if(plan?.items?.Length!=2 || plan.items.Any(i=>i==null || i.count<=0 || i.definition is not ("p10" or "batt")) ||
                plan.items.Select(i=>i.definition).Distinct(StringComparer.Ordinal).Count()!=2) return false;
            return FactoryRouteLootProof.Confirms(world,FactoryWarehouseLowerRoute.AmmoId,plan,claims,out reason);
        }
    }
}
