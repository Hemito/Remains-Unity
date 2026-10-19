using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // UnitTurret.control after the original, times in seconds (frames / 30), angles in degrees with y up:
    //   watch  (aiState 1, lamp 2): the gun sweeps between the watch angles (aRot), one every period (120 frames), turning
    //          at the watch rate; the turret sees only the side its gun points to (and close behind it, detecting);
    //   alert  (aiState 2, lamp 3): it noticed a target and turns to it for 30–40 frames before attacking;
    //   attack (aiState 3, lamp 4): it turns to the target at the attack rate (actDrot) and fires;
    //   search (aiState 4, lamp 5): it lost the target and turns to where it was seen, for maxSpok × 10 frames, then
    //          watches again; seeing the target again resumes the attack.
    // The gun turns at a limited rate, so it fires where it points: a fast target outruns it.
    public static class NpcTurret
    {
        public const string Watch="watch",Alert="alert",Attack="attack",Search="search";

        // Returns whether the turret may fire this step (attack state and the target in sight).
        public static bool Step(ActorState actor,ActorTypeDefinition type,ActorState target,bool visible,Vector2? known,RoomLayout room,float dt,System.Random random)
        {
            var settings=type.turret;
            var state=actor.creature??=Initial(actor,settings);
            // UnitTurret.control: a held hostile turret wakes straight into attack,
            // without surrendering its body position to ordinary AI locomotion.
            if(ActorHoldRules.Active(actor) && (state.mode==Watch || state.mode==Alert)) state.mode=Attack;
            state.timer=Mathf.Max(0,state.timer-dt);
            Vector2? seen=visible?target.BodyPosition+Vector2.up*.55f:(Vector2?)null;
            switch(state.mode)
            {
                case Alert:
                    if(seen.HasValue) Turn(actor,seen.Value,settings.attackTurnDegreesPerSecond,room,dt,settings);
                    if(state.timer<=0) {state.mode=seen.HasValue?Attack:Search;state.timer=settings.searchSeconds;}
                    break;
                case Attack:
                    if(seen.HasValue) Turn(actor,seen.Value,settings.attackTurnDegreesPerSecond,room,dt,settings);
                    else {state.mode=Search;state.timer=settings.searchSeconds;}
                    break;
                case Search:
                    if(seen.HasValue) {state.mode=Attack;Turn(actor,seen.Value,settings.attackTurnDegreesPerSecond,room,dt,settings);break;}
                    if(known.HasValue) Turn(actor,known.Value+Vector2.up*.55f,settings.watchTurnDegreesPerSecond,room,dt,settings);
                    if(state.timer<=0) {state.mode=Watch;state.timer=settings.periodSeconds;}
                    break;
                default:   // watch
                    if(seen.HasValue)
                    {
                        state.mode=Alert;
                        state.timer=settings.alertMinSeconds+(float)random.NextDouble()*(settings.alertMaxSeconds-settings.alertMinSeconds);
                        break;
                    }
                    if(state.timer<=0) {state.directionX=(state.directionX+1)%settings.patrolAngles.Length;state.timer=settings.periodSeconds;}
                    TurnTo(actor,Patrol(actor,settings,(int)state.directionX),settings.watchTurnDegreesPerSecond,dt,settings);
                    break;
            }
            ActorPose.Select(actor,state.mode==Watch?"idle":state.mode);
            return state.mode==Attack && seen.HasValue;
        }

        // While an action resolves (aiming, reloading) the gun keeps following its target at the attack rate.
        public static void Follow(ActorState actor,ActorTypeDefinition type,ActorState target,bool visible,RoomLayout room,float dt)
        {if(visible) Turn(actor,target.BodyPosition+Vector2.up*.55f,type.turret.attackTurnDegreesPerSecond,room,dt,type.turret);}

        private static NpcCreatureState Initial(ActorState actor,TurretSettings settings)
        {
            // UnitTurret.putLoc: a random watch angle to start with (stable per turret here), the gun already there.
            int index=(int)((uint)actor.id.Aggregate(17,(hash,c)=>unchecked(hash*31+c))%(uint)settings.patrolAngles.Length);
            var state=new NpcCreatureState {mode=Watch,timer=settings.periodSeconds,directionX=index};
            actor.weaponAngle=Patrol(actor,settings,index);actor.weaponFlipY=Mathf.Cos(actor.weaponAngle*Mathf.Deg2Rad)<0;
            return state;
        }
        // The watch angles are given for a turret facing right; an armored turret facing left watches mirrored.
        private static float Patrol(ActorState actor,TurretSettings settings,int index)
        {
            float angle=settings.patrolAngles[index%settings.patrolAngles.Length];
            return settings.fixedFacing && actor.facing<0?180-angle:angle;
        }
        private static void Turn(ActorState actor,Vector2 point,float speed,RoomLayout room,float dt,TurretSettings settings)
        {
            float previous=actor.weaponAngle;
            WeaponPose.Aim(actor,point,room);
            float wanted=actor.weaponAngle;actor.weaponAngle=previous;
            TurnTo(actor,wanted,speed,dt,settings);
        }
        private static void TurnTo(ActorState actor,float wanted,float speed,float dt,TurretSettings settings)
        {
            actor.weaponAngle=Mathf.MoveTowardsAngle(actor.weaponAngle,wanted,speed*dt);
            bool left=Mathf.Cos(actor.weaponAngle*Mathf.Deg2Rad)<0;
            actor.weaponFlipY=left;
            // It looks where its gun points (its perception sees that side); an armored turret keeps its side.
            if(!settings.fixedFacing) actor.facing=left?-1:1;
        }
    }
}
