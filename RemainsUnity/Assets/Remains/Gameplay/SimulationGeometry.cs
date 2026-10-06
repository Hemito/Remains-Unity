using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Networking;

namespace Remains.Gameplay
{
    // Rebuilt on world changes, never while stepping physics.
    public sealed class SimulationGeometry
    {
        private readonly Dictionary<string,RoomDoorGeometry> rooms=new();
        private readonly Func<string,RoomLayout> load;
        private string worldId;
        private string roomDefinitions;
        public long TopologyVersion {get;private set;}
        public event Action<string,RoomLayout,long> OcclusionChanged;
        public IEnumerable<string> LoadedLevels=>rooms.Keys;
        public SimulationGeometry(Func<string,RoomLayout> load)=>this.load=load??throw new ArgumentNullException(nameof(load));
        public SimulationGeometry(CoopSession session):this(level=>RoomLayout.Load(level,session.World))
        {session.Changed+=()=>Reconcile(session.World);}
        public void Reconcile(WorldState world)
        {
            string definitions=Remains.Serialization.StateJson.ToJson(world?.rooms);
            if(worldId!=world?.worldId || roomDefinitions!=definitions)
            {rooms.Clear();worldId=world?.worldId;roomDefinitions=definitions;TopologyVersion++;}
            var required=world==null?new HashSet<string>():new HashSet<string>(world.actors.Where(a=>a.player && a.connected).Select(a=>a.level));
            foreach(string level in rooms.Keys.Where(level=>!required.Contains(level)).ToArray()) {rooms.Remove(level);TopologyVersion++;}
            foreach(string level in required)
            {
                if(!rooms.TryGetValue(level,out var geometry)) {geometry=new RoomDoorGeometry(load(level));rooms.Add(level,geometry);TopologyVersion++;}
                if(geometry.Reconcile(world.doors,level)) OcclusionChanged?.Invoke(level,geometry.Room,geometry.OcclusionRevision);
            }
        }
        public RoomLayout Get(string level)=>rooms.TryGetValue(level,out var room)?room.Room:
            throw new InvalidOperationException($"Не загружена геометрия активной локации: level={level}.");
        public long OcclusionRevision(string level)=>rooms.TryGetValue(level,out var room)?room.OcclusionRevision:
            throw new InvalidOperationException($"Не загружены препятствия активной локации: level={level}.");
    }
}
