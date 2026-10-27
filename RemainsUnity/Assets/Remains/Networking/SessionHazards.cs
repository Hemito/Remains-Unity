using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Networking
{
    public struct HazardActionRequest : NetworkMessage {public string world,actor,hazard;public int generation,lifecycle;}
    public struct HazardActionReply : NetworkMessage {public string hazard,reason;public bool accepted;}
    // What happened to a trap or charge, for every client's sound and animation (a removal alone does not say why).
    public struct HazardEventMessage : NetworkMessage {public string hazard,definition,level,kind;public float x,y;}
    public struct HazardHitRequest : NetworkMessage {public string json;}
    public struct HazardHitRejected : NetworkMessage {public string hazard,reason;}
    [Serializable] public sealed class HazardHitRequestData : AttackContactRequest {public string hazard;}

    // The host simulates the traps and charges of levels with a connected player (UnitTrap/UnitDamager.control): an
    // open trap checks the actors every TrapSensePeriod and bites the first that springs it, breaking itself; damage
    // from attacks and explosions wears hazards down, and a destroyed charge explodes through the common explosion path
    // (its blast damages the hazards around it in turn). Players disarm and rearm through requests the host checks.
    public sealed class SessionHazards
    {
        public const string Snap="snap",Rearm="rearm",Disarm="disarm",Bite="bite",Break="break";
        private readonly CoopSession session;
        private readonly Dictionary<string,float> senseClocks=new(StringComparer.Ordinal);   // host-local, not saved
        private readonly List<(HazardState trap,ActorState victim)> bites=new();
        private readonly Queue<(HazardState hazard,float amount)> damage=new();
        private readonly DoorDamageAttackLedger ledger=new();
        private readonly HashSet<string> resolvedHits=new(StringComparer.Ordinal);private readonly Queue<string> resolvedOrder=new();
        private readonly HashSet<string> reported=new(StringComparer.Ordinal);private string clientWorld;
        private string pending;
        private double deadline;
        public int Bites {get;private set;}
        public int Explosions {get;private set;}
        public int Actions {get;private set;}
        public int Hits {get;private set;}
        public bool Pending=>pending!=null;
        public event Action<HazardEventMessage> EventHeard;
        public SessionHazards(CoopSession session) {this.session=session;}
        public void ResetServer() {senseClocks.Clear();bites.Clear();damage.Clear();ledger.Clear();resolvedHits.Clear();resolvedOrder.Clear();}

        public void RegisterServer()
        {
            ResetServer();
            NetworkServer.RegisterHandler<HazardActionRequest>(MessageTiming.Server<HazardActionRequest>(ReceiveAction));
            NetworkServer.RegisterHandler<HazardHitRequest>(MessageTiming.Server<HazardHitRequest>(ReceiveHit));
        }
        public void RegisterClient()
        {
            pending=null;reported.Clear();clientWorld=null;
            NetworkClient.RegisterHandler<HazardActionReply>(MessageTiming.Client<HazardActionReply>(reply=>
            {
                if(reply.hazard!=pending) return;
                pending=null;if(!reply.accepted) session.ShowNotice(reply.reason);
            }));
            NetworkClient.RegisterHandler<HazardEventMessage>(MessageTiming.Client<HazardEventMessage>(message=>EventHeard?.Invoke(message)));
            NetworkClient.RegisterHandler<HazardHitRejected>(MessageTiming.Client<HazardHitRejected>(reply=>
                Debug.LogWarning($"Контакт с ловушкой отклонён: ловушка={reply.hazard}, причина={reply.reason}.")));
        }

        // ---- Disarming and rearming --------------------------------------------------------------------------------
        public bool RequestAction(string hazardId)
        {
            var actor=session.LocalActor;var hazard=session.World?.hazards.Find(h=>h.id==hazardId);
            if(actor==null || !NetworkClient.isConnected) return false;
            if(Pending) {session.ShowNotice("Действие с ловушкой ещё подтверждается…");return false;}
            if(session.Pause.IsPaused(actor.level)) {session.ShowNotice("Локация на паузе.");return false;}
            string reason=hazard==null?"Ловушка больше недоступна.":HazardRules.DisarmRejection(hazard,actor,HazardRules.Profile(hazard));
            if(reason!=null) {session.ShowNotice(reason);return false;}
            pending=hazard.id;deadline=Time.unscaledTimeAsDouble+10;
            session.PublishActor(actor);
            NetworkClient.Send(new HazardActionRequest {world=session.World.worldId,actor=actor.id,hazard=hazard.id,generation=actor.generation,lifecycle=actor.lifecycleSequence});
            return true;
        }
        public void StepClient()
        {
            if(!Pending || Time.unscaledTimeAsDouble<deadline) return;
            Debug.LogWarning($"Подтверждение действия с ловушкой не получено: мир={session.World?.worldId}, ловушка={pending}.");
            pending=null;session.ShowNotice("Подтверждение не получено. Проверьте подключение.");
        }
        private void ReceiveAction(NetworkConnectionToClient connection,HazardActionRequest request)
        {
            var world=session.ServerWorld;var actor=session.ConnectedActor(connection);
            var hazard=world?.hazards.Find(h=>h.id==request.hazard);
            string reason=world==null || request.world!=world.worldId || actor==null || request.actor!=actor.id ||
                actor.generation!=request.generation || actor.lifecycleSequence!=request.lifecycle?"Действие отменено: мир или персонаж изменился.":
                hazard==null?"Ловушка больше недоступна.":session.Pause.IsServerPaused(hazard.level)?"Локация на паузе.":
                HazardRules.DisarmRejection(hazard,actor,HazardRules.Profile(hazard));
            if(reason!=null) {connection.Send(new HazardActionReply {hazard=request.hazard,reason=reason});return;}
            var profile=HazardRules.Profile(hazard);string kind;
            if(profile.kind==HazardRules.Trap && !hazard.closed)
            {
                // UnitTrap.disarm on an open trap: klac — it snaps shut without biting anyone.
                hazard.closed=true;kind=Snap;WorldChanges.Mark(world,hazard);
            }
            else if(profile.kind==HazardRules.Trap)
            {
                // A shut trap rearmed: it is the player's from now on (fraction F_PLAYER, rearm saved).
                hazard.closed=false;hazard.side=MineRules.PlayerSide;hazard.author=actor.id;kind=Rearm;
                senseClocks.Remove(hazard.id);WorldChanges.Mark(world,hazard);
            }
            else
            {
                // UnitDamager.disarm (tip 3): removed; the dynamite lies where it stood (LootGen.lootCont "bomb").
                List<ContainerState> drops;
                try {drops=PrepareDisarmDrops(world,hazard,profile);}
                catch(InvalidOperationException error)
                {
                    Debug.LogError($"Обезвреживание отменено: world={world.worldId}, hazard={hazard.id}, item={profile.disarmItem}. {error.Message}");
                    connection.Send(new HazardActionReply {hazard=hazard.id,reason="Не подготовлены данные добычи ловушки. Объект сохранён; подробности в журнале."});return;
                }
                world.hazards.Remove(hazard);world.containers.AddRange(drops);kind=Disarm;
            }
            Actions++;
            session.CommitWorld();
            connection.Send(new HazardActionReply {hazard=hazard.id,accepted=true});
            Announce(hazard,kind);
        }
        public static List<ContainerState> PrepareDisarmDrops(WorldState world,HazardState hazard,HazardDefinition profile,Func<string,Vector2> sizeForItem=null)
        {
            var drops=new List<ContainerState>();
            for(int i=0;i<profile.disarmCount;i++)
            {
                string itemId=$"drop:hazard:{hazard.id}:{i}";
                if(WorldProps.Find(world,itemId)!=null)throw new InvalidOperationException("Предмет обезвреженной ловушки уже существует: "+itemId);
                var item=ExportedContentCatalog.Load().CreateFormalItem(profile.disarmItem,0,1);
                var size=sizeForItem==null?DroppedItemTransfer.Size(item.definition):sizeForItem(item.definition);
                if(!float.IsFinite(size.x)||!float.IsFinite(size.y)||size.x<=0||size.y<=0)throw new InvalidOperationException("Неверный размер добычи ловушки: "+item.definition);
                float x=hazard.x+(i-(profile.disarmCount-1)*.5f)*size.x;
                drops.Add(new ContainerState {id=itemId,definition="loose-item",presentationItem=item.definition,level=hazard.level,
                    x=x,y=hazard.y+size.y*.5f,width=size.x,height=size.y,anchorHeight=size.y*.5f,mass=.1f,generation=1,items=new List<ItemStack> {item}});
            }
            return drops;
        }
        private void Announce(HazardState hazard,string kind)=>
            NetworkServer.SendToAll(new HazardEventMessage {hazard=hazard.id,definition=hazard.definition,level=hazard.level,kind=kind,x=hazard.x,y=hazard.y});

        // ---- Traps -------------------------------------------------------------------------------------------------
        public void Step(float dt)
        {
            var world=session.ServerWorld;
            if(!NetworkServer.active || world==null || world.hazards.Count==0 || dt<=0) return;
            bites.Clear();
            foreach(var hazard in world.hazards)
            {
                if(hazard.closed || !LevelActivity.HasConnectedPlayer(world,hazard.level) || session.Pause.IsServerPaused(hazard.level)) continue;
                var profile=HazardRules.Profile(hazard);
                if(profile.kind!=HazardRules.Trap) continue;
                senseClocks.TryGetValue(hazard.id,out float clock);clock+=dt;
                if(clock<HazardRules.TrapSensePeriod) {senseClocks[hazard.id]=clock;continue;}
                senseClocks[hazard.id]=clock%HazardRules.TrapSensePeriod;
                foreach(var actor in world.actors)
                    if(HazardRules.Springs(hazard,actor,profile)) {bites.Add((hazard,actor));break;}
            }
            if(bites.Count==0) return;
            var attacks=new List<AttackEvent>();
            foreach(var (trap,victim) in bites)
            {
                // The snap bites the victim and breaks the trap (no corpse, trup 0).
                attacks.Add(HazardRules.Bite(trap,HazardRules.Profile(trap),victim,session.LocalOwner));
                world.hazards.Remove(trap);senseClocks.Remove(trap.id);Bites++;
            }
            session.CommitWorld();
            foreach(var attack in attacks) NetworkServer.SendToAll(AttackMessage.From(attack));
            foreach(var (trap,_) in bites) Announce(trap,Bite);
        }

        // ---- Damage ------------------------------------------------------------------------------------------------
        // Host: an explosion (grenade, mine, charge) damages every hazard its blast reaches.
        public void Blast(AttackEvent blast)
        {
            var world=session.ServerWorld;
            if(world==null || blast==null) return;
            float amount=WeaponCatalog.Get(blast.weapon).damage*FactoryEnemyScaling.AttackFactor(blast);
            foreach(var hazard in world.hazards)
                if(HazardRules.InBlast(hazard,HazardRules.Profile(hazard),blast)) damage.Enqueue((hazard,amount));
            Resolve();
        }
        private void Resolve()
        {
            var world=session.ServerWorld;bool changed=false;
            while(damage.Count>0)
            {
                var (hazard,amount)=damage.Dequeue();
                if(!world.hazards.Contains(hazard)) continue;
                var profile=HazardRules.Profile(hazard);
                changed=true;WorldChanges.Mark(world,hazard);
                if(!HazardRules.Damage(hazard,profile,amount)) continue;
                if(profile.kind==HazardRules.Trap)
                {
                    world.hazards.Remove(hazard);senseClocks.Remove(hazard.id);
                    Announce(hazard,Break);
                    continue;
                }
                // A destroyed charge explodes (UnitDamager.dropLoot → iExpl); its blast reaches the hazards around it.
                var blast=HazardRules.Blast(hazard,profile);
                var hits=world.actors.Where(actor=>!actor.player && AttackScope.Includes(blast,actor) && HitGeometry.Contacts(actor,blast)).Select(actor=>actor.id).ToArray();
                var explosion=HazardRules.Detonate(world,hazard,hits);
                Explosions++;
                session.CommitWorld();
                NetworkServer.SendToAll(AttackMessage.From(explosion.attack));
                float blastDamage=WeaponCatalog.Get(explosion.attack.weapon).damage*FactoryEnemyScaling.AttackFactor(explosion.attack);
                foreach(var other in world.hazards)
                    if(HazardRules.InBlast(other,HazardRules.Profile(other),explosion.attack)) damage.Enqueue((other,blastDamage));
            }
            if(changed) session.CommitWorldAtFrameEnd();
        }

        // Client: an attack this client simulates touched hazards (as lamps: SessionLamps.ReportContacts).
        public void ReportContacts(AttackEvent attack,float limit,int projectileGeneration=0,Func<float,bool> reachable=null)
        {
            if(attack==null || session.World==null || !NetworkClient.isConnected || session.World.hazards.Count==0) return;
            var participant=session.LocalActor;var source=session.World.Find(attack.attacker);
            if(participant==null || participant.level!=attack.level || session.Pause.IsPaused(attack.level)) return;
            if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.IsExplosive(attack.weapon)) return;
            var weapon=WeaponCatalog.Get(attack.weapon);
            if(weapon.projectileSpeed>0)
            {
                var flight=session.World.projectiles.Find(value=>value.launch?.id==attack.id);
                if(flight==null || flight.owner!=session.LocalOwner || flight.generation!=projectileGeneration) return;
            }
            else if(source==null || source.owner!=session.LocalOwner || attack.sourceOwner!=session.LocalOwner) return;
            if(clientWorld!=session.World.worldId) {reported.Clear();clientWorld=session.World.worldId;}
            foreach(var hazard in session.World.hazards)
            {
                if(hazard.level!=attack.level) continue;
                var entry=SurfaceContact.Entry(HazardRules.Body(hazard,HazardRules.Profile(hazard)),attack.Origin,attack.End,weapon.projectileSpeed>0?0:attack.radius);
                if(!entry.HasValue || entry.Value>limit || reachable!=null && !reachable(entry.Value)) continue;
                if(!reported.Add(attack.id+"|"+hazard.id)) continue;
                NetworkClient.Send(new HazardHitRequest {json=StateJson.ToJson(new HazardHitRequestData {world=session.World.worldId,hazard=hazard.id,
                    decisionActor=participant.id,decisionGeneration=participant.generation,decisionLifecycle=participant.lifecycleSequence,
                    sourceGeneration=source?.generation??0,sourceLifecycle=source?.lifecycleSequence??0,projectileGeneration=projectileGeneration,
                    attack=StateJson.Copy(attack)})});
            }
        }
        // Host: an accepted melee or instant attack may later report hazard contacts (CoopSession.RelayAttack).
        internal string RecordAccepted(AttackEvent attack,ActorState source)
        {
            if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.IsExplosive(attack.weapon) || WeaponCatalog.Get(attack.weapon).projectileSpeed>0) return null;
            return ledger.Record(session.ServerWorld.worldId,source,attack);
        }
        private void ReceiveHit(NetworkConnectionToClient connection,HazardHitRequest message)
        {
            HazardHitRequestData request=null;
            try
            {
                request=StateJson.FromJson<HazardHitRequestData>(message.json);
                var world=session.ServerWorld;
                string reason=AttackContactRules.Validate(world,session.ConnectedActor(connection),request,ledger,"с ловушкой",null,out var weapon);
                if(reason!=null) throw new InvalidOperationException(reason);
                var hazard=world.hazards.Find(h=>h.id==request.hazard);
                if(hazard==null || hazard.level!=request.attack.level) return;   // already destroyed by another hit: nothing to do
                var profile=HazardRules.Profile(hazard);
                if(!SurfaceContact.Entry(HazardRules.Body(hazard,profile),request.attack.Origin,request.attack.End,weapon.projectileSpeed>0?0:request.attack.radius).HasValue)
                    throw new InvalidOperationException("Сегмент не касается ловушки.");
                if(session.Pause.IsServerPaused(hazard.level)) throw new InvalidOperationException("Локация ловушки на паузе.");
                if(!resolvedHits.Add(request.attack.id+"|"+hazard.id)) return;
                resolvedOrder.Enqueue(request.attack.id+"|"+hazard.id);if(resolvedOrder.Count>1024) resolvedHits.Remove(resolvedOrder.Dequeue());
                Hits++;
                damage.Enqueue((hazard,session.Rules.Damage.Calculate(request.attack.weapon,request.attack.speed)*FactoryEnemyScaling.AttackFactor(request.attack)));
                Resolve();
            }
            catch(Exception error) when(error is ArgumentException || error is InvalidOperationException || error is IOException)
            {
                connection.Send(new HazardHitRejected {hazard=request?.hazard,reason=error.Message});
                Debug.LogWarning($"Контакт с ловушкой отклонён: ловушка={request?.hazard}, атака={request?.attack?.id}, соединение={connection.connectionId}, причина={error.Message}.");
            }
        }
    }
}
