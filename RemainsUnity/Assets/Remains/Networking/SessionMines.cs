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
    public struct MinePlaceRequest : NetworkMessage {public string transaction,world,actor,level,item;public int generation,lifecycle;public float x,y,drop;}

    // The host simulates the mines of levels with a connected player (Mine.control): an armed mine checks its zone every
    // SensePeriod, a triggered one counts down and explodes as its weapon through the common explosion path (persisted
    // ExplosionState, each player decides its own contact, the host lists the non-player actors hit).
    public sealed class SessionMines
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,float> senseClocks=new(StringComparer.Ordinal);   // host-local, not saved
        private readonly List<MineState> exploding=new();
        private string pending;
        private double deadline;
        public int Explosions {get;private set;}
        public int Disarmed {get;private set;}
        public bool Pending=>pending!=null;
        public event Action<MineDisarmedMessage> DisarmHeard;
        public SessionMines(CoopSession session) {this.session=session;}
        public void ResetServer() {senseClocks.Clear();exploding.Clear();}
        public event Action<MinePlaceRequest> PlaceHeard;
        public void RegisterServer()
        {
            NetworkServer.RegisterHandler<MineDisarmRequest>(MessageTiming.Server<MineDisarmRequest>(Receive));
            NetworkServer.RegisterHandler<MinePlaceRequest>(MessageTiming.Server<MinePlaceRequest>(ReceivePlace));
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
            var release=Remains.Gameplay.HeldItemPose.Mount(actor)-Vector2.up*(profile.height*.5f);
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
            world.mines.Remove(mine);senseClocks.Remove(mine.id);
            world.containers.Add(new ContainerState {id=itemId,definition="loose-item",presentationItem=item.definition,level=mine.level,
                x=mine.x,y=mine.y,width=DroppedItemTransfer.Width,height=DroppedItemTransfer.Height,anchorHeight=DroppedItemTransfer.Height*.5f,
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
            bool triggered=false;exploding.Clear();
            foreach(var mine in world.mines)
            {
                if(!LevelActivity.HasConnectedPlayer(world,mine.level) || session.Pause.IsServerPaused(mine.level)) continue;
                var profile=MineRules.Profile(mine);
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
                senseClocks.TryGetValue(mine.id,out float clock);clock+=dt;
                if(clock<MineRules.SensePeriod) {senseClocks[mine.id]=clock;continue;}
                senseClocks[mine.id]=clock%MineRules.SensePeriod;
                foreach(var actor in world.actors)
                    if(MineRules.Senses(mine,actor,profile))
                    {MineRules.Trigger(mine,profile);WorldChanges.Mark(world,mine);triggered=true;break;}
            }
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
