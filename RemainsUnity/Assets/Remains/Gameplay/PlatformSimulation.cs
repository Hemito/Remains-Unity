using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Serialization;

namespace Remains.Gameplay
{
    // Reconcile cached topology/types on explicit world changes. Step publishes one
    // read-only geometry interval for all local entity owners and presentation.
    public sealed class PlatformSimulation : IDisposable
    {
        private sealed class Entry
        {
            public MovingPlatformState state;
            public MovingPlatformState samplingState;
            public MovingPlatformMotion motion;
            public MovingPlatformSample sample;
            public double elapsed;
            public bool initialized;
            public bool needsRebase;
            public bool confirmedDeparture;
        }
        private sealed class Level
        {public RoomLayout room;public Entry[] entries;}
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private readonly Func<WorldState> currentWorld;
        private readonly Func<string,bool> hasClock;
        private readonly Func<string,double> readClock;
        private readonly Func<string,MovingPlatformDefinition> loadDefinition;
        private WorldState boundWorld;
        private long boundGeometryVersion=-1;
        private long boundWorldRevision=-1;
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly Dictionary<(string Id,int Version),MovingPlatformMotion> motions=new();
        private readonly Dictionary<string,Level> levels=new(StringComparer.Ordinal);
        private string worldId;
        private long sequence;
        public event Action<RoomLayout> TopologyChanged;
        public PlatformSimulation(CoopSession session,SimulationGeometry geometry)
            :this(geometry,()=>session.World,session.LevelTime.HasLocal,session.LevelTime.ReadLocal,
                id=>MechanismCatalogAsset.Load().Platform(id).CreateDefinition())
        {this.session=session;session.Changed+=Reconcile;}
        public PlatformSimulation(SimulationGeometry geometry,Func<WorldState> world,Func<string,bool> hasClock,
            Func<string,double> readClock,Func<string,MovingPlatformDefinition> loadDefinition)
        {
            this.geometry=geometry??throw new ArgumentNullException(nameof(geometry));currentWorld=world??throw new ArgumentNullException(nameof(world));
            this.hasClock=hasClock??throw new ArgumentNullException(nameof(hasClock));this.readClock=readClock??throw new ArgumentNullException(nameof(readClock));
            this.loadDefinition=loadDefinition??throw new ArgumentNullException(nameof(loadDefinition));Reconcile();
        }
        public bool TrySample(string id,out MovingPlatformSample sample)
        {if(entries.TryGetValue(id,out var entry)) {sample=entry.sample;return true;}sample=default;return false;}
        public void Reconcile()
        {
            var world=currentWorld();
            // PublishActor also raises Changed within the current physics interval.
            // An actor-only event cannot rebuild or overwrite the shared surface frame.
            if(ReferenceEquals(world,boundWorld) && boundWorldRevision==world?.revision &&
                worldId==world?.worldId && boundGeometryVersion==geometry.TopologyVersion) return;
            boundWorld=world;
            boundWorldRevision=world?.revision??-1;
            boundGeometryVersion=geometry.TopologyVersion;
            if(worldId!=world?.worldId) {entries.Clear();levels.Clear();worldId=world?.worldId;}
            if(world==null) return;
            var retained=new HashSet<string>(StringComparer.Ordinal);
            long initialSequence=++sequence;
            var loaded=new HashSet<string>(geometry.LoadedLevels,StringComparer.Ordinal);
            foreach(string removed in levels.Keys.Where(level=>!loaded.Contains(level)).ToArray()) levels.Remove(removed);
            foreach(string level in loaded)
            {
                var room=geometry.Get(level);
                var states=world.movingPlatforms.Where(p=>p.level==level && !p.removed).ToArray();
                var frame=room.dynamicSurfaces??=new DynamicSurfaceFrame();
                bool hadFrame=frame.Count>0 && frame.IsPublished;
                double baseline=hadFrame?frame.ElapsedSeconds:hasClock(level)?readClock(level):SavedTime(world,level);
                bool topologyChanged=frame.ReconcileIds(states.Select(p=>CreateSpec(p,loadDefinition(p.definition))).ToArray());
                var selected=new Entry[states.Length];
                for(int i=0;i<states.Length;i++)
                {
                    // Event-time platform snapshots protect route and retired segments from
                    // in-place canonical mutations. Actor-only Changed returns before copying.
                    var state=StateJson.Copy(states[i]);retained.Add(state.id);
                    var motionKey=(state.definition,state.definitionVersion);
                    if(!motions.TryGetValue(motionKey,out var motion))
                        motions.Add(motionKey,motion=new MovingPlatformMotion(loadDefinition(state.definition)));
                    if(!entries.TryGetValue(state.id,out var entry))
                        entries.Add(state.id,entry=new Entry {motion=motion});
                    bool changedRoute=entry.state!=null && (state.definition!=entry.state.definition || state.definitionVersion!=entry.state.definitionVersion ||
                        state.Start!=entry.state.Start || state.End!=entry.state.End);
                    entry.needsRebase|=changedRoute;
                    if(topologyChanged || !hadFrame) entry.initialized=false;
                    entry.motion=motion;
                    var previousSampleState=entry.samplingState??entry.state;
                    // Delayed confirmation may move an already published stationary
                    // endpoint. Retain that observed support as the start of one
                    // correction, rather than abandoning its existing passengers.
                    // A route/type change, new binding or moving predecessor has
                    // no such endpoint witness and still starts from a baseline.
                    bool finishLocally=previousSampleState?.segment!=null && state.segment==null &&
                        state.endpoint==previousSampleState.segment.toEndpoint && !changedRoute;
                    entry.samplingState=finishLocally?previousSampleState:state;
                    entry.confirmedDeparture=entry.initialized && !topologyChanged && hadFrame && !changedRoute &&
                        (entry.confirmedDeparture && SameSegment(previousSampleState?.segment,entry.samplingState.segment) ||
                         previousSampleState!=null && previousSampleState.segment==null && state.segment!=null &&
                         state.segment.fromEndpoint==previousSampleState.endpoint &&
                         motion.Evaluate(state,state.segment.startElapsedSeconds).SupportBounds.Equals(entry.sample.SupportBounds));
                    entry.state=state;selected[i]=entry;
                    if(!entry.initialized)
                    {
                        entry.elapsed=baseline;
                        entry.sample=motion.Evaluate(entry.samplingState,entry.elapsed);entry.initialized=true;
                    }
                    if(topologyChanged || !hadFrame)
                    {
                        entry.elapsed=baseline;
                        frame.SetPose(i,entry.sample.SupportBounds,entry.sample.SupportBounds,baseline,baseline,initialSequence,false);
                    }
                }
                levels[level]=new Level {room=room,entries=selected};
                if(topologyChanged) TopologyChanged?.Invoke(room);
            }
            foreach(string id in entries.Keys.Where(id=>!retained.Contains(id)).ToArray()) entries.Remove(id);
        }
        public void Step(double delta)
        {
            if(currentWorld()==null) return;
            long frameSequence=++sequence;
            foreach(var pair in levels)
            {
                var level=pair.Value;
                if(level.entries.Length==0) continue;
                if(!hasClock(pair.Key)) continue; // Initial confirmed epoch is still in flight; saved support stays frozen.
                double now=readClock(pair.Key);
                double previous=level.entries[0].elapsed;
                bool continuous=LevelTimePlayback.IsContinuous(previous,now,delta);
                foreach(var entry in level.entries)
                {
                    var next=entry.motion.Evaluate(entry.samplingState,now);
                    bool deferredDeparture=entry.confirmedDeparture && continuous && now==entry.elapsed &&
                        !next.SupportBounds.Equals(entry.sample.SupportBounds);
                    if(deferredDeparture) next=entry.sample; // Frozen time cannot consume a correction.
                    // A newly confirmed segment can begin inside this unconsumed interval.
                    // Carry an ordinary interval, or one confirmed departure from
                    // the same previously observed endpoint. PassengerMotion sweeps
                    // this whole displacement and consumes it once per owner.
                    bool carryInterval=continuous && !entry.needsRebase &&
                        (entry.confirmedDeparture || entry.motion.Evaluate(entry.samplingState,entry.elapsed).SupportBounds.Equals(entry.sample.SupportBounds));
                    level.room.dynamicSurfaces.SetPose(entry.state.id,carryInterval?entry.sample.SupportBounds:next.SupportBounds,next.SupportBounds,
                        continuous?previous:now,now,frameSequence,carryInterval);
                    entry.needsRebase=false;
                    entry.confirmedDeparture=deferredDeparture;
                    entry.sample=next;entry.elapsed=now;entry.initialized=true;
                    if(next.Completed && entry.state.segment==null) entry.samplingState=entry.state;
                }
            }
        }
        public static void RestoreSupport(WorldState world,RoomLayout room,string level,double elapsed)
        {
            var states=world.movingPlatforms.Where(p=>p.level==level && !p.removed).ToArray();
            // Restoring a detached snapshot starts a new transient support frame.
            // A repeated preparation keeps RoomLayout/light identity, but cannot
            // republish sequence 1 into a frame from the previous snapshot.
            var frame=new DynamicSurfaceFrame();
            frame.ReconcileIds(states.Select(state=>CreateSpec(state,MechanismCatalogAsset.Load().Platform(state.definition).CreateDefinition())).ToArray());
            for(int i=0;i<states.Length;i++)
            {
                var motion=new MovingPlatformMotion(MechanismCatalogAsset.Load().Platform(states[i].definition).CreateDefinition());
                var sample=motion.Evaluate(states[i],elapsed);
                frame.SetPose(i,sample.SupportBounds,sample.SupportBounds,elapsed,elapsed,1,false);
            }
            frame.RequirePublished();room.dynamicSurfaces=frame;
        }
        private static double SavedTime(WorldState world,string level)
            =>world.levelTimes.Find(clock=>clock.level==level)?.elapsedSeconds??throw new InvalidOperationException("Нет сохранённых часов платформы: "+level);
        private static bool SameSegment(MovingPlatformSegment a,MovingPlatformSegment b)
            =>a!=null && b!=null && a.sequence==b.sequence && a.fromEndpoint==b.fromEndpoint && a.toEndpoint==b.toEndpoint &&
                a.startElapsedSeconds==b.startElapsedSeconds && a.durationSeconds==b.durationSeconds;
        private static DynamicSurfaceSpec CreateSpec(MovingPlatformState state,MovingPlatformDefinition type)
        {
            return new DynamicSurfaceSpec(state.id,type.oneWay,type.shotsPassThrough,type.surfaceTexture,type.movementSound);
        }
        public void Dispose() {if(session!=null) session.Changed-=Reconcile;}
    }
}
