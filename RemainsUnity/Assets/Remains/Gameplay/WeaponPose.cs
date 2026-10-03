using UnityEngine;
using Remains.Content;
using Remains.Domain;

namespace Remains.Gameplay
{
    public static class WeaponPose
    {
        public static float AimRadius=>WeaponRigs.AimRadius;
        public static float RangedReachLimit(ActorState actor)=>actor.climbing?WeaponRigs.ClimbingAimRadius:AimRadius;
        public static Vector2 RangedAnchor(ActorState actor)=>actor.Position+new Vector2(0,WeaponRigs.AnchorHeight*Anatomy.StanceHeightRatio(actor)+
            (actor.down || actor.climbing || actor.crouched?0:Mathf.Clamp(actor.weaponLift,0,WeaponRigs.RaiseHeight)));
        public static Vector2 Direction(ActorState actor)=>new(Mathf.Cos(actor.weaponAngle*Mathf.Deg2Rad),Mathf.Sin(actor.weaponAngle*Mathf.Deg2Rad));
        public static bool Flip(ActorState actor)=>actor.weaponFlipY;
        public static Vector2 Mount(ActorState actor)=>RangedAnchor(actor)+Vector2.right*(Flip(actor)?-1:1)*Mathf.Clamp(actor.weaponReach,0,RangedReachLimit(actor));
        public static Vector2 Muzzle(ActorState actor)
        {
            var rig=WeaponRigs.Get(actor.weapon);var direction=Direction(actor);
            return Mount(actor)+direction*rig.muzzleX+new Vector2(-direction.y,direction.x)*(Flip(actor)?-rig.muzzleY:rig.muzzleY);
        }
        public static void Aim(ActorState actor,Vector2 target,RoomLayout room=null)
        {
            AimWithReach(actor,target,RangedReachLimit(actor));
            LowerUnderCeiling(actor,target,room,RangedReachLimit(actor));
            if(room!=null && !string.IsNullOrEmpty(actor.weapon) && WeaponRigs.Get(actor.weapon).HasMuzzle &&
                ShotObstacles.FirstContact(room,RangedAnchor(actor),Muzzle(actor)).HasValue)
            {
                AimWithReach(actor,target,0);
                LowerUnderCeiling(actor,target,room,0);
            }
        }
        private static void LowerUnderCeiling(ActorState actor,Vector2 target,RoomLayout room,float reachLimit)
        {
            if(room==null || actor.weaponLift<=0 || string.IsNullOrEmpty(actor.weapon) || !WeaponRigs.Get(actor.weapon).HasMuzzle) return;
            var mount=Mount(actor);
            if(RoomVisibility.Clear(room,mount-Vector2.up*actor.weaponLift,mount)) return;
            actor.weaponLift=0;AimWithReach(actor,target,reachLimit);
        }
        private static void AimWithReach(ActorState actor,Vector2 target,float reachLimit)
        {
            if(string.IsNullOrEmpty(actor.weapon)) return;
            var rig=WeaponRigs.Get(actor.weapon);
            var delta=target-(rig.HasMuzzle?RangedAnchor(actor):Origin(actor));
            if(delta.sqrMagnitude<.000001f)
            {
                actor.weaponReach=0;
                return;
            }
            actor.weaponFlipY=delta.x<0;
            float angle=Mathf.Atan2(delta.y,delta.x);
            if(rig.HasMuzzle)
            {
                // The support moves horizontally; barrel elevation must not orbit the whole gun around the actor.
                // Retract continuously near the body so the forward muzzle does not overshoot a nearby cursor.
                actor.weaponReach=Mathf.Clamp(Mathf.Abs(delta.x)-rig.muzzleX-.1f,0,reachLimit);
                delta=target-Mount(actor);
                angle=Mathf.Atan2(delta.y,delta.x);
                float side=delta.x<0?-1:1;
                float lateral=side*rig.muzzleY;
                // Solve the barrel ray through the cursor, including its authored vertical offset.
                angle-=Mathf.Asin(Mathf.Clamp(lateral/delta.magnitude,-1,1));
            }
            else actor.weaponReach=Mathf.Min(delta.magnitude,WeaponCatalog.Get(actor.weapon).range);
            actor.weaponAngle=angle*Mathf.Rad2Deg;
        }
        public static Vector2 Origin(Vector2 actorPosition)=>actorPosition+new Vector2(0,.8f);
        public static Vector2 Origin(ActorState actor)=>actor.Position+new Vector2(0,.8f*Anatomy.StanceHeightRatio(actor));
        public static Vector2 Tip(string weapon,Vector2 origin,Vector2 direction,float progress,float? restReach=null)
        {
            var definition=WeaponCatalog.Get(weapon);
            var keys=definition.meleeMotion??throw new System.InvalidOperationException("Нет траектории оружия: "+weapon);
            float rest=Mathf.Clamp(restReach??definition.range,0,definition.range);
            progress=Mathf.Clamp01(progress);
            int index=1;
            while(index<keys.Length-1 && keys[index].time<progress) index++;
            var from=keys[index-1];var to=keys[index];
            float blend=Mathf.InverseLerp(from.time,to.time,progress);
            float reach=Mathf.Lerp(from.rest?rest:from.reach*definition.range,to.rest?rest:to.reach*definition.range,blend);
            float angle=Mathf.Lerp(from.angle,to.angle,blend)*(direction.x<0?-1:1)*Mathf.Deg2Rad;
            return origin+new Vector2(direction.x*Mathf.Cos(angle)-direction.y*Mathf.Sin(angle),
                direction.x*Mathf.Sin(angle)+direction.y*Mathf.Cos(angle))*reach;
        }
    }
}
