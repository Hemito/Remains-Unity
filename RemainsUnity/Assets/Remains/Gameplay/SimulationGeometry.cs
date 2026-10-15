using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using Remains.Diagnostics;

namespace Remains.Gameplay
{
    // Rebuilt on world changes, never while stepping physics.
    public sealed class SimulationGeometry : IDisposable
    {
        private readonly Dictionary<string,RoomDoorGeometry> rooms=new();
        private readonly Func<string,RoomLayout> load;
        private readonly CoopSession session;
        private string worldId;
        private string roomDefinitions;
        private FactoryContentIdentity factoryDefinitions;
        private string stagedWorld,stagedDefinitions,stagedLevel;
        private FactoryContentIdentity stagedFactories;
        private RoomDoorGeometry stagedEntry;
        private bool stagedReady;
        public long TopologyVersion {get;private set;}
        public event Action<string,RoomLayout,long> OcclusionChanged;
        public IEnumerable<string> LoadedLevels=>rooms.Keys;
        public SimulationGeometry(Func<string,RoomLayout> load)=>this.load=load??throw new ArgumentNullException(nameof(load));
        public SimulationGeometry(CoopSession session):this(level=>RoomLayout.Load(level,session.World))
        {this.session=session??throw new ArgumentNullException(nameof(session));session.Changed+=OnSessionChanged;}
        private void OnSessionChanged()=>Reconcile(session.World,session.LastChange);
        public void Dispose()
        {
            if(session!=null) session.Changed-=OnSessionChanged;
            rooms.Clear();ClearEntryPreparation();OcclusionChanged=null;
        }
        // Preparation has no live world or simulation owner. The same reviewed geometry
        // is adopted only after the ordinary Welcome publishes the matching world.
        public RoomLayout PrepareEntry(WorldState viewWorld,string level)
        {
            if(viewWorld==null || string.IsNullOrWhiteSpace(viewWorld.worldId) || string.IsNullOrWhiteSpace(level))
                throw new ArgumentException("Подготовка входа: не указаны мир или локация.");
            string definitions=Remains.Serialization.StateJson.ToJson(viewWorld.rooms);
            if(stagedEntry==null || stagedWorld!=viewWorld.worldId || stagedDefinitions!=definitions || stagedLevel!=level || stagedFactories?.Matches(viewWorld)!=true)
            {
                stagedEntry=new RoomDoorGeometry(RoomLayout.Load(level,viewWorld));
                stagedWorld=viewWorld.worldId;stagedDefinitions=definitions;stagedLevel=level;
                stagedFactories=FactoryContentIdentity.Capture(viewWorld);
            }
            stagedReady=false;stagedEntry.Reconcile(viewWorld.doors,level);
            return stagedEntry.Room;
        }
        public long EntryOcclusionRevision(RoomLayout expected)
        {
            RequireEntry(expected);return stagedEntry.OcclusionRevision;
        }
        public void MarkEntryPrepared(RoomLayout expected) {RequireEntry(expected);stagedReady=true;}
        private void RequireEntry(RoomLayout expected)
        {
            if(stagedEntry==null || !ReferenceEquals(stagedEntry.Room,expected))
                throw new InvalidOperationException("Подготовка входа ссылается на другую геометрию локации.");
        }
        public void ClearEntryPreparation()
        {stagedEntry=null;stagedWorld=stagedDefinitions=stagedLevel=null;stagedFactories=null;stagedReady=false;}
        // changes: what the notification changed; door geometry of a loaded level is re-checked only for doors.
        public void Reconcile(WorldState world,WorldChangeKind changes=WorldChangeKind.All)
        {
            using var timing=TransitionTiming.Measure("geometry.reconcile",world?.worldId);
            string definitions=Remains.Serialization.StateJson.ToJson(world?.rooms);
            if(worldId!=world?.worldId || roomDefinitions!=definitions || factoryDefinitions?.Matches(world)!=true)
            {rooms.Clear();worldId=world?.worldId;roomDefinitions=definitions;factoryDefinitions=FactoryContentIdentity.Capture(world);TopologyVersion++;}
            var required=world==null?new HashSet<string>():new HashSet<string>(world.actors.Where(a=>a.player && a.connected).Select(a=>a.level));
            if(stagedEntry!=null && world!=null && required.Contains(stagedLevel))
            {
                if(!stagedReady || stagedWorld!=world.worldId || stagedDefinitions!=definitions || stagedFactories?.Matches(world)!=true)
                    throw new InvalidOperationException($"Вход завершён до готовности согласованной геометрии: world={world.worldId}, level={stagedLevel}, ready={stagedReady}.");
                rooms[stagedLevel]=stagedEntry;ClearEntryPreparation();TopologyVersion++;
            }
            foreach(string level in rooms.Keys.Where(level=>!required.Contains(level)).ToArray()) {rooms.Remove(level);TopologyVersion++;}
            foreach(string level in required)
            {
                bool created=false;
                if(!rooms.TryGetValue(level,out var geometry)) {geometry=new RoomDoorGeometry(load(level));rooms.Add(level,geometry);TopologyVersion++;created=true;}
                if((created || changes.Any(WorldChangeKind.Doors)) && geometry.Reconcile(world.doors,level)) OcclusionChanged?.Invoke(level,geometry.Room,geometry.OcclusionRevision);
            }
        }
        public RoomLayout Get(string level)=>rooms.TryGetValue(level,out var room)?room.Room:
            throw new InvalidOperationException($"Не загружена геометрия активной локации: level={level}.");
        public RoomLayout Find(string level)=>level!=null && rooms.TryGetValue(level,out var room)?room.Room:null;
        public long OcclusionRevision(string level)=>rooms.TryGetValue(level,out var room)?room.OcclusionRevision:
            throw new InvalidOperationException($"Не загружены препятствия активной локации: level={level}.");
    }
}
