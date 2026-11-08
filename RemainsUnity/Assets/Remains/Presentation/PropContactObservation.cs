using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // Cosmetic contact history reads live physical support; it never moves an object.
    public sealed class PropContactObservation
    {
        private MotionContacts contacts=new();
        private string sampledWorld;
        private readonly MovementHull hull=new();
        private bool reusableSupport;
        private Vector2 supportedFeet;
        private RoomLayout supportedRoom;
        private string supportedLevel;
        private int supportedGeneration;
        private float supportedWidth,supportedHeight;
        private bool held;
        private Vector2 heldFeet;
        private string heldLevel,heldOwner;
        private int heldGeneration;
        public int SupportQueries {get;private set;}
        public void InvalidateSupport()=>reusableSupport=false;
        public MotionContact Sample(PropState prop,RoomLayout room,IEnumerable<PropState> neighbours,bool paused,float teleportDistance,string world=null)
        {
            if(!prop.hasPhysicalBody) {held=false;InvalidateSupport();return MotionContact.None;}
            if(world!=null && sampledWorld!=world)
            {contacts=new MotionContacts();sampledWorld=world;held=false;InvalidateSupport();}
            hull.width=prop.width;hull.height=prop.height;
            var feet=prop.Position-Vector2.up*prop.anchorHeight;
            bool suppressed=paused || prop.held;
            // A short fall can finish between observations. The preceding held pose is still a real observed
            // airborne position when this same owner releases it into the next generation. Seed that epoch
            // before sampling its first free pose; ownership changes and teleports keep the ordinary reset.
            if(world!=null && held && !suppressed && prop.level==heldLevel && prop.owner==heldOwner &&
                prop.generation==unchecked(heldGeneration+1) && feet.y<=heldFeet.y &&
                Vector2.Distance(feet,heldFeet)<=teleportDistance)
                contacts.Sample(heldFeet,prop.level,prop.generation,false,false,false,
                    float.MaxValue,float.MaxValue,teleportDistance);
            held=prop.held;
            if(held) {heldFeet=feet;heldLevel=prop.level;heldOwner=prop.owner;heldGeneration=prop.generation;}
            bool supported=!suppressed && reusableSupport && feet==supportedFeet && room==supportedRoom &&
                prop.level==supportedLevel && prop.generation==supportedGeneration && prop.width==supportedWidth && prop.height==supportedHeight;
            if(!suppressed && !supported)
            {
                SupportQueries++;
                supported=ActorTerrainMotion.Supported(room,hull,feet,bodyTops:false) || PropSupport.Supported(prop,feet,neighbours);
            }
            reusableSupport=supported;
            supportedFeet=feet;supportedRoom=room;supportedLevel=prop.level;supportedGeneration=prop.generation;
            supportedWidth=prop.width;supportedHeight=prop.height;
            return contacts.Sample(feet,prop.level,prop.generation,supported,false,paused || prop.held,
                float.MaxValue,float.MaxValue,teleportDistance);
        }
    }
}
