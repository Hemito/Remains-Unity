using System;
using UnityEngine;

namespace Remains.Domain
{
    // Owned by one entity simulator; never serialized or attached to the shared surface frame.
    public sealed class PassengerMotionState
    {
        public readonly string EntityId,Owner,Level;
        public readonly int Generation;
        public string SupportId {get;internal set;}
        public Vector2 LastAppliedDelta {get;internal set;}
        public bool Blocked {get;internal set;}
        public long ConsumedSequence {get;internal set;}
        internal DynamicSurfaceFrame Frame;
        internal long TopologyVersion;
        internal double ElapsedSeconds;
        internal string BlockedSupport;
        public PassengerMotionState(string id,string owner,int generation,string level)
        {
            if(string.IsNullOrEmpty(id) || string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(level))
                throw new ArgumentException($"Не задана личность пассажира: id={id}, owner={owner}, level={level}.");
            EntityId=id;Owner=owner;Generation=generation;Level=level;
        }
        internal void Release() {SupportId=null;LastAppliedDelta=Vector2.zero;Blocked=false;}
    }
    public static class PassengerMotion
    {
        public static bool PreviouslySupported(RoomLayout room,MovementHull hull,Vector2 feet,float dt)
        {
            var frame=room.dynamicSurfaces;if(frame==null) return false;
            for(int i=0;i<frame.Count;i++) {var pose=frame.Pose(i);if(pose.Continuous && pose.ElapsedSeconds-pose.PreviousElapsedSeconds<=dt+.001 && Contact(pose.Previous,hull,feet)>0) return true;}
            return false;
        }
        public static Vector2 Carry(RoomLayout room,MovementHull hull,Vector2 start,float dt,PassengerMotionState state,bool enabled=true)
        {
            var frame=room.dynamicSurfaces;
            if(frame==null || frame.Count==0) {state?.Release();return start;}
            frame.RequirePublished();
            if(!enabled && state==null) return start;
            if(state==null) throw new InvalidOperationException($"Нет владельческого состояния пассажира динамической опоры: room={room.id}.");
            if(!float.IsFinite(dt) || dt<0) throw new InvalidOperationException($"Некорректный шаг пассажира: entity={state.EntityId}, dt={dt}.");
            if(state.Frame!=frame || state.TopologyVersion!=frame.TopologyVersion)
            {
                state.Frame=frame;state.TopologyVersion=frame.TopologyVersion;state.ConsumedSequence=frame.Sequence;state.ElapsedSeconds=frame.ElapsedSeconds;
                state.BlockedSupport=null;state.Release();return start; // First observation/rebind never replays old motion.
            }
            if(state.ConsumedSequence==frame.Sequence) return start;
            double previous=state.ElapsedSeconds;state.ElapsedSeconds=frame.ElapsedSeconds;state.ConsumedSequence=frame.Sequence;state.Release();
            if(!enabled || dt<=0) {state.BlockedSupport=null;return start;}
            DynamicSurfacePose chosen=default;float overlap=0;
            for(int i=0;i<frame.Count;i++)
            {
                var pose=frame.Pose(i);float contact=Contact(pose.Previous,hull,start);
                if(!pose.Continuous || Math.Abs(pose.PreviousElapsedSeconds-previous)>1e-6 || pose.ElapsedSeconds-pose.PreviousElapsedSeconds>dt+.001 || contact<=overlap) continue;
                chosen=pose;overlap=contact;
            }
            if(overlap<=0) {state.BlockedSupport=null;return start;}
            if(state.BlockedSupport==chosen.Id) return start;
            state.SupportId=chosen.Id;
            var moved=ActorTerrainMotion.Move(room,hull,start,chosen.Delta,false,excludedSurface:chosen.Current);
            state.LastAppliedDelta=moved-start;
            if((state.LastAppliedDelta-chosen.Delta).sqrMagnitude>.00000001f || !ActorTerrainMotion.IsClear(room,hull,moved))
            {state.Blocked=true;state.BlockedSupport=chosen.Id;state.SupportId=null;}
            return moved;
        }
        private static float Contact(RoomSurface surface,MovementHull hull,Vector2 feet)
        {
            if(Mathf.Abs(feet.y-RoomSupport.Top(surface,feet.x,hull.width))>RoomSupport.ContactTolerance) return 0;
            return Mathf.Max(0,Mathf.Min(feet.x+hull.width*.5f,surface.x+surface.width)-Mathf.Max(feet.x-hull.width*.5f,surface.x)-.0001f);
        }
    }
}
