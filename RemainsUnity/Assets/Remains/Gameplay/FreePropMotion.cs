using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Gameplay
{
    // Free physical objects follow their owner and active level, not player input.
    public static class FreePropMotion
    {
        public static int Step(WorldState world,string localOwner,float dt,Func<string,RoomLayout> roomForLevel,
            Func<string,bool> isPaused,Action<PropState> publish=null,PassengerMotionRegistry passengers=null)
            =>Step(world,localOwner,dt,roomForLevel,isPaused,publish,passengers,new FreePropMotionContext());
        public static int Step(WorldState world,string localOwner,float dt,Func<string,RoomLayout> roomForLevel,
            Func<string,bool> isPaused,Action<PropState> publish,PassengerMotionRegistry passengers,FreePropMotionContext context)
        {
            if(world==null || dt<=0) return 0;
            if(string.IsNullOrEmpty(localOwner)) throw new ArgumentException("Не задан владелец симуляции свободных объектов.",nameof(localOwner));
            if(roomForLevel==null) throw new ArgumentNullException(nameof(roomForLevel));
            if(isPaused==null) throw new ArgumentNullException(nameof(isPaused));
            if(context==null) throw new ArgumentNullException(nameof(context));
            context.Prepare(world,isPaused);
            int stepped=0;
            foreach(var prop in context.Props)
            {
                if(prop.owner!=localOwner || !prop.movable || !prop.hasPhysicalBody || prop.held ||
                    !context.Running(prop.level)) continue;
                var room=context.Room(prop.level,roomForLevel);
                float previousX=prop.x;
                PropMotion.Step(prop,room,dt,null,context.Support,room.dynamicSurfaces?.Count>0?passengers?.ForProp(prop):null,context.Hull);
                context.Support.Moved(prop,previousX);
                stepped++;
                publish?.Invoke(prop);
            }
            return stepped;
        }
    }

    // Lightweight frame scratch data. References stay live; physics still steps every eligible body.
    public sealed class FreePropMotionContext
    {
        private readonly Dictionary<string,bool> levels=new(StringComparer.Ordinal);
        private readonly Dictionary<string,RoomLayout> rooms=new(StringComparer.Ordinal);
        internal readonly MovementHull Hull=new();
        internal readonly PropSupportIndex Support=new();
        internal WorldPropView Props {get;private set;}
        internal void Prepare(WorldState world,Func<string,bool> isPaused)
        {
            if(Props?.World!=world) Props=new WorldPropView(world);
            levels.Clear();rooms.Clear();
            foreach(var actor in world.actors)
                if(actor.player && actor.connected && !levels.ContainsKey(actor.level)) levels.Add(actor.level,!isPaused(actor.level));
            // Only running levels' bodies: neighbours are searched on the stepped body's own level.
            Support.Prepare(Props,Running);
        }
        internal bool Running(string level)=>levels.TryGetValue(level,out bool running) && running;
        internal RoomLayout Room(string level,Func<string,RoomLayout> read)
        {
            if(!rooms.TryGetValue(level,out var room)) rooms.Add(level,room=read(level));
            return room;
        }
    }
}
