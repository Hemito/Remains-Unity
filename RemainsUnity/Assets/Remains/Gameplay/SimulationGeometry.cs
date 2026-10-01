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
        private readonly Dictionary<string,RoomLayout> rooms=new();
        public IEnumerable<string> LoadedLevels=>rooms.Keys;
        public SimulationGeometry(CoopSession session)
        {session.Changed+=()=>Reconcile(session.World);}
        private void Reconcile(WorldState world)
        {
            var required=world==null?new HashSet<string>():new HashSet<string>(world.actors.Where(a=>a.player && a.connected).Select(a=>a.level));
            foreach(string level in rooms.Keys.Where(level=>!required.Contains(level)).ToArray()) rooms.Remove(level);
            foreach(string level in required) if(!rooms.ContainsKey(level)) rooms.Add(level,RoomLayout.Load(level));
        }
        public RoomLayout Get(string level)=>rooms.TryGetValue(level,out var room)?room:
            throw new InvalidOperationException($"Не загружена геометрия активной локации: level={level}.");
    }
}
