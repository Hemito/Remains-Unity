using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Content;
using UnityEngine;

namespace Remains.Networking
{
    public sealed class InteractionAdapter
    {
        private readonly CoopSession session;
        private readonly Func<string,RoomLayout> roomForLevel;
        public event Action<GrenadeState> GrenadeUpdated;
        public event Action<PropState,Vector2> PropMoved;
        public InteractionAdapter(CoopSession session,Func<string,RoomLayout> roomForLevel=null)
        {
            this.session=session;this.roomForLevel=roomForLevel;
            session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<PropGrabMessage>(Remains.Networking.MessageTiming.Server<PropGrabMessage>(Grab));
                NetworkServer.RegisterHandler<PropPoseMessage>(Remains.Networking.MessageTiming.Server<PropPoseMessage>(Pose));
                NetworkServer.RegisterHandler<ThrowRequest>(Remains.Networking.MessageTiming.Server<ThrowRequest>(Throw));
                NetworkServer.RegisterHandler<DebugGrantRequest>(Remains.Networking.MessageTiming.Server<DebugGrantRequest>(DebugGrant));
                NetworkServer.RegisterHandler<GrenadePoseMessage>(Remains.Networking.MessageTiming.Server<GrenadePoseMessage>(GrenadePose));
                NetworkServer.RegisterHandler<GrenadeHoldRequest>(Remains.Networking.MessageTiming.Server<GrenadeHoldRequest>(GrenadeHold));
            };
            session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PropGrabRejected>(Remains.Networking.MessageTiming.Client<PropGrabRejected>(reply=>
                {
                    var body=WorldProps.Find(session.World,reply.prop);
                    if(body!=null) session.RejectPrediction(BodyKey(body),reply.reason);
                    if(!string.IsNullOrEmpty(reply.reason)) session.ShowNotice(reply.reason);
                }));
                NetworkClient.RegisterHandler<PropPoseMessage>(Remains.Networking.MessageTiming.Client<PropPoseMessage>(message=>
                {
                    var prop=WorldProps.Find(session.World,message.prop);
                    if(prop==null || prop.generation!=message.generation || !PropReplication.CanAcceptPose(prop) || prop.owner==session.LocalOwner) return;
                    var previous=prop.Position;
                    if(PropReplication.Accept(prop,message.generation,message.sequence,message.x,message.y,message.vx,message.vy))
                    {prop.impactFriendlyPlayers=message.impactFriendlyPlayers;NotifyPropMotion(prop,previous);}
                }));
                NetworkClient.RegisterHandler<GrenadePoseMessage>(Remains.Networking.MessageTiming.Client<GrenadePoseMessage>(message=>
                {
                    var grenade=session.World?.grenades.Find(g=>g.id==message.id);
                    if(grenade==null || grenade.generation!=message.generation || unchecked((int)(message.sequence-grenade.sequence))<=0) return;
                    Apply(grenade,message);GrenadeUpdated?.Invoke(grenade);
                }));
            };
        }
        public void NotifyPropMotion(PropState prop,Vector2 previous)
        {
            if(!prop.Position.Equals(previous)) PropMoved?.Invoke(prop,previous);
        }

        private void Grab(NetworkConnectionToClient connection,PropGrabMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var prop=WorldProps.Find(session.ServerWorld,message.prop);
            void Reject(string reason)=>connection.Send(new PropGrabRejected {prop=message.prop,reason=reason});
            if(actor==null || prop==null || actor.level!=prop.level) {Reject(null);return;}
            PropBodyRules.Validate(prop);
            if(session.Pause.IsServerPaused(actor.level)) {Reject("Захват/отпускание отменено: уровень остановлен.");return;}
            if(message.release)
            {
                if(prop.owner!=actor.owner || !prop.held) {Reject(null);return;}
                prop.held=false;prop.generation++;WorldChanges.Mark(session.ServerWorld,prop);
            }
            else
            {
                if(actor.down) {Reject(null);return;}
                if(actor.hold!=null || session.ServerWorld.actors.Any(a=>a.hold?.holder==actor.id)) {Reject("Сначала завершите захват тела.");return;}
                if(!prop.movable || !prop.hasPhysicalBody) {Reject("Этот объект закреплён.");return;}
                if(prop.held && prop.owner!=actor.owner) {Reject("Объект уже удерживает другой игрок.");return;}
                prop.owner=actor.owner;prop.generation++;prop.held=true;prop.impactFriendlyPlayers=true;WorldChanges.Mark(session.ServerWorld,prop);
            }
            session.CommitWorld();
        }
        private void Pose(NetworkConnectionToClient connection,PropPoseMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var prop=WorldProps.Find(session.ServerWorld,message.prop);
            if(prop!=null && !PropReplication.CanAcceptPose(prop)) return;
            if(!PropPoseScope.CanPublish(session.ServerWorld,actor,prop,prop!=null && session.Pause.IsServerPaused(prop.level)) ||
                !PropReplication.Accept(prop,message.generation,message.sequence,message.x,message.y,message.vx,message.vy)) return;
            prop.impactFriendlyPlayers=message.impactFriendlyPlayers;
            WorldChanges.Mark(session.ServerWorld,prop);
            NetworkServer.SendToAll(message,Channels.Unreliable);
        }

        private void DebugGrant(NetworkConnectionToClient connection,DebugGrantRequest message)
        {
            var actor=session.ConnectedActor(connection);
            if(actor==null || DebugItems.Grant(actor,message.definition,message.count)==null)
            {Debug.LogWarning($"Выдача отклонена: actor={actor?.id}, item={message.definition}, count={message.count}.");return;}
            session.CommitWorld();
        }
        private void Throw(NetworkConnectionToClient connection,ThrowRequest message)
        {
            var actor=session.ConnectedActor(connection);
            if(!string.IsNullOrEmpty(message.thrower))
            {
                // A sapient NPC of the sender's throws: owned by it, present, of the raiders' mind.
                var npc=session.ServerWorld.Find(message.thrower);
                actor=actor!=null && npc!=null && !npc.player && npc.owner==actor.owner && ActorPresence.IsPresent(npc) && SapientPonies.Is(npc)?npc:null;
                if(actor==null) {Debug.LogWarning($"Бросок NPC отклонён: thrower={message.thrower} не принадлежит отправителю или не разумный пони.");return;}
            }
            if(actor!=null && session.Pause.IsServerPaused(actor.level))
            {connection.Send(new NoticeMessage {text="Бросок отменён: уровень остановлен. Граната не потрачена."});return;}
            if(!GrenadeLaunchRequest.Matches(actor,message) || session.ServerWorld.completedTransactions.Contains(message.transaction) ||
                InventoryStacks.Find(actor,message.item) is not { } stack || !WeaponCatalog.IsGrenade(stack.definition) ||
                !SharedInventory.Consume(actor,stack.definition,message.item,message.transaction)) return;
            string weapon=stack.definition;
            session.ServerWorld.completedTransactions.Add(message.transaction);
            Vector2 origin=new(message.originX,message.originY);
            // The throwing client captures launch motion with its local pose and aim.
            var velocity=new Vector2(message.vx,message.vy);
            actor.revision++;
            session.ServerWorld.grenades.Add(new GrenadeState {id=message.transaction,weapon=weapon,attacker=actor.id,level=actor.level,owner=actor.owner,
                sourcePlayer=actor.player,generation=1,x=origin.x,y=origin.y,vx=velocity.x,vy=velocity.y,
                rotationVersion=1,angle=message.angle,angularVelocity=message.angularVelocity,mirrored=message.mirrored,
                remaining=WeaponCatalog.Get(weapon).grenadeFuseSeconds});
            session.CommitWorld();
        }
        private void GrenadeHold(NetworkConnectionToClient connection,GrenadeHoldRequest request)
        {
            var actor=session.ConnectedActor(connection);
            var world=session.ServerWorld;
            var grenade=world?.grenades.Find(value=>value.id==request.grenade);
            if(actor==null || grenade==null || actor.generation!=request.actorGeneration || actor.lifecycleSequence!=request.actorLifecycle ||
                grenade.generation!=request.generation || grenade.level!=actor.level || grenade.remaining<=0) return;
            if(request.release)
            {
                if(grenade.holder!=actor.id || grenade.owner!=actor.owner) return;
                GrenadeRules.ReleaseHold(grenade);
            }
            else
            {
                var room=roomForLevel?.Invoke(grenade.level);
                if(room==null || session.Pause.IsServerPaused(actor.level)) return;
                if(grenade.holder!=null || actor.hold!=null || world.grenades.Any(value=>value.holder==actor.id) ||
                    world.actors.Any(value=>value.hold?.holder==actor.id) || world.mines.Any(value=>value.holder==actor.id) ||
                    WorldProps.All(world).Any(value=>value.held && value.owner==actor.owner)) return;
                var body=GrenadeRules.TelekinesisBody(grenade);
                var rule=session.Rules.Telekinesis;
                string reason=rule.Rejection(actor,body,false);
                if(reason==null && rule is ITelekinesisSightRule sight) reason=sight.SightRejection(actor,body,room);
                if(reason!=null) {connection.Send(new NoticeMessage {text=reason});return;}
                GrenadeRules.Hold(grenade,actor);
            }
            session.CommitWorld();
        }
        public void RequestGrenadeHold(string id,int generation,bool release)
        {
            var actor=session.LocalActor;
            if(actor==null || !NetworkClient.isConnected) return;
            NetworkClient.Send(new GrenadeHoldRequest {grenade=id,generation=generation,actorGeneration=actor.generation,
                actorLifecycle=actor.lifecycleSequence,release=release});
        }
        private static void Apply(GrenadeState state,GrenadePoseMessage message)
        {state.x=message.x;state.y=message.y;state.vx=message.vx;state.vy=message.vy;state.remaining=message.remaining;state.simulationRemainder=message.simulationRemainder;state.sequence=message.sequence;
            state.rotationVersion=1;state.angle=message.angle;state.angularVelocity=message.angularVelocity;state.mirrored=message.mirrored;
            state.shattered=message.shattered;state.impactVx=message.impactVx;state.impactVy=message.impactVy;state.fuel=message.fuel;}
        private void GrenadePose(NetworkConnectionToClient connection,GrenadePoseMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var grenade=session.ServerWorld?.grenades.Find(g=>g.id==message.id);
            if(actor==null || grenade==null || grenade.owner!=actor.owner || grenade.generation!=message.generation ||
                unchecked((int)(message.sequence-grenade.sequence))<=0) return;
            if(grenade.holder!=null && (grenade.holder!=actor.id || actor.down || actor.level!=grenade.level || actor.mana<=0))
            {session.CommitWorld();return;}
            if(message.detonate && session.Pause.Defer(grenade.level,()=>GrenadePose(connection,message))) return;
            if(!float.IsFinite(message.x) || !float.IsFinite(message.y) || !float.IsFinite(message.vx) || !float.IsFinite(message.vy) ||
                !float.IsFinite(message.angle) || !float.IsFinite(message.angularVelocity) ||
                !double.IsFinite(message.simulationRemainder) || message.simulationRemainder<0 || message.simulationRemainder>=GrenadeRules.SimulationStep ||
                !float.IsFinite(message.remaining) || message.remaining<0 || message.remaining>grenade.remaining+.001f)
            {Debug.LogWarning($"Некорректное состояние гранаты: id={message.id}, generation={message.generation}, remaining={message.remaining}.");return;}
            bool fire=message.detonate && WeaponCatalog.Get(grenade.weapon).IsFire;
            // Blast cover and fire droplets both need the level with its current doors (a level no one has loaded is read here).
            RoomLayout room=null;
            if(message.detonate && roomForLevel!=null)
            {
                room=roomForLevel(grenade.level);
                if(room==null)
                {
                    room=RoomLayout.Load(grenade.level,session.ServerWorld);
                    room.platforms=DoorGeometry.Compose(room.platforms,session.ServerWorld.doors,grenade.level);
                    RoomSurfaces.Prepare(room);
                }
            }
            Apply(grenade,message);
            if(fire)
            {
                // A fire grenade or bottle splashes burning droplets instead of a blast (FireRules).
                // No fire when a bottle burnt all its fuel in flight; the bottle is gone either way.
                FireRules.Ignite(session.ServerWorld,grenade,session.LevelTime.ReadServer(grenade.level),room);
                session.CommitWorld();
            }
            else if(message.detonate)
            {
                var explosion=GrenadeRules.Detonate(session.ServerWorld,grenade,message.enemyHits,room);
                if(explosion==null) return;
                session.CommitWorld();
                NetworkServer.SendToAll(AttackMessage.From(explosion.attack));
                session.Hazards.Blast(explosion.attack,explosion.grenadeCover);
            }
            else NetworkServer.SendToAll(message);
        }
        public void SendGrenade(GrenadeState state,bool detonate,string[] enemyHits=null)
        {
            state.sequence++;
            NetworkClient.Send(new GrenadePoseMessage {id=state.id,generation=state.generation,sequence=state.sequence,
                x=state.x,y=state.y,vx=state.vx,vy=state.vy,remaining=state.remaining,simulationRemainder=state.simulationRemainder,
                angle=state.angle,angularVelocity=state.angularVelocity,mirrored=state.mirrored,detonate=detonate,enemyHits=enemyHits,
                shattered=state.shattered,impactVx=state.impactVx,impactVy=state.impactVy,fuel=state.fuel});
        }
        public void GrabProp(string prop,bool release)
        {
            NetworkClient.Send(new PropGrabMessage { prop=prop,release=release });
            PredictGrab(prop,release);
        }
        private static string BodyKey(PropState body)=>(body is ContainerState?"containers:":"props:")+body.id;
        // The grabbing client holds the body at once (decisions.md, 2026-10-24) with the generation the host will give
        // it; the host's tick confirms ownership, a rejection or a missing confirmation restores the host's body.
        private void PredictGrab(string id,bool release)
        {
            var body=WorldProps.Find(session.World,id);string owner=session.LocalOwner;
            if(body==null || owner==null) return;
            session.Predict(BodyKey(body),body is ContainerState?WorldChangeKind.Containers:WorldChangeKind.Props,
                live=>
                {
                    var local=WorldProps.Find(live,id);
                    if(local==null) return false;
                    if(release) {if(!local.held || local.owner!=owner) return false;local.held=false;}
                    else {if(local.held && local.owner!=owner || !local.movable || !local.hasPhysicalBody) return false;local.owner=owner;local.held=true;local.impactFriendlyPlayers=true;}
                    local.generation++;return true;
                },
                canonical=>{var host=WorldProps.Find(canonical,id);return host!=null && (release?!host.held || host.owner!=owner:host.held && host.owner==owner);},
                (canonical,live)=>
                {
                    var host=WorldProps.Find(canonical,id);
                    if(host is ContainerState container) {int at=live.containers.FindIndex(c=>c.id==id);if(at>=0) live.containers[at]=JsonUtility.Copy(container);}
                    else if(host!=null) {int at=live.props.FindIndex(p=>p.id==id);if(at>=0) live.props[at]=JsonUtility.Copy(host);}
                });
        }
        public void SendProp(PropState prop)=>SendProp(prop,true);
        // newPose=false: a resting heartbeat resends the current pose version unchanged (PropReplication.Accept
        // takes only newer sequences, so a receiver that missed the last pose still accepts it).
        public void SendProp(PropState prop,bool newPose)
        {
            if(!PropReplication.CanAcceptPose(prop)) return;
            if(newPose) prop.sequence++;
            NetworkClient.Send(new PropPoseMessage { prop=prop.id,generation=prop.generation,sequence=prop.sequence,x=prop.x,y=prop.y,vx=prop.vx,vy=prop.vy,impactFriendlyPlayers=prop.impactFriendlyPlayers },Channels.Unreliable);
        }
        public bool ThrowGrenade(Vector2 aim,RoomLayout room=null)
        {
            var actor=session.LocalActor;
            string selected=actor==null?null:WeaponInventory.Selected(actor)?.definition;
            if(actor==null || actor.down || session.Pause.IsPaused(actor.level) || GrenadeItems.Next(actor)==null ||
                !HeldItemSelection.HasSelection(actor) && WeaponCatalog.IsGrenade(selected) && !WeaponInventory.Available(actor,selected)) return false;
            if(!HeldItemSelection.HasSelection(actor) && WeaponCatalog.IsGrenade(selected) && !actor.heldItemPoseValid)
            {session.ShowNotice("Гранату нельзя бросить: точка удержания перекрыта. Измените положение или прицел.");return false;}
            var request=GrenadeLaunchRequest.Capture(actor,aim,room);
            if(!GrenadeLaunchRequest.Matches(actor,request))
            {Debug.LogWarning($"Некорректная точка броска гранаты: actor={actor.id}, level={actor.level}, origin=({request.originX};{request.originY}), aim={aim}.");return false;}
            NetworkClient.Send(request);return true;
        }
    }
}
