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
        private bool reusableSupport;
        private Vector2 supportedFeet;
        private RoomLayout supportedRoom;
        private string supportedLevel;
        private int supportedGeneration;
        private float supportedWidth,supportedHeight;
        public int SupportQueries {get;private set;}
        public void InvalidateSupport()=>reusableSupport=false;
        public MotionContact Sample(PropState prop,RoomLayout room,IEnumerable<PropState> neighbours,bool paused,float teleportDistance)
        {
            if(!prop.hasPhysicalBody) {InvalidateSupport();return MotionContact.None;}
            hull.width=prop.width;hull.height=prop.height;
            var feet=prop.Position-Vector2.up*prop.anchorHeight;
            bool suppressed=paused || prop.held;
            bool supported=!suppressed && reusableSupport && feet==supportedFeet && room==supportedRoom &&
                prop.level==supportedLevel && prop.generation==supportedGeneration && prop.width==supportedWidth && prop.height==supportedHeight;
            if(!suppressed && !supported)
            {
                SupportQueries++;
                supported=ActorTerrainMotion.Supported(room,hull,feet) || PropSupport.Supported(prop,feet,neighbours);
            }
            reusableSupport=supported;
            supportedFeet=feet;supportedRoom=room;supportedLevel=prop.level;supportedGeneration=prop.generation;
            supportedWidth=prop.width;supportedHeight=prop.height;
            return contacts.Sample(feet,prop.level,prop.generation,supported,false,paused || prop.held,
                float.MaxValue,float.MaxValue,teleportDistance);
        }
    }
}
