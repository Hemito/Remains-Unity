using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Shared selection/admission/velocity rules for the controller and static editor navigation.
    // Pose-dependent descending release remains in MovementController/LadderPoseContact.
    public static class LadderMotion
    {
        public static bool InReach(RoomLadder ladder,float x,float y)=>Mathf.Abs(x-ladder.x-ladder.movementOffset)<.65f &&
            y>=ladder.bottom-.1f && y<ladder.top+.2f;

        public static RoomLadder Select(RoomLadder[] ladders,float x,float y,bool climbing,RoomLadder held)
        {
            if(climbing && held!=null && System.Array.IndexOf(ladders,held)>=0 && InReach(held,x,y)) return held;
            RoomLadder selected=null;float bestHeight=float.PositiveInfinity,bestAxis=float.PositiveInfinity;
            foreach(var ladder in ladders)
            {
                if(!InReach(ladder,x,y)) continue;
                float height=Mathf.Abs(y-Mathf.Clamp(y,ladder.bottom,ladder.top));
                float axis=Mathf.Abs(x-ladder.x-ladder.movementOffset);
                if(height>bestHeight || height==bestHeight && axis>bestAxis) continue;
                if(height==bestHeight && axis==bestAxis && selected!=null &&
                    (ladder.bottom>selected.bottom || ladder.bottom==selected.bottom && ladder.x>=selected.x)) continue;
                selected=ladder;bestHeight=height;bestAxis=axis;
            }
            return selected;
        }

        public static bool Attach(RoomLadder selected,bool wasClimbing,bool detach,bool stepOff,bool downEntry,float inputY,float y)
            =>selected!=null && !detach && !stepOff && (wasClimbing || inputY>.1f && y<selected.top-.001f || downEntry);

        public static Vector2 Velocity(RoomLadder selected,float x,float inputY,float dt,float walkSpeed,float climbSpeed,float downMultiplier)
            =>new(dt>0?Mathf.Clamp((selected.x+selected.movementOffset-x)/dt,-walkSpeed,walkSpeed):0,
                inputY*climbSpeed*(inputY<0?downMultiplier:1));
    }
}
