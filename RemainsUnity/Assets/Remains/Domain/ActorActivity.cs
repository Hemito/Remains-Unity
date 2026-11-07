using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // The animation/pose writers share one active-zone decision. Refresh once per simulation frame;
    // queries inspect connected players only, not the entire actor list for each actor.
    public sealed class ActorActivity
    {
        private readonly ConnectedPlayers connected=new();
        private readonly HashSet<string> pendingPoses=new(StringComparer.Ordinal);
        private string worldId;
        private string visibleLevel;
        private Rect visibleArea;
        public void Refresh(WorldState world,IReadOnlyList<ActorState> players=null)
        {
            if(worldId!=world.worldId) {pendingPoses.Clear();worldId=world.worldId;}
            connected.RefreshActors(players??world.actors);
            visibleLevel=null;
        }
        public ConnectedPlayers Connected=>connected;
        public IReadOnlyCollection<string> PendingPoses=>pendingPoses;
        public void ForgetPose(string id)=>pendingPoses.Remove(id);
        // Visual coverage may exceed the AI room on an unusually wide viewport. It affects animation/pose
        // writers only; NpcController continues to use NpcActivity's gameplay rectangle.
        public void SetVisibleArea(string level,Rect area) {visibleLevel=level;visibleArea=area;}
        public bool InVisibleArea(ActorState actor)=>visibleLevel==null || actor.level==visibleLevel && visibleArea.Contains(actor.Position);
        public bool Active(ActorState actor)=>ActorPresence.IsPresent(actor) && connected.OnLevel(actor.level) &&
            (actor.player || ActorSessionRules.IsPassive(actor) || ActorHoldRules.Active(actor) || NpcActivity.Awake(connected.Players,actor) ||
                actor.level==visibleLevel && visibleArea.Contains(actor.Position));
        // Remember activity until the next transport cadence, even when the actor/player crosses the room boundary
        // in this frame. The final real pose is sent once; its sleeping successors do not advance transport clocks.
        public void ObserveSimulation(ActorState actor)=>pendingPoses.Add(actor.id);
        public bool TakePosePublication(ActorState actor,out bool final,bool paused=false)
        {
            if(paused) {final=false;return false;}
            bool pending=pendingPoses.Remove(actor.id);
            bool active=Active(actor);final=pending && !active;
            return pending || active;
        }
    }
}
