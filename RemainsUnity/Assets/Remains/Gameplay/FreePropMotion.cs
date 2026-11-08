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
            Func<string,bool> isPaused,Action<PropState> publish,PassengerMotionRegistry passengers,FreePropMotionContext context,
            Action<PropState,Vector2> moved,Action<PropState> settled=null,IReadOnlyList<ActorState> connectedPlayers=null,
            Action<PropState> observe=null)
        {
            if(world==null || dt<=0) return 0;
            if(string.IsNullOrEmpty(localOwner)) throw new ArgumentException("Не задан владелец симуляции свободных объектов.",nameof(localOwner));
            if(roomForLevel==null) throw new ArgumentNullException(nameof(roomForLevel));
            if(isPaused==null) throw new ArgumentNullException(nameof(isPaused));
            if(context==null) throw new ArgumentNullException(nameof(context));
            context.Frame++;
            using(Remains.Diagnostics.FrameHitchLog.Section("Тела: подготовка опор")) context.Prepare(world,localOwner,isPaused,connectedPlayers);
            int stepped=0,sleeping=context.CountUnscheduledSleepers();
            var bodies=context.Scheduled;
            for(int i=0;i<bodies.Count;i++)
            {
                var prop=bodies[i];
                if(prop.owner!=localOwner || !prop.movable || !prop.hasPhysicalBody || !context.Running(prop.level)) continue;
                // A body carried by telekinesis may leave bodies resting on it without support (no geometry needed).
                if(prop.held) {context.WakeAbove(prop,prop.y);continue;}
                // Before any step: a body first seen here gets its received pose as the publication baseline.
                observe?.Invoke(prop);
                var room=context.Room(prop.level,roomForLevel);
                if(context.Sleeping(prop,room,dt)) {sleeping++;publish?.Invoke(prop);continue;}
                var passenger=room.dynamicSurfaces?.Count>0?passengers?.ForProp(prop):null;
                float previousX=prop.x,previousY=prop.y;
                PropMotion.Step(prop,room,dt,null,context.Support,passenger,context.Hull);
                if(PropImpactRules.IsBox(prop) && PropImpactRules.Dangerous(new Vector2(prop.vx,prop.vy)))
                    context.ImpactSources.Add((prop,prop.Position,new Vector2(prop.vx,prop.vy)));
                context.Support.Moved(prop,previousX);
                bool fellAsleep=context.Settle(prop,room,previousX,previousY,passenger);
                moved?.Invoke(prop,new Vector2(previousX,previousY));
                stepped++;
                publish?.Invoke(prop);
                if(fellAsleep && publish==null) settled?.Invoke(prop);
            }
            context.LastStepped=stepped;context.LastSleeping=sleeping;
            return stepped;
        }
        public static int Step(WorldState world,string localOwner,float dt,Func<string,RoomLayout> roomForLevel,
            Func<string,bool> isPaused,Action<PropState> publish,PassengerMotionRegistry passengers,FreePropMotionContext context)
            =>Step(world,localOwner,dt,roomForLevel,isPaused,publish,passengers,context,null);
    }

    // Frame scratch data and body sleep. References stay live.
    // Sleep: a body still for SleepAfterSteps steps (no motion, no velocity, not carried by a platform) stops stepping
    // until something can move it: its own velocity or position changed, the level geometry changed (doors), a body
    // it may rest on moved or is held. Each sleeper still checks support every VerifyEvery frames, staggered.
    public sealed class FreePropMotionContext
    {
        public const int SleepAfterSteps=10,VerifyEvery=30;
        private const float Still=.0005f;
        private sealed class Rest
        {
            public float x,y;public int still;public bool asleep,staticSupport;public RoomSurface[] surfaces;public int phase,generation;
        }
        // By body object (weak): a received snapshot reuses unchanged body objects, so their sleep survives it.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PropState,Rest> rest=new();
        private int phases;
        internal long Frame;
        public int LastStepped {get;internal set;}
        public int LastSleeping {get;internal set;}

        internal bool Sleeping(PropState prop,RoomLayout room,float dt)
        {
            if(!rest.TryGetValue(prop,out var state) || !state.asleep) return false;
            bool disturbed=Disturbed(prop,state) || !ReferenceEquals(state.surfaces,room.platforms);
            if(disturbed)
            {
                Wake(prop,state);
                return false;
            }
            if((Frame+state.phase)%VerifyEvery!=0) return true;
            if(trackSupport)
            {
                if(state.staticSupport && !MovingPlatforms(room)) return true;
                Hull.width=prop.width;Hull.height=prop.height;
                var feet=prop.Position-Vector2.up*prop.anchorHeight;
                if(!PassengerMotion.PreviouslySupported(room,Hull,feet,dt) &&
                    (state.staticSupport || ActorTerrainMotion.Supported(room,Hull,feet,bodyTops:false) ||
                        PropSupport.Supported(prop,feet,Support)))
                    return true;
            }
            Wake(prop,state,trackSupport?0:SleepAfterSteps-1);
            return false;
        }

        internal bool Settle(PropState prop,RoomLayout room,float previousX,float previousY,PassengerMotionState passenger)
        {
            if(!rest.TryGetValue(prop,out var state)) rest.Add(prop,state=new Rest {phase=phases++%VerifyEvery});
            bool moved=Mathf.Abs(prop.x-previousX)>Still || Mathf.Abs(prop.y-previousY)>Still;
            if(moved) {WakeAbove(prop,Mathf.Min(previousY,prop.y));state.still=0;return false;}
            if(Mathf.Abs(prop.vx)>Still || prop.vy!=0 || passenger?.SupportId!=null) {state.still=0;return false;}
            if(++state.still<SleepAfterSteps) return false;
            prop.vx=0;   // settle exactly: the decayed residual would otherwise count as motion forever
            state.x=prop.x;state.y=prop.y;state.surfaces=room.platforms;state.asleep=true;state.generation=prop.generation;
            if(trackSupport)
            {
                var feet=prop.Position-Vector2.up*prop.anchorHeight;
                state.staticSupport=ActorTerrainMotion.SupportedByStaticSurface(room,Hull,feet);
                if(schedules.TryGetValue(prop.level,out var schedule) && schedule.Awake.Remove(prop))
                    schedule.Sleeping[DueSlot(state)].Add(prop);
            }
            return true;
        }

        // Wakes sleepers that may rest on this body: overlapping it horizontally, with feet at or above its lowest top.
        internal void WakeAbove(PropState mover,float lowestY)
            =>WakeAbove(mover,lowestY,mover.x);
        private void WakeAbove(PropState mover,float lowestY,float x)
        {
            float top=lowestY-mover.anchorHeight+mover.height-.05f;
            foreach(var other in Support.Near(mover,x))
            {
                if(ReferenceEquals(other,mover) || !rest.TryGetValue(other,out var state) || !state.asleep) continue;
                if(Mathf.Abs(other.x-x)>=(other.width+mover.width)*.5f) continue;
                if(other.y-other.anchorHeight<top) continue;
                Wake(other,state);
            }
        }

        private readonly Dictionary<string,bool> levels=new(StringComparer.Ordinal);
        private readonly Dictionary<string,RoomLayout> rooms=new(StringComparer.Ordinal);
        private readonly Dictionary<RoomLayout,bool> movingPlatforms=new();
        private readonly bool trackSupport;
        private bool supportDirty=true;
        private string trackedOwner;
        private sealed class LevelSchedule
        {
            public readonly HashSet<PropState> Awake=new();
            public readonly HashSet<PropState>[] Sleeping=new HashSet<PropState>[VerifyEvery];
            public LevelSchedule() {for(int i=0;i<Sleeping.Length;i++) Sleeping[i]=new HashSet<PropState>();}
            public void Clear() {Awake.Clear();foreach(var slot in Sleeping) slot.Clear();}
        }
        private readonly Dictionary<string,LevelSchedule> schedules=new(StringComparer.Ordinal);
        private readonly Dictionary<PropState,int> order=new();
        private readonly List<PropState> scheduled=new(),waking=new();
        private readonly Comparison<PropState> compareOrder;
        public FreePropMotionContext(bool trackSupport=false)
        {
            this.trackSupport=trackSupport;
            compareOrder=(left,right)=>order[left].CompareTo(order[right]);
        }
        internal readonly MovementHull Hull=new();
        internal readonly PropSupportIndex Support=new();
        internal readonly List<(PropState prop,Vector2 position,Vector2 velocity)> ImpactSources=new();
        internal WorldPropView Props {get;private set;}
        internal IReadOnlyList<PropState> Scheduled=>trackSupport?scheduled:Props;
        private static bool Disturbed(PropState prop,Rest state)=>prop.held || prop.generation!=state.generation ||
            prop.vx!=0 || prop.vy!=0 || prop.x!=state.x || prop.y!=state.y;
        private static int DueSlot(Rest state)=>(VerifyEvery-state.phase)%VerifyEvery;
        private void Wake(PropState prop,Rest state,int still=0)
        {
            if(trackSupport && schedules.TryGetValue(prop.level,out var schedule) &&
                schedule.Sleeping[DueSlot(state)].Remove(prop)) schedule.Awake.Add(prop);
            state.asleep=false;state.still=still;
        }
        internal void WakeLevel(string level)
        {
            if(!schedules.TryGetValue(level,out var schedule)) return;
            waking.Clear();
            foreach(var slot in schedule.Sleeping) waking.AddRange(slot);
            foreach(var prop in waking) if(rest.TryGetValue(prop,out var state)) Wake(prop,state);
        }
        internal void WakeImpulse(PropState prop)
        {
            if(rest.TryGetValue(prop,out var state) && state.asleep) Wake(prop,state);
        }
        internal void InvalidateSupport()=>supportDirty=true;
        internal void ExternalMotion(PropState prop,Vector2 previous)
        {
            if(!trackSupport || !ReferenceEquals(Support.Find(prop.id),prop)) return;
            Support.Moved(prop,previous.x);
            if(rest.TryGetValue(prop,out var state) && state.asleep && Disturbed(prop,state)) Wake(prop,state);
            WakeAbove(prop,Mathf.Min(previous.y,prop.y),previous.x);
        }
        internal void Prepare(WorldState world,string localOwner,Func<string,bool> isPaused,IReadOnlyList<ActorState> connectedPlayers)
        {
            ImpactSources.Clear();
            if(Props?.World!=world) {Props=new WorldPropView(world);supportDirty=true;}
            if(trackedOwner!=localOwner) {trackedOwner=localOwner;supportDirty=true;}
            levels.Clear();rooms.Clear();movingPlatforms.Clear();
            var players=connectedPlayers??world.actors;
            for(int i=0;i<players.Count;i++)
            {
                var actor=players[i];
                if(actor.player && actor.connected && !levels.ContainsKey(actor.level)) levels.Add(actor.level,!isPaused(actor.level));
            }
            // The live index follows changed bodies. A level is still stepped only
            // while players occupy it, and neighbour queries stay on that level.
            if(!trackSupport || supportDirty)
            {
                Support.Prepare(Props,trackSupport?null:Running);
                if(trackSupport) RebuildSchedule(localOwner);
                supportDirty=false;
            }
            if(!trackSupport) return;
            scheduled.Clear();
            int slot=(int)(Frame%VerifyEvery);
            foreach(var level in levels)
            {
                if(!level.Value || !schedules.TryGetValue(level.Key,out var schedule)) continue;
                scheduled.AddRange(schedule.Awake);scheduled.AddRange(schedule.Sleeping[slot]);
            }
            scheduled.Sort(compareOrder);
        }
        private bool MovingPlatforms(RoomLayout room)
        {
            if(movingPlatforms.TryGetValue(room,out bool moving)) return moving;
            var frame=room.dynamicSurfaces;
            for(int i=0;i<(frame?.Count??0);i++)
            {
                var pose=frame.Pose(i);
                if(pose.Continuous && !pose.Delta.Equals(Vector2.zero)) {moving=true;break;}
            }
            movingPlatforms.Add(room,moving);
            return moving;
        }
        private void RebuildSchedule(string localOwner)
        {
            foreach(var schedule in schedules.Values) schedule.Clear();
            order.Clear();
            for(int i=0;i<Props.Count;i++)
            {
                var prop=Props[i];
                if(prop.owner!=localOwner || !prop.movable || !prop.hasPhysicalBody) continue;
                order.Add(prop,i);
                if(!schedules.TryGetValue(prop.level,out var schedule)) schedules.Add(prop.level,schedule=new LevelSchedule());
                if(rest.TryGetValue(prop,out var state) && state.asleep && !Disturbed(prop,state))
                    schedule.Sleeping[DueSlot(state)].Add(prop);
                else
                {
                    if(state!=null && state.asleep) Wake(prop,state);
                    schedule.Awake.Add(prop);
                }
            }
        }
        internal int CountUnscheduledSleepers()
        {
            if(!trackSupport) return 0;
            int sleeping=0;
            int due=(int)(Frame%VerifyEvery);
            foreach(var level in levels)
            {
                if(!level.Value || !schedules.TryGetValue(level.Key,out var schedule)) continue;
                for(int i=0;i<schedule.Sleeping.Length;i++)
                {
                    if(i!=due) sleeping+=schedule.Sleeping[i].Count;
                }
            }
            return sleeping;
        }
        internal bool Running(string level)=>levels.TryGetValue(level,out bool running) && running;
        internal RoomLayout Room(string level,Func<string,RoomLayout> read)
        {
            if(!rooms.TryGetValue(level,out var room)) rooms.Add(level,room=read(level));
            return room;
        }
    }
}
