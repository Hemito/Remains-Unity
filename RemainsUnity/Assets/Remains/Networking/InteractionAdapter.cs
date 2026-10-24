using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public sealed class InteractionAdapter
    {
        private readonly CoopSession session;
        public event Action<GrenadeState> GrenadeUpdated;
        public InteractionAdapter(CoopSession session)
        {
            this.session=session;
            session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<PropGrabMessage>(Remains.Networking.MessageTiming.Server<PropGrabMessage>(Grab));
                NetworkServer.RegisterHandler<PropPoseMessage>(Remains.Networking.MessageTiming.Server<PropPoseMessage>(Pose));
                NetworkServer.RegisterHandler<ThrowRequest>(Remains.Networking.MessageTiming.Server<ThrowRequest>(Throw));
                NetworkServer.RegisterHandler<GrenadePoseMessage>(Remains.Networking.MessageTiming.Server<GrenadePoseMessage>(GrenadePose));
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
                    if(PropReplication.Accept(prop,message.generation,message.sequence,message.x,message.y,message.vx,message.vy)) prop.impactFriendlyPlayers=message.impactFriendlyPlayers;
                }));
                NetworkClient.RegisterHandler<GrenadePoseMessage>(Remains.Networking.MessageTiming.Client<GrenadePoseMessage>(message=>
                {
                    var grenade=session.World?.grenades.Find(g=>g.id==message.id);
                    if(grenade==null || grenade.generation!=message.generation || unchecked((int)(message.sequence-grenade.sequence))<=0) return;
                    Apply(grenade,message);GrenadeUpdated?.Invoke(grenade);
                }));
            };
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

        private void Throw(NetworkConnectionToClient connection,ThrowRequest message)
        {
            var actor=session.ConnectedActor(connection);
            if(actor!=null && session.Pause.IsServerPaused(actor.level))
            {connection.Send(new NoticeMessage {text="Бросок отменён: уровень остановлен. Граната не потрачена."});return;}
            if(!GrenadeLaunchRequest.Matches(actor,message) || session.ServerWorld.completedTransactions.Contains(message.transaction) ||
                !SharedInventory.Consume(actor,"grenade",message.item,message.transaction)) return;
            session.ServerWorld.completedTransactions.Add(message.transaction);
            Vector2 origin=new(message.originX,message.originY);
            // The throwing client captures launch motion with its local pose and aim.
            var velocity=new Vector2(message.vx,message.vy);
            actor.revision++;
            session.ServerWorld.grenades.Add(new GrenadeState {id=message.transaction,attacker=actor.id,level=actor.level,owner=actor.owner,
                sourcePlayer=actor.player,generation=1,x=origin.x,y=origin.y,vx=velocity.x,vy=velocity.y,
                rotationVersion=1,angle=message.angle,angularVelocity=message.angularVelocity,mirrored=message.mirrored,
                remaining=Remains.Content.WeaponCatalog.Get("grenade").grenadeFuseSeconds});
            session.CommitWorld();
        }
        private static void Apply(GrenadeState state,GrenadePoseMessage message)
        {state.x=message.x;state.y=message.y;state.vx=message.vx;state.vy=message.vy;state.remaining=message.remaining;state.simulationRemainder=message.simulationRemainder;state.sequence=message.sequence;
            state.rotationVersion=1;state.angle=message.angle;state.angularVelocity=message.angularVelocity;state.mirrored=message.mirrored;}
        private void GrenadePose(NetworkConnectionToClient connection,GrenadePoseMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var grenade=session.ServerWorld?.grenades.Find(g=>g.id==message.id);
            if(actor==null || grenade==null || grenade.owner!=actor.owner || grenade.generation!=message.generation ||
                unchecked((int)(message.sequence-grenade.sequence))<=0) return;
            if(message.detonate && session.Pause.Defer(grenade.level,()=>GrenadePose(connection,message))) return;
            if(!float.IsFinite(message.x) || !float.IsFinite(message.y) || !float.IsFinite(message.vx) || !float.IsFinite(message.vy) ||
                !float.IsFinite(message.angle) || !float.IsFinite(message.angularVelocity) ||
                !double.IsFinite(message.simulationRemainder) || message.simulationRemainder<0 || message.simulationRemainder>=GrenadeRules.SimulationStep ||
                !float.IsFinite(message.remaining) || message.remaining<0 || message.remaining>grenade.remaining+.001f)
            {Debug.LogWarning($"Некорректное состояние гранаты: id={message.id}, generation={message.generation}, remaining={message.remaining}.");return;}
            Apply(grenade,message);
            if(message.detonate)
            {
                var explosion=GrenadeRules.Detonate(session.ServerWorld,grenade,message.enemyHits);
                if(explosion==null) return;
                session.CommitWorld();
                NetworkServer.SendToAll(new AttackMessage {json=JsonUtility.ToJson(explosion.attack)});
                session.Hazards.Blast(explosion.attack);
            }
            else NetworkServer.SendToAll(message);
        }
        public void SendGrenade(GrenadeState state,bool detonate,string[] enemyHits=null)
        {
            state.sequence++;
            NetworkClient.Send(new GrenadePoseMessage {id=state.id,generation=state.generation,sequence=state.sequence,
                x=state.x,y=state.y,vx=state.vx,vy=state.vy,remaining=state.remaining,simulationRemainder=state.simulationRemainder,
                angle=state.angle,angularVelocity=state.angularVelocity,mirrored=state.mirrored,detonate=detonate,enemyHits=enemyHits});
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
            if(actor==null || actor.down || session.Pause.IsPaused(actor.level) || InventoryJournal.Count(actor,"grenade")<=0 ||
                !HeldItemSelection.HasSelection(actor) && WeaponInventory.Selected(actor)?.definition=="grenade" && !WeaponInventory.Available(actor,"grenade")) return false;
            if(!HeldItemSelection.HasSelection(actor) && WeaponInventory.Selected(actor)?.definition=="grenade" && !actor.heldItemPoseValid)
            {session.ShowNotice("Гранату нельзя бросить: точка удержания перекрыта. Измените положение или прицел.");return false;}
            var request=GrenadeLaunchRequest.Capture(actor,aim,room);
            if(!GrenadeLaunchRequest.Matches(actor,request))
            {Debug.LogWarning($"Некорректная точка броска гранаты: actor={actor.id}, level={actor.level}, origin=({request.originX};{request.originY}), aim={aim}.");return false;}
            NetworkClient.Send(request);return true;
        }
    }
}
