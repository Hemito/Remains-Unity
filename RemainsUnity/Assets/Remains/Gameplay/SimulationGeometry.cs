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
        public IEnumerable<string> LoadedLevels=>rooms.Keys;
        public SimulationGeometry(Func<string,RoomLayout> load)=>this.load=load??throw new ArgumentNullException(nameof(load));
        public SimulationGeometry(CoopSession session):this(RoomLayout.Load)
        {session.Changed+=()=>Reconcile(session.World);}
        public void Reconcile(WorldState world)
        {
            if(worldId!=world?.worldId) {rooms.Clear();worldId=world?.worldId;}
            var required=world==null?new HashSet<string>():new HashSet<string>(world.actors.Where(a=>a.player && a.connected).Select(a=>a.level));
            foreach(string level in rooms.Keys.Where(level=>!required.Contains(level)).ToArray()) rooms.Remove(level);
            foreach(string level in required)
            {
                if(!rooms.TryGetValue(level,out var geometry)) {geometry=new RoomDoorGeometry(load(level));rooms.Add(level,geometry);}
                geometry.Reconcile(world.doors,level);
            }
        }
        public RoomLayout Get(string level)=>rooms.TryGetValue(level,out var room)?room.Room:
            throw new InvalidOperationException($"Не загружена геометрия активной локации: level={level}.");
    }
}
