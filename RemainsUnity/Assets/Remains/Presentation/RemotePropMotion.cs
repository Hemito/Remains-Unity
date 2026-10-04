using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // Visual interpolation only. Never writes back to the shared object's physics or inventory.
    public sealed class RemotePropMotion
    {
        public const float CorrectionSeconds=.05f,TeleportDistance=3;
        private bool ready;
        private string id,owner,level;
        private int generation;
        private Vector2 position,received;
        public Vector2 Sample(PropState state,string localOwner,bool paused,float dt)
        {
            var target=state.Center;
            bool reset=!ready || id!=state.id || owner!=state.owner || level!=state.level || generation!=state.generation ||
                Vector2.Distance(received,target)>TeleportDistance;
            if(reset || state.owner==localOwner || string.IsNullOrEmpty(state.owner)) position=target;
            else if(!paused && dt>0) position=Vector2.Lerp(position,target,1-Mathf.Exp(-dt/CorrectionSeconds));
            ready=true;id=state.id;owner=state.owner;level=state.level;generation=state.generation;received=target;
            return position;
        }
    }
}
