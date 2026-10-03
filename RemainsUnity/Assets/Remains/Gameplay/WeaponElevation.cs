using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Input gesture state is local; only the resulting weapon pose is replicated.
    public sealed class WeaponElevation
    {
        private string actorId,level;
        private float heldSeconds;
        public void Step(ActorState actor,RoomLayout room,bool upHeld,float dt)
        {
            if(actorId!=actor.id || level!=actor.level) heldSeconds=0;
            actorId=actor.id;level=actor.level;
            bool eligible=upHeld && !actor.down && !actor.climbing && !actor.crouched &&
                !string.IsNullOrEmpty(actor.weapon) && WeaponRigs.Get(actor.weapon).HasMuzzle &&
                ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
            heldSeconds=eligible?Mathf.Min(heldSeconds+Mathf.Max(0,dt),WeaponRigs.RaiseDelay):0;
            actor.weaponLift=eligible && heldSeconds>=WeaponRigs.RaiseDelay?WeaponRigs.RaiseHeight:0;
        }
    }
}
