using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

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
            context.Frame++;
            int stepped=0,sleeping=0;
            foreach(var prop in context.Props)
            {
                if(prop.owner!=localOwner || !prop.movable || !prop.hasPhysicalBody || !context.Running(prop.level)) continue;
                // A body carried by telekinesis may leave bodies resting on it without support (no geometry needed).
                if(prop.held) {context.WakeAbove(prop,prop.y);continue;}
                var room=context.Room(prop.level,roomForLevel);
                var passenger=room.dynamicSurfaces?.Count>0?passengers?.ForProp(prop):null;
                if(context.Sleeping(prop,room)) {sleeping++;publish?.Invoke(prop);continue;}   // pose heartbeat continues
                float previousX=prop.x,previousY=prop.y;
                PropMotion.Step(prop,room,dt,null,context.Support,passenger,context.Hull);
                context.Support.Moved(prop,previousX);
                context.Settle(prop,room,previousX,previousY,passenger);
                stepped++;
                publish?.Invoke(prop);
            }
            context.LastStepped=stepped;context.LastSleeping=sleeping;
            return stepped;
        }
    }

    // Frame scratch data and body sleep. References stay live.
    // Sleep: a body still for SleepAfterSteps steps (no motion, no velocity, not carried by a platform) stops stepping
    // until something can move it: its own velocity or position changed, the level geometry changed (doors), a body
    // it may rest on moved or is held. Each sleeper still takes a normal step every VerifyEvery frames (staggered),
    // so a support removed by any other path is noticed within that period.
    public sealed class FreePropMotionContext
    {
        public const int SleepAfterSteps=10,VerifyEvery=30;
        private const float Still=.0005f;
        private sealed class Rest {public float x,y;public int still;public bool asleep;public RoomSurface[] surfaces;public int phase;}
        // By body object (weak): a received snapshot reuses unchanged body objects, so their sleep survives it.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PropState,Rest> rest=new();
        private int phases;
        internal long Frame;
        public int LastStepped {get;internal set;}
        public int LastSleeping {get;internal set;}

        internal bool Sleeping(PropState prop,RoomLayout room)
        {
            if(!rest.TryGetValue(prop,out var state) || !state.asleep) return false;
            bool disturbed=prop.vx!=0 || prop.vy!=0 || prop.x!=state.x || prop.y!=state.y || !ReferenceEquals(state.surfaces,room.platforms);
            if(disturbed || (Frame+state.phase)%VerifyEvery==0)
            {
                state.asleep=false;state.still=disturbed?0:SleepAfterSteps-1;
                return false;
            }
            return true;
        }

        internal void Settle(PropState prop,RoomLayout room,float previousX,float previousY,PassengerMotionState passenger)
        {
            if(!rest.TryGetValue(prop,out var state)) rest.Add(prop,state=new Rest {phase=phases++%VerifyEvery});
            bool moved=Mathf.Abs(prop.x-previousX)>Still || Mathf.Abs(prop.y-previousY)>Still;
            if(moved) {WakeAbove(prop,Mathf.Min(previousY,prop.y));state.still=0;return;}
            if(Mathf.Abs(prop.vx)>Still || prop.vy!=0 || passenger?.SupportId!=null) {state.still=0;return;}
            if(++state.still<SleepAfterSteps) return;
            prop.vx=0;   // settle exactly: the decayed residual would otherwise count as motion forever
            state.x=prop.x;state.y=prop.y;state.surfaces=room.platforms;state.asleep=true;
        }

        // Wakes sleepers that may rest on this body: overlapping it horizontally, with feet at or above its lowest top.
        internal void WakeAbove(PropState mover,float lowestY)
        {
            float top=lowestY-mover.anchorHeight+mover.height-.05f;
            foreach(var other in Support.Near(mover,mover.x))
            {
                if(ReferenceEquals(other,mover) || !rest.TryGetValue(other,out var state) || !state.asleep) continue;
                if(Mathf.Abs(other.x-mover.x)>=(other.width+mover.width)*.5f) continue;
                if(other.y-other.anchorHeight<top) continue;
                state.asleep=false;state.still=0;
            }
        }

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
