using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomEditorLadderChecks
    {
        public static void Run()
        {
            var low=new RoomLadder{x=2,bottom=0,top=3,movementOffset=.1f};
            var high=new RoomLadder{x=2,bottom=3,top=6,movementOffset=.1f};
            var room=new[]{high,low};
            Require(ReferenceEquals(LadderMotion.Select(room,2.1f,3,true,low),low),"Existing grip at junction");
            Require(ReferenceEquals(LadderMotion.Select(room,2.1f,3,false,null),low),"Deterministic junction tie");
            Require(LadderMotion.Select(room,8,1,false,null)==null,"Out of reach");
            Require(!LadderMotion.Attach(low,false,false,false,false,1,3),"Attach upwards at top");
            Require(!LadderMotion.Attach(low,true,true,false,false,1,1),"Jump detach");
            Require(!LadderMotion.Attach(low,true,false,true,false,0,1),"Supported side exit");
            Require(LadderMotion.Attach(low,false,false,false,false,1,1),"Ordinary upward admission");
            var settings=MovementSettings.Load();
            foreach(int rate in new[]{30,60,144})
            {
                float dt=1f/rate;
                var velocity=LadderMotion.Velocity(low,1,1,dt,settings.walkSpeed,settings.climbSpeed,settings.climbDownMultiplier);
                Require(velocity.x==settings.walkSpeed && velocity.y==settings.climbSpeed,"Upward velocity and bounded alignment");
                velocity=LadderMotion.Velocity(low,2.1f,-1,dt,settings.walkSpeed,settings.climbSpeed,settings.climbDownMultiplier);
                Require(Mathf.Abs(velocity.x)<.001f && Mathf.Abs(velocity.y+settings.climbSpeed*settings.climbDownMultiplier)<.0001f,"Descending velocity");
            }
            Debug.Log("Room editor shared ladder rules: PASS — native Unity selector, admission, held junction, side exit and 30/60/144 Hz velocities.");
        }
        private static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
    }
}
