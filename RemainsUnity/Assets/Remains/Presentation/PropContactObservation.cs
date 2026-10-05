using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // Cosmetic contact history reads live physical support; it never moves an object.
    public sealed class PropContactObservation
    {
        private readonly MotionContacts contacts=new();
        private readonly MovementHull hull=new();
        public MotionContact Sample(PropState prop,RoomLayout room,IEnumerable<PropState> neighbours,bool paused,float teleportDistance)
        {
            if(!prop.hasPhysicalBody) return MotionContact.None;
            hull.width=prop.width;hull.height=prop.height;
            var feet=prop.Position-Vector2.up*prop.anchorHeight;
            bool supported=ActorTerrainMotion.Supported(room,hull,feet) || PropSupport.Supported(prop,feet,neighbours);
            return contacts.Sample(feet,prop.level,prop.generation,supported,false,paused || prop.held,
                float.MaxValue,float.MaxValue,teleportDistance);
        }
    }
}
