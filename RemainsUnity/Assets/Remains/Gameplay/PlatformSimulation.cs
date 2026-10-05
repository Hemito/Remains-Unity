using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;

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
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly Dictionary<string,MovingPlatformMotion> motions=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Level> levels=new(StringComparer.Ordinal);
        private string worldId;
        private long sequence;
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
            if(ReferenceEquals(world,boundWorld) && boundGeometryVersion==geometry.TopologyVersion) return;
            boundWorld=world;
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
                    var state=states[i];retained.Add(state.id);
                    if(!motions.TryGetValue(state.definition,out var motion))
                        motions.Add(state.definition,motion=new MovingPlatformMotion(loadDefinition(state.definition)));
                    if(!entries.TryGetValue(state.id,out var entry))
                        entries.Add(state.id,entry=new Entry {motion=motion});
                    bool newSegment=state.segment!=null && state.segment.sequence!=entry.state?.segment?.sequence;
                    bool changedRoute=entry.state!=null && (state.definition!=entry.state.definition || state.definitionVersion!=entry.state.definitionVersion ||
                        state.Start!=entry.state.Start || state.End!=entry.state.End);
                    entry.needsRebase|=newSegment || changedRoute;
                    if(topologyChanged || !hadFrame) entry.initialized=false;
                    entry.motion=motion;
                    var previousSampleState=entry.samplingState??entry.state;
                    bool finishLocally=previousSampleState?.segment!=null && state.segment==null &&
                        state.endpoint==previousSampleState.segment.toEndpoint && !changedRoute;
                    entry.samplingState=finishLocally?previousSampleState:state;
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
                    bool carryInterval=continuous && !entry.needsRebase;
                    level.room.dynamicSurfaces.SetPose(entry.state.id,carryInterval?entry.sample.SupportBounds:next.SupportBounds,next.SupportBounds,
                        continuous?previous:now,now,frameSequence,carryInterval);
                    entry.needsRebase=false;
                    entry.sample=next;entry.elapsed=now;entry.initialized=true;
                    if(next.Completed && entry.state.segment==null) entry.samplingState=entry.state;
                }
            }
        }
        public static void RestoreSupport(WorldState world,RoomLayout room,string level,double elapsed)
        {
            var states=world.movingPlatforms.Where(p=>p.level==level && !p.removed).ToArray();
            if(states.Length==0) return;
            var frame=room.dynamicSurfaces??=new DynamicSurfaceFrame();
            frame.ReconcileIds(states.Select(state=>CreateSpec(state,MechanismCatalogAsset.Load().Platform(state.definition).CreateDefinition())).ToArray());
            for(int i=0;i<states.Length;i++)
            {
                var motion=new MovingPlatformMotion(MechanismCatalogAsset.Load().Platform(states[i].definition).CreateDefinition());
                var sample=motion.Evaluate(states[i],elapsed);
                frame.SetPose(i,sample.SupportBounds,sample.SupportBounds,elapsed,elapsed,1,false);
            }
        }
        private static double SavedTime(WorldState world,string level)
            =>world.levelTimes.Find(clock=>clock.level==level)?.elapsedSeconds??throw new InvalidOperationException("Нет сохранённых часов платформы: "+level);
        private static DynamicSurfaceSpec CreateSpec(MovingPlatformState state,MovingPlatformDefinition type)
        {
            return new DynamicSurfaceSpec(state.id,type.oneWay,type.shotsPassThrough,type.surfaceTexture,type.movementSound);
        }
        public void Dispose() {if(session!=null) session.Changed-=Reconcile;}
    }
}
