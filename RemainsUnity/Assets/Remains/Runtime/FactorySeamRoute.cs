using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct FactorySeamViewed : NetworkMessage {}
    public struct FactorySeamLoot : NetworkMessage {public string item,definition;public int count;}
    // Only input intents and UI clicks. No position injection or travel call at the room seam.
    internal sealed class FactorySeamRoute
    {
        private readonly PrototypeRuntime runtime;
        private readonly FactoryOfficeRoute office;
        private readonly HashSet<int> viewers=new();
        private int phase,generation,lifecycle,inventoryBefore;
        private string actorId,owner,claimedItem,definition,capture;
        private RoomLayout initialRoom;
        private int quantity;
        private bool jumped,opened,claiming,crossed,shared;
        public bool Complete=>phase==10 && crossed && shared && (office==null || office.Complete);
        public bool OfficeRouteVerified=>office?.Complete==true;
        public bool WarehouseLowerRouteVerified=>office?.WarehouseLowerRouteVerified==true;
        public bool ContinuationRouteVerified=>office?.ContinuationRouteVerified==true;
        public FactoryContinuationRouteEvidence ContinuationEvidence=>office?.ContinuationEvidence;
        public WarehouseLowerClaim[] WarehouseClaims=>office?.WarehouseClaims??Array.Empty<WarehouseLowerClaim>();
        public bool OfficeLowerFloorVerified=>office?.LowerFloorVerified==true;
        public bool OfficeReturnVerified=>office?.ReturnedFromOfficeVerified==true;
        public string OfficeContainer=>office?.TargetId;
        public string OfficeClaimedItem=>office?.ClaimedItem;
        public string OfficeClaimedDefinition=>office?.ClaimedDefinition;
        public int OfficeClaimedCount=>office?.ClaimedCount??0;
        public bool OfficePlatformRepairReady=>office?.PlatformRepairReady==true;
        public bool OfficePlatformCarryVerified=>office?.PlatformCarryVerified==true;
        public float OfficePlatformCarryDistance=>office?.PlatformCarryDistance??0;
        public float OfficePlatformCarryMaximumRelativeError=>office?.PlatformCarryMaximumRelativeError??0;
        public bool BigBoxesVerified {get;private set;}
        public bool WorkshopNpcsVerified {get;private set;}
        public bool LadderLandingVerified {get;private set;}
        public string Progress=>$"seamPhase={phase}, actor={Actor?.id}/{actorId}, owner={Actor?.owner}/{owner}/{runtime.Session.LocalOwner}, generation={Actor?.generation}/{generation}, lifecycle={Actor?.lifecycleSequence}/{lifecycle}, level={Actor?.level}/factory:0, connected={Actor?.connected}, travelPending={runtime.Session.Travel.Pending}, geometrySame={GeometrySame}, position={Actor?.Position}, HP={Actor?.health}, opened={opened}, shared={shared}, claiming={claiming}, item={claimedItem}, {office?.Progress}";
        private bool GeometrySame=>initialRoom!=null && runtime.Geometry.LoadedLevels.Contains("factory:0") && ReferenceEquals(initialRoom,runtime.Geometry.Get("factory:0"));
        public string Failure {get;private set;}
        private ActorState Actor=>runtime.Session.LocalActor;
        private bool Host=>runtime.Options.Role=="host";
        private ContainerState Container=>runtime.Session.World.containers.Single(c=>c.id=="factory:0:original:plant:18:sjN3zN7dP8aCZGe6");
        public FactorySeamRoute(PrototypeRuntime value)
        {
            runtime=value;
            if(value.Options.Smoke.Contains("-office-",StringComparison.Ordinal))
            {
                office=new FactoryOfficeRoute(value);
                office.SelfHealingConfirmed+=(prior,current,transaction)=>
                {
                    if(phase!=11 || lifecycle!=prior || current!=prior+1)
                    {Failure=$"Подтверждённое самолечение не совпало с epoch маршрута: transaction={transaction}, expected={lifecycle}, observed={prior}->{current}, phase={phase}.";return;}
                    lifecycle=current;
                };
            }
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<FactorySeamViewed>((connection,_)=>viewers.Add(connection.connectionId));
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<FactorySeamLoot>(message=>
            {claimedItem=message.item;definition=message.definition;quantity=message.count;});
        }
        public void Begin()
        {
            phase=1;generation=Actor.generation;lifecycle=Actor.lifecycleSequence;actorId=Actor.id;owner=Actor.owner;
            initialRoom=runtime.Geometry.Get("factory:0");
        }
        public PlayerCommand Command()
        {
            if(Failure!=null || Complete) return default;
            if(phase==11) return office.Command();
            if(phase==1) return Move(16.22f);
            if(phase==2) return new PlayerCommand {movement=Vector2.up};
            if(phase==3)
            {
                return Move(40.4f);
            }
            if(phase==4 && !opened)
            {
                var cursor=((InventorySource)Container).Bounds.center;
                var target=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,cursor,runtime.Session.Travel.Points(Actor.level));
                if(target.kind!=InteractionKind.Container || target.id!=Container.id) {Failure="E в цехе выбирает другой объект: "+target.kind;return default;}
                opened=true;return new PlayerCommand {interact=true,hasAim=true,aim=cursor};
            }
            if(phase==7) return runtime.Hud.InventoryOpen?new PlayerCommand {inventory=true}:Move(16.22f);
            if(phase==8) return new PlayerCommand {movement=Vector2.down};
            if(phase==9)
            {
                if(!jumped) {jumped=true;return new PlayerCommand {movement=Vector2.left,jump=true,jumpHeld=true,sprint=true};}
                var command=Move(initialRoom.spawnX);command.sprint=Actor.x>11;return command;
            }
            return default;
        }
        private PlayerCommand Move(float x)=>new() {movement=Vector2.right*(Mathf.Abs(Actor.x-x)>.06f?Mathf.Sign(x-Actor.x):0)};
        public void Tick()
        {
            if(Failure!=null || Complete) return;
            if(Actor==null || Actor.id!=actorId || Actor.owner!=owner || Actor.owner!=runtime.Session.LocalOwner ||
                !Actor.connected || Actor.generation!=generation ||
                Actor.level!="factory:0" || runtime.Session.Travel.Pending || !ReferenceEquals(initialRoom,runtime.Geometry.Get("factory:0")))
            {Failure="Проход между комнатами изменил актёра, управление, поколение, локацию или геометрию: "+Progress;return;}
            if(phase==11)
            {
                bool waiting=office.SynchronizeSelfHealing();
                if(office.Failure!=null) {Failure=office.Failure;return;}
                if(waiting) return;
            }
            if(Actor.lifecycleSequence!=lifecycle)
            {Failure="Проход между комнатами получил неподтверждённое изменение lifecycle: "+Progress;return;}
            if(Actor.down) {Failure="Персонаж потерял сознание на маршруте через стык.";return;}
            if(phase==1 && Mathf.Abs(Actor.x-16.22f)<.08f) phase=2;
            else if(phase==2 && Actor.y>=10.4f-.0001f)
            {
                if(Actor.climbing || !ActorTerrainMotion.Supported(initialRoom,Anatomy.Movement(Actor),Actor.Position))
                {Failure="Верх лестницы не завершился опорой на площадку.";return;}
                LadderLandingVerified=true;phase=3;capture="ladder-top";
            }
            else if(phase==3 && Mathf.Abs(Actor.x-40.4f)<.08f && ActorTerrainMotion.Supported(initialRoom,Anatomy.Movement(Actor),Actor.Position))
            {
                try {FactoryBigBoxObservation.Verify(runtime);BigBoxesVerified=true;FactoryWorkshopNpcObservation.Verify(runtime);WorkshopNpcsVerified=true;}
                catch(InvalidOperationException error) {Failure=error.Message;return;}
                phase=4;crossed=true;capture="workshop";
            }
            else if(phase==4 && runtime.Hud.ContainerOpen)
            {
                if(runtime.Hud.ShownInventorySourceId!=Container.id) {Failure="Окно цеха показывает другой контейнер.";return;}
                shared=true;phase=5;NetworkClient.Send(new FactorySeamViewed());capture="workshop-container";
            }
            else if(phase==5 && Host && viewers.Count==runtime.Options.ProbePeers && !claiming)
            {
                if(Container.items.Count==0) {Failure="В проверочном мире соседний ящик пуст; нужен явный мир для проверки лута.";return;}
                var item=Container.items[0];claimedItem=item.id;definition=item.definition;quantity=item.count;
                inventoryBefore=InventoryJournal.Count(Actor,definition);claiming=true;
                runtime.StartCoroutine(UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0)));phase=6;
            }
            else if(phase==6 && Host && !Container.items.Any(i=>i.id==claimedItem) && !runtime.Session.World.pendingGrants.Any(g=>g.containerId==Container.id))
            {
                if(InventoryJournal.Count(Actor,definition)!=inventoryBefore+quantity) {Failure="Выданный лут цеха не совпал с инвентарём владельца.";return;}
                NetworkServer.SendToAll(new FactorySeamLoot {item=claimedItem,definition=definition,count=quantity});
                runtime.Hud.CloseContainer();AfterWorkshopClaim();
            }
            else if(phase==5 && !Host && claimedItem!=null && !Container.items.Any(i=>i.id==claimedItem))
            {runtime.Hud.CloseContainer();AfterWorkshopClaim();}
            else if(phase==11)
            {
                office.Tick();
                if(office.Failure!=null) {Failure=office.Failure;return;}
                if(office.Complete) phase=7;
            }
            else if(phase==7 && Mathf.Abs(Actor.x-16.22f)<.08f) phase=8;
            // Jump before the unsupported lower end releases the grip. Below that
            // point the actor is already falling behind the entry platform.
            else if(phase==8 && Actor.y<=initialRoom.spawnY+.7f) {phase=9;jumped=false;capture="ladder-return-release";}
            else if(phase==9 && Mathf.Abs(Actor.x-initialRoom.spawnX)<.08f && Mathf.Abs(Actor.y-initialRoom.spawnY)<.03f)
                phase=10;
        }
        public void Capture()
        {
            office?.Capture();
            if(capture==null) return;
            if(runtime.Options.Smoke.Contains("-frames-"))
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>());
                if(capture=="workshop") {FactoryBigBoxObservation.Capture(runtime);FactoryWorkshopNpcObservation.Capture(runtime);}
            }
            capture=null;
        }
        private void AfterWorkshopClaim()
        {
            if(office==null) {phase=7;return;}
            office.Begin();phase=11;
        }
    }
}
