using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Gameplay
{
    // Explicit owner-side cache; snapshots can replace state objects without replaying carry.
    public sealed class PassengerMotionRegistry
    {
        private readonly Dictionary<(bool prop,string id),PassengerMotionState> states=new();
        private readonly List<(bool prop,string id)> removed=new();
        public PassengerMotionState ForActor(ActorState actor)=>Get(false,actor.id,actor.owner,actor.generation,actor.level);
        public PassengerMotionState ForProp(PropState prop)=>Get(true,prop.id,prop.owner,prop.generation,prop.level);
        private PassengerMotionState Get(bool prop,string id,string owner,int generation,string level)
        {
            var key=(prop,id);
            if(!states.TryGetValue(key,out var state) || state.Owner!=owner || state.Generation!=generation || state.Level!=level)
                states[key]=state=new PassengerMotionState(id,owner,generation,level);
            return state;
        }
        public void Reconcile(WorldState world,string localOwner)
        {
            removed.Clear();
            foreach(var pair in states)
            {
                string owner=world==null?null:pair.Key.prop?WorldProps.Find(world,pair.Key.id)?.owner:world.Find(pair.Key.id)?.owner;
                if(string.IsNullOrEmpty(localOwner) || owner!=localOwner) removed.Add(pair.Key);
            }
            foreach(var key in removed) states.Remove(key);
        }
    }
}
