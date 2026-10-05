using System;
using Remains.Domain;

namespace Remains.Gameplay
{
    // Free physical objects follow their owner and active level, not player input.
    public static class FreePropMotion
    {
        public static int Step(WorldState world,string localOwner,float dt,Func<string,RoomLayout> roomForLevel,
            Func<string,bool> isPaused,Action<PropState> publish=null,PassengerMotionRegistry passengers=null)
        {
            if(world==null || dt<=0) return 0;
            if(string.IsNullOrEmpty(localOwner)) throw new ArgumentException("Не задан владелец симуляции свободных объектов.",nameof(localOwner));
            if(roomForLevel==null) throw new ArgumentNullException(nameof(roomForLevel));
            if(isPaused==null) throw new ArgumentNullException(nameof(isPaused));
            int stepped=0;
            foreach(var prop in WorldProps.All(world))
            {
                if(prop.owner!=localOwner || !prop.movable || !prop.hasPhysicalBody || prop.held ||
                    !LevelActivity.HasConnectedPlayer(world,prop.level) || isPaused(prop.level)) continue;
                var room=roomForLevel(prop.level);
                PropMotion.Step(prop,room,dt,null,WorldProps.All(world),room.dynamicSurfaces?.Count>0?passengers?.ForProp(prop):null);
                stepped++;
                publish?.Invoke(prop);
            }
            return stepped;
        }
    }
}
