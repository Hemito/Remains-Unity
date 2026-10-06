using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct OfficeRouteViewed : NetworkMessage
    {public string actor,source;public int generation,lifecycle,barrier;public bool returned;}
    public struct OfficeRouteLoot : NetworkMessage
    {public string source,item,definition,recipient;public int count,barrier;public bool returned;}

    // Bounded scenario over saved 18/53 geometry. Only ordinary player intents and
    // container UI clicks; no actor/world writes, debug travel or NPC fixture changes.
    internal sealed class FactoryOfficeRoute
    {
        private enum Stage
        {
            None,WorkshopLeftApproach,WorkshopLeftUp,
            WorkshopLeaveShelf,WorkshopBottom,WorkshopRightUp,EastGapApproach,EastGap,
            OfficeEntry,OfficeDrop,OfficeLadderApproach,OfficeDown,OfficeTarget,Open,Viewed,Claim,
            OfficeReturnBottom,OfficeUp,OfficeUnderShelf,OfficeJump,ReturnSeam,
            WestGapApproach,WestGap,WorkshopRightApproach,RightBank,RightRepair,CallPlatform,WaitPlatform,
            BoardPlatform,BoardBarrier,ReturnPlatform,RidePlatform,LeftGapApproach,LeftGap,ReturnStart,ReturnBarrier,Complete
        }
        private const string Level="factory:0";
        private const string PlatformId=Level+":original:plant:18:TFAERLMCSehXRcAt";
        private const string RightButtonId=Level+":original:plant:18:MlUHuFSMyaGaDCyi";
        private const float CarryTolerance=.3f;
        public string TargetId=>Level+":original:plant:53:Bf6ZE9xBKE8ABZ5b";
        private readonly PrototypeRuntime runtime;
        private readonly HashSet<string> viewers=new(StringComparer.Ordinal),returners=new(StringComparer.Ordinal),shots=new(StringComparer.Ordinal);
        private readonly HashSet<string> defeated=new(StringComparer.Ordinal),fought=new(StringComparer.Ordinal);
        private readonly HashSet<string> bankWaiters=new(StringComparer.Ordinal),boarders=new(StringComparer.Ordinal);
        private readonly HashSet<string> healedTransactions=new(StringComparer.Ordinal);
        private Stage stage;
        private RoomLayout room;
        private string actorId,owner,capture,combatTarget,claimRecipient,requestedDoor;
        private int generation,lifecycle,inventoryBefore,downPulse;
        private bool jumped,opened,viewSent,claiming,returnSent,returnBroadcast,returnAcknowledged,ammunitionWarned;
        private bool bankBroadcast,boardBroadcast,bankAcknowledged,boardAcknowledged,mechanismPressed,boardSent,rideCaptured;
        private float nextHeal,nextTrace,doorRequestedAt,boardedAt=-1,boardX,boardY,boardRelative;
        private bool healingPending,healingSuccess,combatMoves;
        private int healingLifecycle,healingInventory,projectileContacts;
        private string healingRequest,healingReply,lastProjectileContact;
        private float healingDeadline;
        private readonly List<DepartureFrame> firstDepartureFrames=new();
        private readonly Queue<DepartureFrame> recentDepartureFrames=new();
        private RoomSurface[] boardingFloorCandidates=Array.Empty<RoomSurface>();
        private DepartureFrame boardingObservation,firstMovingObservation,lastDepartureFrame;
        public event Action<int,int,string> SelfHealingConfirmed;
        public string ClaimedItem {get;private set;}
        public string ClaimedDefinition {get;private set;}
        public int ClaimedCount {get;private set;}
        public bool LowerFloorVerified {get;private set;}
        public bool ReturnedFromOfficeVerified {get;private set;}
        public bool PlatformRepairReady {get;private set;}
        public bool PlatformCarryVerified {get;private set;}
        public float PlatformCarryDistance {get;private set;}
        public float PlatformCarryMaximumRelativeError {get;private set;}
        public int CombatShots=>shots.Count;
        public int CombatDefeats=>defeated.Count;
        public int ConfirmedHealingCount=>healedTransactions.Count;
        public bool Complete=>stage==Stage.Complete;
        public string Failure {get;private set;}
        public string Progress=>$"officePhase={stage}, actor={Actor?.id}, owner={Actor?.owner}/{owner}, generation={Actor?.generation}/{generation}, lifecycle={Actor?.lifecycleSequence}/{lifecycle}, level={Actor?.level}/{Level}, geometrySame={GeometrySame}, position={Actor?.Position}, velocity={Actor?.Velocity}, climbing={Actor?.climbing}, crouched={Actor?.crouched}, HP={Actor?.health}, lower={LowerFloorVerified}, returned={ReturnedFromOfficeVerified}, source={TargetId}, item={ClaimedItem}, views={viewers.Count}, returns={returners.Count}, bank={bankWaiters.Count}, board={boarders.Count}, platform={PlatformId}/{Platform?.endpoint}/{Platform?.segment?.sequence}, repair={RightButton?.repairState}/{runtime.MechanismInteraction.Progress}/{runtime.MechanismInteraction.Pending}, carry={PlatformCarryDistance}/{PlatformCarryMaximumRelativeError}/{PlatformCarryVerified}, combat={combatTarget}/{runtime.Session.World?.Find(combatTarget)?.health}, combatMoves={combatMoves}, ammunition={MagazineRounds}/{ReserveRounds}, door={requestedDoor}, shots={CombatShots}, contacts={projectileContacts}/{lastProjectileContact}, defeats={CombatDefeats}, healing={healingPending}/{healingReply}/{ConfirmedHealingCount}";
        private int MagazineRounds=>Actor?.inventory.FirstOrDefault(i=>i.definition=="pistol")?.weaponState?.rounds??-1;
        private int ReserveRounds=>Actor==null?-1:InventoryJournal.Count(Actor,"p10");
        private bool GeometrySame=>room!=null && runtime.Geometry.LoadedLevels.Contains(Level) && ReferenceEquals(room,runtime.Geometry.Get(Level));
        private ActorState Actor=>runtime.Session.LocalActor;
        private bool Host=>runtime.Options.Role=="host";
        private ContainerState Container=>runtime.Session.World.containers.SingleOrDefault(c=>c.id==TargetId);
        private MovingPlatformState Platform=>runtime.Session.World?.movingPlatforms.SingleOrDefault(p=>p.id==PlatformId);
        private MechanismButtonState RightButton=>runtime.Session.World?.mechanismButtons.SingleOrDefault(b=>b.id==RightButtonId);

        // Construct before session startup, like FactorySeamRoute: registrations must
        // precede ServerReady/ClientReady even though Begin runs after workshop loot.
        public FactoryOfficeRoute(PrototypeRuntime value)
        {
            runtime=value;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<OfficeRouteViewed>((connection,message)=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                if(actor==null || actor.id!=message.actor || actor.generation!=message.generation ||
                    actor.lifecycleSequence!=message.lifecycle || actor.level!=Level || message.source!=TargetId) return;
                if(message.barrier==1) bankWaiters.Add(actor.id);
                else if(message.barrier==2) boarders.Add(actor.id);
                else if(message.barrier==0) (message.returned?returners:viewers).Add(actor.id);
            });
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<OfficeRouteLoot>(AcceptLoot);
            value.Session.AttackReceived+=attack=>
            {if(stage!=Stage.None && attack.attacker==actorId && attack.weapon=="pistol") shots.Add(attack.id);};
            value.Combat.Projectiles.ContactObserved+=contact=>
            {
                if(!shots.Contains(contact.projectile)) return;
                projectileContacts++;lastProjectileContact=contact.target??contact.surfaceTexture;
                Debug.Log($"Обычный маршрут офиса: контакт своего снаряда={contact.projectile}, target={contact.target}, surface={contact.surfaceTexture}, point={contact.position}, phase={stage}.");
            };
            value.Session.SelfHealingRequested+=transaction=>
            {
                if(!healingPending) return;
                if(healingRequest!=null && healingRequest!=transaction) {Failure="На маршруте отправлены два лечения при одном собственном intent.";return;}
                healingRequest=transaction;
            };
            value.Session.HealingFinished+=(transaction,success)=>
            {
                if(healedTransactions.Contains(transaction) || !healingPending || transaction!=healingRequest) return;
                healingReply=transaction;healingSuccess=success;
            };
        }

        public void Begin()
        {
            if(stage!=Stage.None) throw new InvalidOperationException("Обычный маршрут офиса нельзя запустить повторно.");
            if(Actor==null || runtime.Options.ProbePeers is <1 or >2 || runtime.Hud==null)
                throw new InvalidOperationException("Маршрут офиса требует локального игрока, HUD и одного/двух участников.");
            actorId=Actor.id;owner=Actor.owner;generation=Actor.generation;lifecycle=Actor.lifecycleSequence;
            room=runtime.Geometry.Get(Level);
            if(Actor.level!=Level || WorldRooms.Definition(runtime.Session.World,Level)!=NewGameWorld.FactoryDefinitionId ||
                !At(40.4f,10.4f)) throw new InvalidOperationException("Маршрут офиса начинается на настоящей опоре цеха (40,4;10,4).");
            if(Container==null || Container.definition!="explbox")
                throw new InvalidOperationException("В сохранённом офисе нет исходного explbox38: "+TargetId);
            RequireLadder(43.42f,7.2f,10.4f);
            RequireLadder(67.42f,.8f,10.4f);RequireLadder(90.62f,.8f,7.2f);
            if(Platform==null || RightButton==null || Platform.endpoint!=0 || Platform.segment!=null || !SampleAtEndpoint(0,out _) ||
                RightButton.repairState!=MechanismRepairStates.NeedsRepair)
                throw new InvalidOperationException("Маршрут требует исходную неподвижную platform2 слева и ремонтируемую правую knop3.");
            foreach(var point in new[] {new Vector2(51.1f,.8f),new Vector2(71.3f,10.4f),
                new Vector2(74,10.4f),new Vector2(84,10.4f),new Vector2(84,7.2f),new Vector2(78.8f,.8f)})
                if(!ActorTerrainMotion.IsClear(room,Anatomy.Movement(Actor.anatomy),point) ||
                    !ActorTerrainMotion.Supported(room,Anatomy.Movement(Actor.anatomy),point))
                    throw new InvalidOperationException("Маршрут офиса потерял объявленную опору: "+point);
            runtime.Hud.CloseContainer();Set(Stage.WorkshopLeftApproach);
        }

        private void RequireLadder(float x,float bottom,float top)
        {
            if(room.ladders.Count(l=>Mathf.Abs(l.x+l.movementOffset-x)<.001f &&
                Mathf.Abs(l.bottom-bottom)<.001f && Mathf.Abs(l.top-top)<.001f)!=1)
                throw new InvalidOperationException($"Маршрут офиса: нет единственной лестницы actorX={x}, bottom={bottom}, top={top}.");
        }
        private bool AtX(float x)=>Mathf.Abs(Actor.x-x)<.08f;
        private bool At(float x,float y)=>AtX(x) && Landed(y);
        // Contact tolerance is also true while rising through a one-way shelf.
        // Advance the route only after the controller actually settles on it;
        // releasing jump at a merely nearby height can cancel the ascent.
        private bool Landed(float y)=>Mathf.Abs(Actor.y-y)<.0002f && Mathf.Abs(Actor.vy)<.001f && !Actor.climbing &&
            ActorTerrainMotion.Supported(room,Anatomy.Movement(Actor),Actor.Position);
        private PlayerCommand Move(float x)=>new() {movement=Vector2.right*(Mathf.Abs(Actor.x-x)>.06f?Mathf.Sign(x-Actor.x):0)};
        private PlayerCommand Jump(Vector2 movement,bool hold)
        {bool edge=!jumped;jumped=true;return new PlayerCommand {movement=movement,jump=edge,jumpHeld=hold};}
        private void Set(Stage next,string frame=null)
        {stage=next;jumped=false;mechanismPressed=false;downPulse=0;if(frame!=null) capture=frame;}

        public PlayerCommand Command()
        {
            if(stage==Stage.None || Failure!=null || Complete || Actor==null) return default;
            try
            {
                if(SynchronizeSelfHealing() || Failure!=null) return default;
                if(runtime.Session.Pause.IsPaused(Level)) return default;
                if(runtime.Hud.InventoryOpen) return new PlayerCommand {inventory=true};
                PlayerCommand command;
                // Do not cut the short jump's hold or double-S gesture to fight. The
                // normal controller completes those motions; combat resumes on landing.
                bool critical=stage is Stage.EastGap or Stage.WestGap or Stage.LeftGap or
                    Stage.OfficeJump or Stage.OfficeDrop or Stage.CallPlatform or Stage.WaitPlatform or
                    Stage.BoardPlatform or Stage.BoardBarrier or Stage.ReturnPlatform or Stage.RidePlatform;
                if(!critical && stage is not (Stage.Viewed or Stage.Claim or Stage.ReturnBarrier) &&
                    FindThreat() is { } npc)
                {
                    var pistol=Actor.inventory.FirstOrDefault(i=>i.definition=="pistol" && i.weaponState!=null && InventoryAvailability.Count(Actor,i)>0);
                    if(pistol==null) throw new InvalidOperationException("На обычном маршруте нет собственного пистолета: "+Actor.id);
                    var type=WeaponCatalog.Get("pistol");
                    bool hasAmmunition=pistol.weaponState.rounds>0 || InventoryJournal.Count(Actor,type.ammo)>0 || runtime.Combat.Reload.Active;
                    var crowbar=Actor.inventory.FirstOrDefault(i=>i.definition=="crowbar" && i.weaponState!=null && InventoryAvailability.Count(Actor,i)>0);
                    bool melee=!hasAmmunition && crowbar!=null && Vector2.Distance(npc.Position,Actor.Position)<WeaponCatalog.Get("crowbar").range &&
                        !runtime.Session.World.actors.Any(a=>a.player && a.id!=actorId && a.level==Level &&
                            Vector2.Distance(a.Position,Actor.Position)<WeaponCatalog.Get("crowbar").range+Anatomy.Movement(a).width);
                    if(!hasAmmunition && !ammunitionWarned)
                    {
                        ammunitionWarned=true;
                        Debug.LogWarning($"Обычный маршрут офиса: actor={actorId}, target={npc.id}, патроны пистолета закончились; используются настоящий лом вблизи или продолжение пути. Запасы не изменяются.");
                    }
                    if(hasAmmunition || melee)
                    {
                        combatTarget=npc.id;fought.Add(npc.id);
                        var weapon=melee?crowbar:pistol;
                        var aim=npc.Position+Vector2.up*(Anatomy.Movement(npc).height*.5f);
                        combatMoves=MovingCombatStage();
                        command=combatMoves?RouteCommand():default;
                        command.weaponItem=Actor.equippedWeaponItem==weapon.id?null:weapon.id;
                        command.hasAim=true;command.aim=aim;command.reload=!melee && pistol.weaponState.rounds==0;
                        // First send selection/aim, then inspect the actual previous
                        // muzzle. A conservative ally corridor is a probe input choice;
                        // friendly-fire production settings remain enabled.
                        command.fire=Actor.equippedWeaponItem==weapon.id &&
                            (melee || pistol.weaponState.rounds>0 && ShotClearOfPlayers(aim));
                    }
                    else {combatTarget=null;combatMoves=false;command=RouteCommand();}
                }
                else {combatTarget=null;combatMoves=false;command=RouteCommand();}
                if(combatTarget==null) command=DoorIntent(command);
                if(!healingPending && !command.interact && !command.inventory && !command.menu &&
                    !runtime.Hud.BlocksGameplayInput && !runtime.Hud.ContainerOpen &&
                    Actor.health<75 && InventoryJournal.Count(Actor,"heal")>0 && Time.unscaledTime>=nextHeal)
                {
                    healingPending=true;healingLifecycle=lifecycle;healingInventory=InventoryJournal.Count(Actor,"heal");
                    healingRequest=null;healingReply=null;healingSuccess=false;healingDeadline=Time.unscaledTime+8;
                    command.heal=true;nextHeal=Time.unscaledTime+1.25f;
                    Debug.Log($"Обычный маршрут офиса: собственный intent самолечения actor={actorId}, lifecycle={healingLifecycle}, HP={Actor.health}, heal={healingInventory}, phase={stage}.");
                }
                return command;
            }
            catch(Exception error) {Failure=error.Message;return default;}
        }

        private bool MovingCombatStage()=>stage is Stage.WorkshopLeftApproach or Stage.WorkshopLeftUp or
            Stage.WorkshopLeaveShelf or Stage.WorkshopBottom or Stage.WorkshopRightUp or Stage.EastGapApproach or
            Stage.OfficeEntry or Stage.OfficeLadderApproach or Stage.OfficeDown or Stage.OfficeTarget or
            Stage.OfficeReturnBottom or Stage.OfficeUp or Stage.OfficeUnderShelf or Stage.ReturnSeam or
            Stage.WestGapApproach or Stage.WorkshopRightApproach or Stage.LeftGapApproach or Stage.ReturnStart;

        // The route follows an observed production healing epoch only after its own
        // input intent, successful reply and matching journal/inventory snapshot.
        // A snapshot can arrive before the reply: stop route advancement in that gap.
        public bool SynchronizeSelfHealing()
        {
            if(!healingPending || Failure!=null) return false;
            try
            {
                if(Actor==null || Actor.id!=actorId || Actor.owner!=owner || Actor.owner!=runtime.Session.LocalOwner ||
                    Actor.generation!=generation || Actor.level!=Level || Actor.down || !Actor.connected ||
                    !ReferenceEquals(room,runtime.Geometry.Get(Level)))
                    throw new InvalidOperationException("Самолечение на маршруте изменило личность, владельца, поколение, локацию, сознание или геометрию игрока.");
                if(lifecycle!=healingLifecycle || Actor.lifecycleSequence<healingLifecycle || Actor.lifecycleSequence>healingLifecycle+1)
                    throw new InvalidOperationException("Самолечение на маршруте содержит неожиданный lifecycle: "+Actor.lifecycleSequence);
                if(Time.unscaledTime>healingDeadline) throw new InvalidOperationException("Не подтверждено собственное самолечение за 8 секунд: "+Progress);
                if(healingReply==null) return Actor.lifecycleSequence!=lifecycle;
                if(!healingSuccess)
                {
                    if(Actor.lifecycleSequence!=lifecycle || InventoryJournal.Count(Actor,"heal")!=healingInventory)
                        throw new InvalidOperationException("Отказ лечения изменил lifecycle или запас аптечек на маршруте.");
                    healingPending=false;return false;
                }
                if(Actor.lifecycleSequence==healingLifecycle) return false;
                if(!runtime.Session.World.completedTransactions.Contains(healingReply)) return true;
                if(!OfficeRouteSelfHealingProof.Confirms(runtime.Session.World,Actor,healingReply,
                    actorId,owner,Level,generation,healingLifecycle,healingInventory))
                    throw new InvalidOperationException("Подтверждённое самолечение не совпало с игроком, epoch, журналом или расходом одной собственной аптечки.");
                int prior=lifecycle;lifecycle=Actor.lifecycleSequence;
                healedTransactions.Add(healingReply);healingPending=false;
                SelfHealingConfirmed?.Invoke(prior,lifecycle,healingReply);
                Debug.Log($"Обычный маршрут офиса: подтверждено самолечение transaction={healingReply}, actor={actorId}, lifecycle={prior}->{lifecycle}, heal={healingInventory}->{healingInventory-1}, HP={Actor.health}.");
                return false;
            }
            catch(Exception error) {Failure=error.Message;return false;}
        }

        private ActorState FindThreat()
        {
            var origin=Actor.Position+Vector2.up*.65f;
            return runtime.Session.World.actors.Where(a=>!a.player && !a.down && a.health>0 && a.level==Level &&
                (a.id.StartsWith(Level+":original:plant:18:",StringComparison.Ordinal) ||
                 a.id.StartsWith(Level+":original:plant:53:",StringComparison.Ordinal)) && ActorTypes.Get(a.actorType).combatAi &&
                Vector2.Distance(a.Position,Actor.Position)<12 &&
                (a.target==Actor.id || a.attackBusy && a.attackTarget==Actor.id ||
                 Vector2.Distance(a.Position,Actor.Position)<4 && Mathf.Abs(a.y-Actor.y)<1) &&
                RoomVisibility.Clear(room,origin,a.Position+Vector2.up*(Anatomy.Movement(a).height*.5f)))
                .OrderBy(a=>(a.Position-Actor.Position).sqrMagnitude).ThenBy(a=>a.id,StringComparer.Ordinal).FirstOrDefault();
        }

        private bool ShotClearOfPlayers(Vector2 aim)
        {
            var muzzle=WeaponPose.Muzzle(Actor);var delta=aim-muzzle;
            if(delta.sqrMagnitude<.0001f || Vector2.Dot(WeaponPose.Direction(Actor),delta.normalized)<.9999f) return false;
            if(ShotObstacles.FirstContact(room,muzzle,aim).HasValue) return false;
            foreach(var other in runtime.Session.World.actors)
                if(other.player && other.id!=actorId && other.level==Level &&
                    SurfaceContact.Entry(InventorySource.BodyBounds(other),muzzle,aim,.2f).HasValue) return false;
            return true;
        }

        private PlayerCommand DoorIntent(PlayerCommand command)
        {
            if(Actor.climbing || command.interact || command.jump || command.jumpHeld || Mathf.Abs(command.movement.x)<.1f) return command;
            var hull=Anatomy.Movement(Actor);float direction=Mathf.Sign(command.movement.x);
            var door=runtime.Session.World.doors.Where(d=>!d.open && d.level==Level &&
                Actor.y+hull.height>d.y && Actor.y<d.y+d.height &&
                (direction>0?d.x>=Actor.x:d.x+d.width<=Actor.x) && d.DistanceTo(Actor.Position)<1.3f &&
                runtime.Session.Rules.DoorAccess.Rejection(Actor,d)==null)
                .OrderBy(d=>d.DistanceTo(Actor.Position)).ThenBy(d=>d.id,StringComparer.Ordinal).FirstOrDefault();
            if(door==null) {requestedDoor=null;return command;}
            if(requestedDoor==door.id)
            {
                if(Time.unscaledTime-doorRequestedAt>3) throw new InvalidOperationException("Обычный E не открыл дверь маршрута: "+door.id);
                return command;
            }
            var target=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,door.Bounds.center,
                runtime.Session.Travel.Points(Level),runtime.MechanismInteraction.Points);
            if(target.kind!=InteractionKind.Door || target.id!=door.id)
                throw new InvalidOperationException($"E перед дверью {door.id} выбрал {target.kind}/{target.id}.");
            requestedDoor=door.id;doorRequestedAt=Time.unscaledTime;
            command.hasAim=true;command.aim=door.Bounds.center;command.interact=true;return command;
        }

        private PlayerCommand RouteCommand()
        {
            switch(stage)
            {
                case Stage.WorkshopLeftApproach:return Move(43.42f);
                case Stage.WorkshopLeftUp:case Stage.WorkshopRightUp:case Stage.OfficeUp:return new PlayerCommand {movement=Vector2.up};
                case Stage.OfficeDown:return new PlayerCommand {movement=Vector2.down};
                case Stage.WorkshopLeaveShelf:return Move(51.1f);
                case Stage.WorkshopBottom:return Move(67.42f);
                case Stage.EastGapApproach:return new PlayerCommand {movement=Vector2.right};
                case Stage.EastGap:return Jump(Vector2.right,false);
                case Stage.OfficeEntry:return Move(84);
                case Stage.OfficeDrop:
                    // One press, one released sample, second press; then gravity does
                    // the drop. Retry a failed gesture only while still on the shelf.
                    int pulse=downPulse++;
                    return new PlayerCommand {movement= pulse%12 is 0 or 2?Vector2.down:Vector2.zero};
                case Stage.OfficeLadderApproach:return Move(90.62f);
                case Stage.OfficeTarget:return Move(78.8f);
                case Stage.Open:
                    if(opened) return default;
                    var aim=((InventorySource)Container).Bounds.center;
                    var target=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,aim,runtime.Session.Travel.Points(Level),runtime.MechanismInteraction.Points);
                    if(target.kind!=InteractionKind.Container || target.id!=TargetId)
                        throw new InvalidOperationException($"E офиса выбрал {target.kind}/{target.id} вместо {TargetId}.");
                    opened=true;return new PlayerCommand {interact=true,hasAim=true,aim=aim};
                case Stage.OfficeReturnBottom:return Move(90.62f);
                case Stage.OfficeUnderShelf:return Move(84);
                case Stage.OfficeJump:return Jump(Vector2.zero,true);
                case Stage.ReturnSeam:return Move(74.4f);
                case Stage.WestGapApproach:return new PlayerCommand {movement=Vector2.left};
                case Stage.WestGap:return Jump(Vector2.left,false);
                case Stage.WorkshopRightApproach:return Move(67.42f);
                case Stage.RightRepair:
                    if(!Host || RightButton.repairState==MechanismRepairStates.Ready || runtime.MechanismInteraction.Pending) return default;
                    if(!runtime.MechanismInteraction.Repairing) mechanismPressed=false;
                    return MechanismCommand(true);
                case Stage.CallPlatform:case Stage.ReturnPlatform:
                    if(!Host || runtime.MechanismInteraction.Pending || mechanismPressed) return default;
                    return MechanismCommand(false);
                case Stage.BoardPlatform:return Move(66.3f);
                case Stage.LeftGapApproach:return new PlayerCommand {movement=Vector2.left};
                case Stage.LeftGap:return Jump(Vector2.left,false);
                case Stage.ReturnStart:return Move(40.4f);
                default:return default;
            }
        }

        public void Tick()
        {
            if(stage==Stage.None || Failure!=null || Complete) return;
            try
            {
                if(SynchronizeSelfHealing() || Failure!=null) return;
                if(Actor==null || Actor.id!=actorId || Actor.owner!=owner || Actor.owner!=runtime.Session.LocalOwner ||
                    Actor.generation!=generation || Actor.lifecycleSequence!=lifecycle || !Actor.connected || Actor.level!=Level ||
                    runtime.Session.Travel.Pending || !ReferenceEquals(room,runtime.Geometry.Get(Level)))
                    throw new InvalidOperationException("Обычный маршрут офиса потерял игрока/владельца/поколение/локацию/геометрию: "+Progress);
                if(Actor.down || Actor.health<=0) throw new InvalidOperationException("Игрок потерял сознание на обычном маршруте офиса.");
                foreach(var target in fought) if(runtime.Session.World.Find(target) is {down:true}) defeated.Add(target);
                if(Time.unscaledTime>=nextTrace) {nextTrace=Time.unscaledTime+5;Debug.Log("Обычный маршрут офиса: "+Progress);}
                if(runtime.Session.Pause.IsPaused(Level) || combatTarget!=null && !combatMoves) return;
                if(boardSent && stage is Stage.BoardBarrier or Stage.ReturnPlatform or Stage.RidePlatform)
                    RecordDepartureFrame();
                switch(stage)
                {
                    case Stage.WorkshopLeftApproach:
                        if(AtX(43.42f) && (Landed(7.2f) || Landed(10.4f))) Set(Stage.WorkshopLeftUp);break;
                    // The starting solid platform overlaps the left ladder's hull.
                    // Walk over it and off its right edge; ordinary gravity reaches
                    // the lower floor beyond the shelf ending at X50.4.
                    case Stage.WorkshopLeftUp:if(Landed(10.4f)) Set(Stage.WorkshopLeaveShelf);break;
                    case Stage.WorkshopLeaveShelf:
                        if(Landed(.8f) && runtime.Platforms.TrySample(PlatformId,out var initialSample) &&
                            Actor.x-Anatomy.Movement(Actor).width*.5f>initialSample.SupportBounds.xMax+.001f)
                            Set(Stage.WorkshopBottom,"workshop-bottom");break;
                    case Stage.WorkshopBottom:if(At(67.42f,.8f)) Set(Stage.WorkshopRightUp);break;
                    case Stage.WorkshopRightUp:if(Landed(10.4f)) Set(Stage.EastGapApproach);break;
                    case Stage.EastGapApproach:if(Actor.x>=71.3f && Landed(10.4f)) Set(Stage.EastGap);break;
                    case Stage.EastGap:if(Actor.x>=73.7f && Landed(10.4f)) Set(Stage.OfficeEntry);break;
                    case Stage.OfficeEntry:if(At(84,10.4f)) Set(Stage.OfficeDrop,"office-entry");break;
                    case Stage.OfficeDrop:if(Landed(7.2f)) Set(Stage.OfficeLadderApproach);break;
                    case Stage.OfficeLadderApproach:if(At(90.62f,7.2f)) Set(Stage.OfficeDown);break;
                    case Stage.OfficeDown:if(Landed(.8f)) {LowerFloorVerified=true;Set(Stage.OfficeTarget,"office-lower");}break;
                    case Stage.OfficeTarget:if(At(78.8f,.8f)) Set(Stage.Open);break;
                    case Stage.Open:if(runtime.Hud.ContainerOpen)
                        {
                            if(runtime.Hud.ShownInventorySourceId!=TargetId) throw new InvalidOperationException("Окно офиса показывает другой источник.");
                            if(!healingPending && !viewSent) {viewSent=true;SendViewed(false);Set(Stage.Viewed,"office-container");}
                        }break;
                    case Stage.Viewed:
                        if(Host && viewers.Count==runtime.Options.ProbePeers && !claiming)
                        {
                            if(Container.items.Count==0) throw new InvalidOperationException("Исходный explbox офиса пуст: "+TargetId);
                            var item=Container.items[0];ClaimedItem=item.id;ClaimedDefinition=item.definition;ClaimedCount=item.count;claimRecipient=actorId;
                            inventoryBefore=InventoryJournal.Count(Actor,ClaimedDefinition);claiming=true;
                            runtime.StartCoroutine(UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0),followTarget:true));Set(Stage.Claim);
                        }
                        else if(!Host && ClaimedItem!=null && Removed()) {runtime.Hud.CloseContainer();Set(Stage.OfficeReturnBottom,"office-granted");}
                        break;
                    case Stage.Claim:if(Removed())
                        {
                            if(InventoryJournal.Count(Actor,ClaimedDefinition)!=inventoryBefore+ClaimedCount)
                                throw new InvalidOperationException("Подтверждённый лут офиса не совпал с инвентарём получателя.");
                            NetworkServer.SendToAll(Loot(false));runtime.Hud.CloseContainer();Set(Stage.OfficeReturnBottom,"office-granted");
                        }break;
                    case Stage.OfficeReturnBottom:if(At(90.62f,.8f)) Set(Stage.OfficeUp);break;
                    case Stage.OfficeUp:if(Landed(7.2f)) Set(Stage.OfficeUnderShelf);break;
                    case Stage.OfficeUnderShelf:if(At(84,7.2f) && Mathf.Abs(Actor.vx)<.1f) Set(Stage.OfficeJump);break;
                    case Stage.OfficeJump:if(jumped && Landed(10.4f)) Set(Stage.ReturnSeam,"office-return-shelf");break;
                    case Stage.ReturnSeam:if(At(74.4f,10.4f)) Set(Stage.WestGapApproach);break;
                    case Stage.WestGapApproach:if(Actor.x<=74 && Landed(10.4f)) Set(Stage.WestGap);break;
                    case Stage.WestGap:if(Actor.x<=71.5f && Landed(10.4f)) Set(Stage.WorkshopRightApproach);break;
                    case Stage.WorkshopRightApproach:if(!healingPending && At(67.42f,10.4f)) {SendViewed(false,1);Set(Stage.RightBank);}break;
                    case Stage.RightBank:
                        if(Host && bankWaiters.Count==runtime.Options.ProbePeers && !bankBroadcast)
                        {bankBroadcast=true;NetworkServer.SendToAll(Loot(false,1));}
                        if(bankAcknowledged) Set(Stage.RightRepair,"workshop-right-button");break;
                    case Stage.RightRepair:
                        if(RightButton.repairState==MechanismRepairStates.Ready && !runtime.MechanismInteraction.Pending)
                        {PlatformRepairReady=true;Set(Stage.CallPlatform);}break;
                    case Stage.CallPlatform:
                        if(Platform.segment is {fromEndpoint:0,toEndpoint:1} || SampleAtEndpoint(1,out _)) Set(Stage.WaitPlatform);break;
                    case Stage.WaitPlatform:if(SampleAtEndpoint(1,out _)) Set(Stage.BoardPlatform);break;
                    case Stage.BoardPlatform:
                        if(SampleAtEndpoint(1,out var boardSample) && At(66.3f,boardSample.SupportBounds.yMax) &&
                            Mathf.Abs(Actor.vx)<.1f && Actor.x-Anatomy.Movement(Actor).width*.5f>=boardSample.SupportBounds.xMin &&
                            Actor.x+Anatomy.Movement(Actor).width*.5f<=boardSample.SupportBounds.xMax)
                        {
                            if(boardedAt<0) boardedAt=Time.unscaledTime;
                            if(!healingPending && Time.unscaledTime-boardedAt>=.15f && !boardSent)
                            {
                                boardSent=true;boardX=Actor.x;boardY=Actor.y;boardRelative=Actor.x-boardSample.Anchor.x;
                                // Explicit boarding event: cache nearby authored floor
                                // candidates once. No geometry query/build in the trace.
                                boardingFloorCandidates=room.platforms.Where(s=>s.slopeDirection==0 &&
                                    Mathf.Abs(s.y+s.height-boardY)<=RoomSupport.ContactTolerance &&
                                    s.x+s.width>boardX-2 && s.x<boardX+2).ToArray();
                                boardingObservation=RecordDepartureFrame();
                                SendViewed(false,2);Set(Stage.BoardBarrier,"workshop-platform-boarded");
                            }
                        }
                        else boardedAt=-1;
                        break;
                    case Stage.BoardBarrier:
                        if(Host && boarders.Count==runtime.Options.ProbePeers && !boardBroadcast)
                        {boardBroadcast=true;NetworkServer.SendToAll(Loot(false,2));}
                        if(boardAcknowledged) Set(Stage.ReturnPlatform);break;
                    case Stage.ReturnPlatform:
                        if(Platform.segment is {fromEndpoint:1,toEndpoint:0}) Set(Stage.RidePlatform);break;
                    case Stage.RidePlatform:
                        if(!runtime.Platforms.TrySample(PlatformId,out var sample)) throw new InvalidOperationException("Нет живой опоры платформы при возвращении.");
                        float relative=Mathf.Abs(Actor.x-sample.Anchor.x-boardRelative);
                        PlatformCarryMaximumRelativeError=Mathf.Max(PlatformCarryMaximumRelativeError,relative);
                        if(relative>CarryTolerance+.00001f || Mathf.Abs(Actor.y-boardY)>RoomSupport.ContactTolerance+.00001f)
                            throw new InvalidOperationException($"Обычная перевозка потеряла контакт: actor={actorId}, relative={relative}, body={Actor.Position}, anchor={sample.Anchor}, {DepartureContext(lastDepartureFrame)}.");
                        if(sample.Moving && sample.Progress>.25f && sample.Progress<.75f && !rideCaptured)
                        {rideCaptured=true;capture="workshop-platform-moving";}
                        if(SampleAtEndpoint(0,out _) && Landed(boardY))
                        {
                            PlatformCarryDistance=boardX-Actor.x;
                            if(Mathf.Abs(PlatformCarryDistance-16.8f)>CarryTolerance) throw new InvalidOperationException("Не подтверждён полный обратный путь платформы: "+PlatformCarryDistance);
                            PlatformCarryVerified=true;SaveDepartureEvidence();Set(Stage.LeftGapApproach,"workshop-platform-returned");
                        }
                        break;
                    case Stage.LeftGapApproach:if(Actor.x<=43.65f && Landed(10.4f)) Set(Stage.LeftGap);break;
                    case Stage.LeftGap:if(Actor.x<=41.2f && Landed(10.4f)) Set(Stage.ReturnStart);break;
                    case Stage.ReturnStart:if(!healingPending && At(40.4f,10.4f))
                        {ReturnedFromOfficeVerified=true;SendViewed(true);returnSent=true;Set(Stage.ReturnBarrier,"office-return");}break;
                    case Stage.ReturnBarrier:
                        if(Host && returners.Count==runtime.Options.ProbePeers && !returnBroadcast)
                        {returnBroadcast=true;NetworkServer.SendToAll(Loot(true));}
                        if(returnSent && returnAcknowledged) Set(Stage.Complete);break;
                }
            }
            catch(Exception error)
            {
                Failure=error.Message;
                if(boardSent)
                {SaveDepartureEvidence();capture="office-platform-contact-failure";}
            }
        }

        private DepartureFrame RecordDepartureFrame()
        {
            if(!runtime.Platforms.TrySample(PlatformId,out var sample)) return null;
            var platform=Platform;var segment=platform.segment;var hull=Anatomy.Movement(Actor);
            var observed=new DepartureFrame {frame=Time.frameCount,phase=stage.ToString(),actorId=actorId,
                delta=Time.deltaTime,realSeconds=Time.unscaledTime,clock=runtime.Session.LevelTime.ReadLocal(Level),
                clockSequence=runtime.Session.LevelTime.ClientSequence,worldRevision=runtime.Session.World.revision,
                endpoint=platform.endpoint,platformRevision=platform.revision,segmentPresent=segment!=null,
                segmentSequence=segment?.sequence??0,start=segment?.startElapsedSeconds??0,end=segment?.EndElapsedSeconds??0,
                body=Actor.Position,velocity=Actor.Velocity,actorPose=Actor.pose,actorPoseTime=Actor.poseTime,
                generation=Actor.generation,lifecycle=Actor.lifecycleSequence,hullWidth=hull.width,
                anchor=sample.Anchor,progress=sample.Progress,moving=sample.Moving,
                supported=ActorTerrainMotion.Supported(room,hull,Actor.Position),
                relativeError=Mathf.Abs(Actor.x-sample.Anchor.x-boardRelative),heightError=Mathf.Abs(Actor.y-boardY)};
            foreach(var surface in boardingFloorCandidates)
                observed.staticHullOverlap=Mathf.Max(observed.staticHullOverlap,
                    Mathf.Max(0,Mathf.Min(Actor.x+hull.width*.5f,surface.x+surface.width)-Mathf.Max(Actor.x-hull.width*.5f,surface.x)));
            var published=room.dynamicSurfaces;
            if(published!=null)
            {
                observed.published=published.IsPublished;observed.physicsSequence=published.Sequence;
                observed.previousClock=published.PreviousElapsedSeconds;observed.physicsClock=published.ElapsedSeconds;
                if(published.IsPublished)
                    for(int i=0;i<published.Count;i++)
                    {
                        var pose=published.Pose(i);if(pose.Id!=PlatformId) continue;
                        observed.physicsFound=true;observed.continuous=pose.Continuous;
                        observed.previousBounds=new Rect(pose.Previous.x,pose.Previous.y,pose.Previous.width,pose.Previous.height);
                        observed.currentBounds=new Rect(pose.Current.x,pose.Current.y,pose.Current.width,pose.Current.height);
                        observed.physicsDelta=pose.Delta;break;
                    }
            }
            if(segment?.fromEndpoint==1 && firstDepartureFrames.Count<24) firstDepartureFrames.Add(observed);
            if(segment?.fromEndpoint==1 && sample.Moving && firstMovingObservation==null) firstMovingObservation=observed;
            recentDepartureFrames.Enqueue(observed);while(recentDepartureFrames.Count>64) recentDepartureFrames.Dequeue();
            if(segment?.fromEndpoint==1 && firstDepartureFrames.Count<=2)
                Debug.Log("Обычный маршрут офиса: первый подтверждённый кадр отправления: "+DepartureContext(observed));
            lastDepartureFrame=observed;return observed;
        }
        private static string DepartureContext(DepartureFrame frame)=>frame==null?"Нет опубликованного наблюдения отправления":
            $"frame={frame.frame}, segment={frame.segmentSequence}/{frame.start}->{frame.end}, clock={frame.clock}, physics={frame.physicsSequence}/{frame.previousClock}->{frame.physicsClock}, continuous={frame.continuous}, previous={frame.previousBounds}, current={frame.currentBounds}, delta={frame.physicsDelta}, velocity={frame.velocity}, supported={frame.supported}, staticHullOverlap={frame.staticHullOverlap}";
        private void SaveDepartureEvidence()
        {
            string path=runtime.Options.Report+".office-carry.json";
            try {File.WriteAllText(path,JsonUtility.ToJson(new DepartureEvidence {failure=Failure,board=boardingObservation,firstMoving=firstMovingObservation,
                authoredFloorCandidates=boardingFloorCandidates,firstConfirmed=firstDepartureFrames.ToArray(),recent=recentDepartureFrames.ToArray()},true));}
            catch(Exception error) {Debug.LogWarning($"Не удалось записать наблюдения обычной перевозки: actor={actorId}, platform={PlatformId}, path={path}, reason={error.Message}.");}
        }
        [Serializable] private sealed class DepartureEvidence
        {public string failure;public DepartureFrame board,firstMoving;public RoomSurface[] authoredFloorCandidates;public DepartureFrame[] firstConfirmed,recent;}
        [Serializable] private sealed class DepartureFrame
        {
            public string phase,actorId,actorPose;
            public int frame,endpoint,generation,lifecycle;
            public long worldRevision,platformRevision,segmentSequence,physicsSequence,clockSequence;
            public float delta,realSeconds,actorPoseTime,hullWidth,relativeError,heightError,staticHullOverlap;
            public double clock,start,end,progress,previousClock,physicsClock;
            public Vector2 body,velocity,anchor,physicsDelta;
            public Rect previousBounds,currentBounds;
            public bool segmentPresent,moving,supported,published,physicsFound,continuous;
        }

        private bool Removed()=>!Container.items.Any(i=>i.id==ClaimedItem) &&
            !runtime.Session.World.pendingGrants.Any(g=>g.containerId==TargetId);
        private PlayerCommand MechanismCommand(bool repairing)
        {
            var point=runtime.MechanismInteraction.Points.Single(p=>p.Button.id==RightButtonId);
            if(MechanismButtonAccess.Rejection(Actor,RightButton,point.Radius) is { } reason)
                throw new InvalidOperationException("Правая кнопка маршрута недоступна: "+reason);
            bool edge=!mechanismPressed;mechanismPressed=true;
            return new PlayerCommand {hasAim=true,aim=point.Bounds.center,interact=edge,revive=repairing};
        }
        private bool SampleAtEndpoint(int endpoint,out MovingPlatformSample sample)
        {
            if(!runtime.Platforms.TrySample(PlatformId,out sample)) return false;
            return Platform.endpoint==endpoint && Platform.segment==null &&
                Vector2.Distance(sample.Anchor,endpoint==0?Platform.Start:Platform.End)<.005f;
        }
        private void SendViewed(bool returned,int barrier=0)=>NetworkClient.Send(new OfficeRouteViewed
            {actor=actorId,source=TargetId,generation=generation,lifecycle=lifecycle,returned=returned,barrier=barrier});
        private OfficeRouteLoot Loot(bool returned,int barrier=0)=>new() {source=TargetId,item=ClaimedItem,definition=ClaimedDefinition,
            count=ClaimedCount,recipient=claimRecipient,returned=returned,barrier=barrier};
        private void AcceptLoot(OfficeRouteLoot message)
        {
            if(message.source!=TargetId || string.IsNullOrEmpty(message.item) || string.IsNullOrEmpty(message.definition) ||
                message.count<=0 || string.IsNullOrEmpty(message.recipient)) {Failure="Неверное подтверждение лута маршрута офиса.";return;}
            if(ClaimedItem!=null && (ClaimedItem!=message.item || ClaimedDefinition!=message.definition ||
                ClaimedCount!=message.count || claimRecipient!=message.recipient)) {Failure="Повторное подтверждение офиса содержит другой лут.";return;}
            ClaimedItem=message.item;ClaimedDefinition=message.definition;ClaimedCount=message.count;claimRecipient=message.recipient;
            if(message.barrier==1) bankAcknowledged=true;
            else if(message.barrier==2) boardAcknowledged=true;
            else if(message.barrier!=0) {Failure="Неизвестный барьер платформы маршрута: "+message.barrier;return;}
            if(message.returned) returnAcknowledged=true;
        }
        public void Capture()
        {
            if(capture==null) return;
            if(runtime.Options.Smoke.Contains("-frames-"))
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                NetworkSceneRecord.Save(runtime,100+(int)stage);
            }
            capture=null;
        }
    }

    // Probe evidence rule; no state mutation or production healing replacement.
    internal static class OfficeRouteSelfHealingProof
    {
        internal static bool Confirms(WorldState world,ActorState actor,string transaction,string actorId,
            string owner,string level,int generation,int lifecycle,int inventoryBefore)=>
            world!=null && actor!=null && !string.IsNullOrEmpty(transaction) && world.completedTransactions.Contains(transaction) &&
            actor.id==actorId && actor.owner==owner && actor.level==level && actor.generation==generation &&
            actor.lifecycleSequence==lifecycle+1 && actor.connected && !actor.down &&
            InventoryJournal.Count(actor,"heal")==inventoryBefore-1;
    }
}
