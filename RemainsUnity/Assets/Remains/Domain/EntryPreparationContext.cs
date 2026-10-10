using System;
using System.IO;
using System.Linq;
using Remains.Persistence;
using Remains.Serialization;

namespace Remains.Domain
{
    // A detached preparation view is never a playable or recoverable session world.
    public sealed class EntryPreparationContext
    {
        public string Token {get;}
        public string WorldId {get;}
        public string Level {get;}
        public string ActorId {get;}
        public string CharacterId {get;}
        public string RoomDefinitionsJson {get;}
        public string GeometryFingerprint {get;}
        public WorldState ViewWorld {get;}
        public EntryPreparationContext(string token,WorldState detachedView,string actorId,string characterId,string level)
        {
            if(!Guid.TryParseExact(token,"N",out _) || detachedView==null || detachedView.schema!=1 ||
                !Guid.TryParseExact(detachedView.worldId,"N",out _) || string.IsNullOrWhiteSpace(actorId) ||
                string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(level))
                throw new InvalidDataException($"Некорректный контекст подготовки входа: token={token}, world={detachedView?.worldId}, actor={actorId}, character={characterId}, level={level}.");
            var actor=detachedView.Find(actorId);
            if(actor==null || !actor.player || actor.connected || actor.character!=characterId || actor.level!=level)
                throw new InvalidDataException($"Подготовка входа содержит другое/подключённое тело: world={detachedView.worldId}, actor={actorId}, character={characterId}, level={level}.");
            Token=token;WorldId=detachedView.worldId;ActorId=actorId;CharacterId=characterId;Level=level;
            ViewWorld=detachedView;RoomDefinitionsJson=StateJson.ToJson(detachedView.rooms);
            GeometryFingerprint=Fingerprint(detachedView,level);
        }
        public bool Matches(WorldState world,string actorId,string level)=>world!=null && world.worldId==WorldId &&
            actorId==ActorId && level==Level && Fingerprint(world,level)==GeometryFingerprint;
        public void RequireWelcome(WorldState world,string actorId)
        {
            var actor=world?.Find(actorId);
            if(actor==null || !actor.connected || !actor.player || actor.character!=CharacterId ||
                !Matches(world,actorId,actor.level))
                throw new InvalidDataException($"Вход не соответствует подготовленной локации: token={Token}, world={world?.worldId}, actor={actorId}, level={actor?.level}, expected={WorldId}/{ActorId}/{Level}.");
        }
        public static string Fingerprint(WorldState world,string level)
        {
            if(world==null || world.doors==null) throw new InvalidDataException("Подготовка входа: отсутствует мир/список дверей.");
            string definition=WorldRooms.Definition(world,level);
            var doors=world.doors.Where(door=>door!=null && door.level==level).OrderBy(door=>door.id,StringComparer.Ordinal).ToArray();
            var ids=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach(var door in world.doors)
                if(door==null) throw new InvalidDataException("Подготовка входа: пустая запись двери мира "+world.worldId);
            foreach(var door in doors)
            {
                door.Validate();
                if(!ids.Add(door.id)) throw new InvalidDataException($"Подготовка входа: повтор ID двери {door.id}, world={world.worldId}, level={level}.");
            }
            // Body health/pose and clocks are intentionally absent. Room bindings (including
            // unknown extensions) and exact finite door states decide the warm-room identity.
            return RecoverySnapshot.Fingerprint(StateJson.ToJson(new GeometryIdentity {
                world=world.worldId,level=level,definition=definition,rooms=world.rooms,factories=world.factories,doors=doors}));
        }
        private sealed class GeometryIdentity
        {
            public string world,level,definition;
            public System.Collections.Generic.List<WorldRoom> rooms;
            public System.Collections.Generic.List<WorldFactory> factories;
            public DoorState[] doors;
        }
        public static WorldState NarrowView(WorldState detachedWorld,string actorId,string level)
        {
            if(detachedWorld==null) throw new ArgumentNullException(nameof(detachedWorld));
            var actor=detachedWorld.Find(actorId)??throw new InvalidDataException("Подготовка входа: нет preview actor "+actorId);
            detachedWorld.actors=new() {actor};
            detachedWorld.doors=detachedWorld.doors.Where(door=>door.level==level).ToList();
            detachedWorld.movingPlatforms=detachedWorld.movingPlatforms.Where(platform=>platform.level==level).ToList();
            detachedWorld.mechanismButtons=detachedWorld.mechanismButtons.Where(button=>button.level==level).ToList();
            detachedWorld.props.Clear();detachedWorld.containers.Clear();detachedWorld.container=null;
            detachedWorld.projectiles.Clear();detachedWorld.grenades.Clear();detachedWorld.explosions.Clear();
            detachedWorld.pendingGrants.Clear();detachedWorld.pendingDeposits.Clear();detachedWorld.completedTransactions.Clear();
            return detachedWorld;
        }
    }

    public sealed class EntryPreparationTicket
    {
        public const double LifetimeSeconds=90;
        public EntryPreparationContext Context {get;}
        public double Deadline {get;}
        public bool Consumed {get;private set;}
        public EntryPreparationTicket(EntryPreparationContext context,double now)
        {
            Context=context??throw new ArgumentNullException(nameof(context));
            if(!double.IsFinite(now) || now<0) throw new InvalidDataException("Подготовка входа: некорректное время reservation.");
            Deadline=now+LifetimeSeconds;
            if(!double.IsFinite(Deadline) || Deadline<=now) throw new InvalidDataException("Подготовка входа: deadline не продвигает время reservation.");
        }
        public bool Expired(double now)=>now>=Deadline;
        public void RequireReady(string token,string fingerprint,double now)
        {
            if(Consumed || !double.IsFinite(now) || now<0 || Expired(now) || token!=Context.Token || fingerprint!=Context.GeometryFingerprint)
                throw new InvalidDataException($"Готовность входа отклонена: token={token}, expected={Context.Token}, expired={Expired(now)}, consumed={Consumed}.");
        }
        public void Consume(string token,string fingerprint,double now)
        {RequireReady(token,fingerprint,now);Consumed=true;}
    }
}
