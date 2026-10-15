using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Networking
{
    public struct MineDisarmRequest : NetworkMessage {public string world,actor,mine;public int generation,lifecycle;}
    public struct MineDisarmReply : NetworkMessage {public string mine,reason;public bool accepted;}
    // Every client plays the defuse sound where the mine was (its removal alone does not say why it disappeared).
    public struct MineDisarmedMessage : NetworkMessage {public string mine,definition,level;public float x,y;}
    // A player's mine: the client releases the held mine and finds where it falls (its own actor's hold); the host checks
    // the stack and spends it. drop is the fall from the release point, drawn by every client.
    // Telekinesis of a lying mine: the holding client moves it and sends poses; the host relays them and owns the state.
    public struct MineGrabRequest : NetworkMessage {public string world,actor,mine;public bool release;public int generation,lifecycle;public float x,y,drop;}
    public struct MinePoseMessage : NetworkMessage {public string mine,holder,level;public uint sequence;public bool released;public float x,y,drop;}
    public struct MinePlaceRequest : NetworkMessage {public string transaction,world,actor,level,item;public int generation,lifecycle;public float x,y,drop;}

    // The host simulates the mines of levels with a connected player (Mine.control): an armed mine checks its zone every
    // SensePeriod, a triggered one counts down and explodes as its weapon through the common explosion path (persisted
    // ExplosionState, each player decides its own contact, the host lists the non-player actors hit).
    public sealed class SessionMines
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,float> senseClocks=new(StringComparer.Ordinal);   // host-local, not saved
        private readonly List<MineState> exploding=new(),lost=new();
        private string pending;
        private double deadline;
        public int Explosions {get;private set;}
        public int Disarmed {get;private set;}
        public bool Pending=>pending!=null;
        public event Action<MineDisarmedMessage> DisarmHeard;
        public SessionMines(CoopSession session) {this.session=session;}
        public void ResetServer() {senseClocks.Clear();exploding.Clear();heldClocks.Clear();receivedPoses.Clear();}
        // A new session: nobody holds anything any more (a saved holder is a stale hold).
        public static void BeginSession(WorldState world) {foreach(var mine in world.mines) mine.holder=null;}

        public void RequestGrab(MineState mine,bool release,Vector2 position=default,float drop=0)
        {
            var actor=session.LocalActor;
            if(actor==null || mine==null || !NetworkClient.isConnected || session.World==null) return;
            NetworkClient.Send(new MineGrabRequest {world=session.World.worldId,actor=actor.id,mine=mine.id,release=release,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,x=position.x,y=position.y,drop=drop});
        }
        public void SendPose(MineState mine,Vector2 position)
        {
            var actor=session.LocalActor;if(actor==null || !NetworkClient.isConnected) return;
            sentPoses.TryGetValue(mine.id,out uint sequence);sentPoses[mine.id]=++sequence;
            var message=new MinePoseMessage {mine=mine.id,holder=actor.id,level=mine.level,sequence=sequence,x=position.x,y=position.y};
            PoseHeard?.Invoke(message);   // the holder draws its own pose at once
            NetworkClient.Send(message,Channels.Unreliable);
        }
        private void ReceiveGrab(NetworkConnectionToClient connection,MineGrabRequest request)
        {
            var world=session.ServerWorld;var actor=session.ConnectedActor(connection);
            var mine=world?.mines.Find(m=>m.id==request.mine);
            if(world==null || request.world!=world.worldId || actor==null || request.actor!=actor.id || mine==null ||
                actor.generation!=request.generation || actor.lifecycleSequence!=request.lifecycle) return;
            if(request.release)
            {
                if(mine.holder!=actor.id) return;
                // The rest point lies straight below the last relayed pose (the client dropped it through its geometry).
                var rest=new Vector2(request.x,request.y);
                bool valid=float.IsFinite(rest.x) && float.IsFinite(rest.y) && float.IsFinite(request.drop) && request.drop>=0 &&
                    Mathf.Abs(rest.x-mine.x)<=1 && mine.y-rest.y>=-1 && mine.y-rest.y<=MineRules.PlaceDrop+1;
                if(!valid) Debug.LogWarning($"Некорректное отпускание мины: мина={mine.id}, actor={actor.id}, точка={rest}, последняя={mine.Position}, падение={request.drop}; мина остаётся на месте.");
                Release(mine,valid?rest:mine.Position,valid?request.drop:0);
                return;
            }
            var profile=MineRules.Profile(mine);
            string reason=mine.holder!=null?"Мину уже удерживает другой игрок.":
                session.Pause.IsServerPaused(mine.level)?"Локация на паузе.":
                !MineRules.Visible(mine,actor,profile)?"Мина не видна.":
                session.Rules.Telekinesis.Rejection(actor,MineRules.TelekinesisBody(mine,profile),false);
            if(reason!=null) {connection.Send(new NoticeMessage {text=reason});return;}
            mine.holder=actor.id;heldClocks.Remove(mine.id);receivedPoses.Remove(mine.id);
            WorldChanges.Mark(world,mine);session.CommitWorld();
        }
        private void ReceivePose(NetworkConnectionToClient connection,MinePoseMessage message)
        {
            var world=session.ServerWorld;var actor=session.ConnectedActor(connection);
            var mine=world?.mines.Find(m=>m.id==message.mine);
            if(actor==null || mine==null || mine.holder!=actor.id || message.holder!=actor.id || message.released) return;
            receivedPoses.TryGetValue(mine.id,out uint last);
            if(last!=0 && unchecked((int)(message.sequence-last))<=0) return;
            var position=new Vector2(message.x,message.y);
            var body=MineRules.TelekinesisBody(mine,MineRules.Profile(mine));body.x=position.x;body.y=position.y;
            if(!float.IsFinite(position.x) || !float.IsFinite(position.y) || session.Rules.Telekinesis.Rejection(actor,body,true) is {} reason)
            {Debug.LogWarning($"Положение мины в телекинезе отклонено: мина={mine.id}, actor={actor.id}, точка={position}.");return;}
            receivedPoses[mine.id]=message.sequence;
            mine.x=position.x;mine.y=position.y;WorldChanges.Mark(world,mine);
            message.level=mine.level;
            NetworkServer.SendToAll(message,Channels.Unreliable);
        }
        private void Release(MineState mine,Vector2 rest,float drop)
        {
            mine.holder=null;mine.x=rest.x;mine.y=rest.y;heldClocks.Remove(mine.id);receivedPoses.Remove(mine.id);
            WorldChanges.Mark(session.ServerWorld,mine);session.CommitWorld();
            NetworkServer.SendToAll(new MinePoseMessage {mine=mine.id,level=mine.level,released=true,x=rest.x,y=rest.y,drop=drop});
        }
        // The holder left, fell or changed level: the mine drops from where it was.
        private void LoseHolder(MineState mine)
        {
            var room=Geometry?.Invoke(mine.level);
            if(room!=null && MineRules.TryDrop(room,mine.Position,MineRules.Profile(mine),out var rest)) {Release(mine,rest,Mathf.Max(0,mine.y-rest.y));return;}
            Debug.LogWarning($"Мина {mine.id} потеряла удерживающего {mine.holder}, но геометрия уровня {mine.level} недоступна или опоры нет; мина остаётся на месте.");
            Release(mine,mine.Position,0);
        }
        public event Action<MinePlaceRequest> PlaceHeard;
        public event Action<MinePoseMessage> PoseHeard;
        // Host-only: the level geometry a mine dropped by a lost holder falls through (set by the runtime).
        public Func<string,RoomLayout> Geometry {get;set;}
        private readonly Dictionary<string,float> heldClocks=new(StringComparer.Ordinal);
        private readonly Dictionary<string,uint> sentPoses=new(StringComparer.Ordinal),receivedPoses=new(StringComparer.Ordinal);
        private readonly System.Random chance=new();
        public void RegisterServer()
        {
            NetworkServer.RegisterHandler<MineDisarmRequest>(MessageTiming.Server<MineDisarmRequest>(Receive));
            NetworkServer.RegisterHandler<MinePlaceRequest>(MessageTiming.Server<MinePlaceRequest>(ReceivePlace));
            NetworkServer.RegisterHandler<MineGrabRequest>(MessageTiming.Server<MineGrabRequest>(ReceiveGrab));
            NetworkServer.RegisterHandler<MinePoseMessage>(MessageTiming.Server<MinePoseMessage>(ReceivePose));
        }
        public void RegisterClient()
        {
            pending=null;
            NetworkClient.RegisterHandler<MineDisarmReply>(MessageTiming.Client<MineDisarmReply>(reply=>
            {
                if(reply.mine!=pending) return;
                pending=null;if(!reply.accepted) session.ShowNotice(reply.reason);
            }));
            NetworkClient.RegisterHandler<MineDisarmedMessage>(MessageTiming.Client<MineDisarmedMessage>(message=>DisarmHeard?.Invoke(message)));
            NetworkClient.RegisterHandler<MinePlaceRequest>(MessageTiming.Client<MinePlaceRequest>(message=>PlaceHeard?.Invoke(message)));
            NetworkClient.RegisterHandler<MinePoseMessage>(MessageTiming.Client<MinePoseMessage>(message=>
            {
                // The holder already drew its own pose; the host's relay of it is older.
                if(!message.released && message.holder!=null && message.holder==session.LocalActor?.id) return;
                PoseHeard?.Invoke(message);
            }));
        }

        // Disarming (interim rule: always succeeds) turns the mine into its item lying where it was, as the original's
        // remine (LootGen.lootId at the mine); the player picks it up through the common loose-item protocol.
        public bool RequestDisarm(string mineId)
        {
            var actor=session.LocalActor;var mine=session.World?.mines.Find(m=>m.id==mineId);
            if(actor==null || !NetworkClient.isConnected) return false;
            if(Pending) {session.ShowNotice("Обезвреживание ещё подтверждается…");return false;}
            if(session.Pause.IsPaused(actor.level)) {session.ShowNotice("Локация на паузе.");return false;}
            string reason=mine==null?"Мина больше недоступна.":MineRules.DisarmRejection(mine,actor,MineRules.Profile(mine));
            if(reason!=null) {session.ShowNotice(reason);return false;}
            pending=mine.id;deadline=Time.unscaledTimeAsDouble+10;
            session.PublishActor(actor);
            NetworkClient.Send(new MineDisarmRequest {world=session.World.worldId,actor=actor.id,mine=mine.id,generation=actor.generation,lifecycle=actor.lifecycleSequence});
            return true;
        }
        // Whether the actor holds a mine in telekinesis (an ordinary held item that places a mine).
        public static bool HoldsMine(ActorState actor)=>MineRules.PlacedWeapon(HeldItemSelection.Selected(actor)?.definition)!=null;

        // Original WThrow.shoot (throwtip 1): the mine appears where it was held, without speed, and falls onto the
        // support below.
        public bool RequestPlace(RoomLayout room)
        {
            var actor=session.LocalActor;
            if(actor==null || actor.down || room==null || !NetworkClient.isConnected || session.World==null) return false;
            if(session.Pause.IsPaused(actor.level)) {session.ShowNotice("Локация на паузе.");return false;}
            var stack=HeldItemSelection.Selected(actor);string weapon=MineRules.PlacedWeapon(stack?.definition);
            if(weapon==null || InventoryAvailability.Count(actor,stack)<=0) {session.ShowNotice("Удерживаемая мина закончилась. Выберите другой предмет.");return false;}
            if(!actor.heldItemPoseValid) {session.ShowNotice("Мину нельзя отпустить: точка удержания перекрыта.");return false;}
            var profile=MineRules.Profile(new MineState {id=stack.id,definition=weapon});
            // The held mine is drawn with its feet at the hold point (HeldItemVisuals: world-scale artwork).
            var release=Remains.Gameplay.HeldItemPose.Mount(actor);
            if(!MineRules.TryDrop(room,release,profile,out var point)) {session.ShowNotice("Под миной нет опоры.");return false;}
            session.PublishActor(actor);
            NetworkClient.Send(new MinePlaceRequest {transaction=Guid.NewGuid().ToString("N"),world=session.World.worldId,actor=actor.id,level=actor.level,
                item=stack.id,generation=actor.generation,lifecycle=actor.lifecycleSequence,x=point.x,y=point.y,drop=Mathf.Max(0,release.y-point.y)});
            return true;
        }
        private void ReceivePlace(NetworkConnectionToClient connection,MinePlaceRequest request)
        {
            var world=session.ServerWorld;var actor=session.ConnectedActor(connection);
            string reason=world==null || request.world!=world.worldId || actor==null || request.actor!=actor.id || actor.down || actor.level!=request.level ||
                actor.generation!=request.generation || actor.lifecycleSequence!=request.lifecycle?"Мина не поставлена: мир или персонаж изменился.":
                session.Pause.IsServerPaused(actor.level)?"Мина не поставлена: локация на паузе.":
                string.IsNullOrWhiteSpace(request.transaction) || world.completedTransactions.Contains(request.transaction)?"Мина уже поставлена.":
                !float.IsFinite(request.x) || !float.IsFinite(request.y) || !float.IsFinite(request.drop) || request.drop<0 || request.drop>MineRules.PlaceDrop+MineRules.PlaceReach || Mathf.Abs(request.x-actor.x)>MineRules.PlaceReach ||
                request.y>actor.y+MineRules.PlaceReach || request.y<actor.y-MineRules.PlaceDrop-MineRules.PlaceReach?"Мина не поставлена: точка далеко от персонажа.":null;
            var stack=reason==null?actor.inventory.Find(item=>item.id==InventoryStacks.ResolveId(actor.inventoryAliases,request.item)):null;
            string weapon=MineRules.PlacedWeapon(stack?.definition);
            if(reason==null && weapon==null) reason="Мина не поставлена: в инвентаре нет этой мины.";
            if(reason==null && !SharedInventory.Consume(actor,stack.definition,request.item,request.transaction)) reason="Мина не поставлена: стопка недоступна.";
            if(reason!=null) {connection.Send(new NoticeMessage {text=reason});return;}
            world.completedTransactions.Add(request.transaction);actor.revision++;
            var mine=new MineState {id="placed:"+request.transaction,level=actor.level,definition=weapon,side=MineRules.PlayerSide,author=actor.id,
                x=request.x,y=request.y,arming=MineRules.PlacedArmingSeconds};
            MineRules.Validate(mine);
            world.mines.Add(mine);Placed++;
            session.CommitWorld();
            NetworkServer.SendToAll(request);
        }
        public int Placed {get;private set;}

        public void StepClient()
        {
            if(!Pending || Time.unscaledTimeAsDouble<deadline) return;
            Debug.LogWarning($"Подтверждение обезвреживания не получено: мир={session.World?.worldId}, мина={pending}.");
            pending=null;session.ShowNotice("Подтверждение обезвреживания не получено. Проверьте подключение.");
        }
        private void Receive(NetworkConnectionToClient connection,MineDisarmRequest request)
        {
            var world=session.ServerWorld;var actor=session.ConnectedActor(connection);
            var mine=world?.mines.Find(m=>m.id==request.mine);
            string reason=world==null || request.world!=world.worldId || actor==null || request.actor!=actor.id ||
                actor.generation!=request.generation || actor.lifecycleSequence!=request.lifecycle?"Обезвреживание отменено: мир или персонаж изменился.":
                mine==null?"Мина больше недоступна.":session.Pause.IsServerPaused(mine.level)?"Локация на паузе.":
                MineRules.DisarmRejection(mine,actor,MineRules.Profile(mine));
            if(reason!=null) {connection.Send(new MineDisarmReply {mine=request.mine,reason=reason});return;}
            string itemId="drop:mine:"+mine.id;
            if(WorldProps.Find(world,itemId)!=null) throw new InvalidOperationException("Предмет обезвреженной мины уже существует: "+itemId);
            var item=ExportedContentCatalog.Load().CreateFormalItem("weapon:"+mine.definition,0,1);
            var size=DroppedItemTransfer.Size(item.definition);
            world.mines.Remove(mine);senseClocks.Remove(mine.id);
            world.containers.Add(new ContainerState {id=itemId,definition="loose-item",presentationItem=item.definition,level=mine.level,
                x=mine.x,y=mine.y+size.y*.5f,width=size.x,height=size.y,anchorHeight=size.y*.5f,
                mass=.1f,generation=1,items=new List<ItemStack> {item}});
            Disarmed++;
            session.CommitWorld();
            connection.Send(new MineDisarmReply {mine=mine.id,accepted=true});
            NetworkServer.SendToAll(new MineDisarmedMessage {mine=mine.id,definition=mine.definition,level=mine.level,x=mine.x,y=mine.y});
        }

        public void Step(float dt)
        {
            var world=session.ServerWorld;
            if(!NetworkServer.active || world==null || world.mines.Count==0 || dt<=0) return;
            bool triggered=false;exploding.Clear();lost.Clear();
            foreach(var mine in world.mines)
            {
                if(!LevelActivity.HasConnectedPlayer(world,mine.level) || session.Pause.IsServerPaused(mine.level)) continue;
                var profile=MineRules.Profile(mine);
                if(mine.holder!=null)
                {
                    var holder=world.Find(mine.holder);
                    if(holder==null || !holder.connected || holder.down || holder.level!=mine.level) {lost.Add(mine);continue;}
                    if(MineRules.CanTriggerHeld(mine))
                    {
                        heldClocks.TryGetValue(mine.id,out float held);held+=dt;
                        while(held>=MineRules.HeldTriggerPeriod && !mine.triggered)
                        {
                            held-=MineRules.HeldTriggerPeriod;
                            if(chance.NextDouble()<MineRules.HeldTriggerChance) {MineRules.Trigger(mine,profile);WorldChanges.Mark(world,mine);triggered=true;}
                        }
                        heldClocks[mine.id]=held;
                    }
                }
                if(mine.triggered)
                {
                    mine.remaining=Mathf.Max(0,mine.remaining-dt);WorldChanges.Mark(world,mine);
                    if(mine.remaining<=0) exploding.Add(mine);
                    continue;
                }
                if(mine.arming>0)
                {
                    mine.arming=Mathf.Max(0,mine.arming-dt);WorldChanges.Mark(world,mine);
                    if(mine.arming<=0) triggered=true;   // armed: the view changes its frame
                    continue;
                }
                if(mine.holder!=null) continue;   // a held mine reacts to telekinesis, not to its zone
                senseClocks.TryGetValue(mine.id,out float clock);clock+=dt;
                if(clock<MineRules.SensePeriod) {senseClocks[mine.id]=clock;continue;}
                senseClocks[mine.id]=clock%MineRules.SensePeriod;
                foreach(var actor in world.actors)
                    if(MineRules.Senses(mine,actor,profile))
                    {MineRules.Trigger(mine,profile);WorldChanges.Mark(world,mine);triggered=true;break;}
            }
            foreach(var mine in lost) LoseHolder(mine);
            foreach(var mine in exploding)
            {
                var attack=MineRules.Attack(mine,MineRules.Profile(mine));
                var hits=world.actors.Where(actor=>!actor.player && AttackScope.Includes(attack,actor) && HitGeometry.Contacts(actor,attack)).Select(actor=>actor.id).ToArray();
                var explosion=MineRules.Detonate(world,mine,hits);
                senseClocks.Remove(mine.id);Explosions++;
                session.CommitWorld();
                NetworkServer.SendToAll(new AttackMessage {json=StateJson.ToJson(explosion.attack)});
            }
            if(exploding.Count==0 && triggered) session.CommitWorldAtFrameEnd();
        }
    }
}
