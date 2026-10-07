using UnityEngine;
using Remains.Content;
using Remains.Domain;

namespace Remains.Gameplay
{
    public readonly struct RangedWeaponPose
    {
        public readonly Vector2 Root,Direction,Muzzle;
        public readonly float Angle;
        public readonly bool Mirrored;
        public RangedWeaponPose(Vector2 root,float angle,bool mirrored,WeaponRig rig)
        {
            Root=root;Angle=angle;Mirrored=mirrored;
            Direction=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));
            Muzzle=root+Direction*rig.muzzleX+new Vector2(-Direction.y,Direction.x)*(mirrored?-rig.muzzleY:rig.muzzleY);
        }
        public Vector2 Socket(Vector2 local)=>Root+Direction*local.x+new Vector2(-Direction.y,Direction.x)*(Mirrored?-local.y:local.y);
        public RangedShotPose Capture(ActorState actor,string worldId=null)=>new()
        {
            worldId=worldId,rootX=Root.x,rootY=Root.y,angleDegrees=Angle,mirrored=Mirrored,
            weaponItem=actor.equippedWeaponItem,selectionRevision=actor.weaponSelectionRevision,
            actorGeneration=actor.generation,actorLifecycle=actor.lifecycleSequence
        };
    }
    public static class WeaponPose
    {
        public static float AimRadius=>WeaponRigs.AimRadius;
        public static float RangedReachLimit(ActorState actor)=>AtlasWeaponSockets.Enabled(actor)?0:actor.climbing?WeaponRigs.ClimbingAimRadius:AimRadius;
        public static Vector2 RangedAnchor(ActorState actor)=>AtlasWeaponSockets.Enabled(actor)?AtlasWeaponSockets.Anchor(actor):actor.Position+new Vector2(0,WeaponRigs.AnchorHeight*Anatomy.StanceHeightRatio(actor)+
            (ActorSessionRules.IsPassive(actor) || actor.climbing || actor.crouched?0:Mathf.Clamp(actor.weaponLift,0,WeaponRigs.RaiseHeight)));
        private static Vector2 BaseDirection(ActorState actor)=>new(Mathf.Cos(actor.weaponAngle*Mathf.Deg2Rad),Mathf.Sin(actor.weaponAngle*Mathf.Deg2Rad));
        public static Vector2 Direction(ActorState actor)=>!string.IsNullOrEmpty(actor.weapon) && WeaponRigs.Get(actor.weapon).HasMuzzle?SampleRanged(actor).Direction:BaseDirection(actor);
        public static bool Flip(ActorState actor)=>actor.weaponFlipY;
        private static Vector2 BaseMount(ActorState actor)=>actor.weapon=="grenade"?HeldItemPose.Mount(actor):RangedAnchor(actor)+Vector2.right*(Flip(actor)?-1:1)*Mathf.Clamp(actor.weaponReach,0,RangedReachLimit(actor));
        public static RangedWeaponPose SampleRanged(ActorState actor)
        {
            var rig=WeaponRigs.Get(actor.weapon);var root=BaseMount(actor);float angle=actor.weaponAngle;
            if(rig.HasMuzzle && actor.weaponRecoil!=null)
            {
                if(!actor.weaponRecoil.IsValid) actor.weaponRecoil.Validate("actor="+actor.id+", item="+actor.equippedWeaponItem);
                root.x+=actor.weaponRecoil.offsetX;angle+=actor.weaponRecoil.angleDegrees;
            }
            return new RangedWeaponPose(root,angle,Flip(actor),rig);
        }
        public static Vector2 Mount(ActorState actor)=>!string.IsNullOrEmpty(actor.weapon) && WeaponRigs.Get(actor.weapon).HasMuzzle?SampleRanged(actor).Root:BaseMount(actor);
        public static Vector2 Muzzle(ActorState actor)=>SampleRanged(actor).Muzzle;
        public static void Aim(ActorState actor,Vector2 target,RoomLayout room=null)
        {
            if(actor.weapon=="grenade") {HeldItemPose.Aim(actor,target,room);return;}
            AimWithReach(actor,target,RangedReachLimit(actor));
            LowerUnderCeiling(actor,target,room,RangedReachLimit(actor));
            if(room!=null && !string.IsNullOrEmpty(actor.weapon) && WeaponRigs.Get(actor.weapon).HasMuzzle &&
                ShotObstacles.FirstContact(room,RangedAnchor(actor),BaseMuzzle(actor)).HasValue)
            {
                AimWithReach(actor,target,0);
                LowerUnderCeiling(actor,target,room,0);
            }
        }
        private static void LowerUnderCeiling(ActorState actor,Vector2 target,RoomLayout room,float reachLimit)
        {
            if(room==null || actor.weaponLift<=0 || string.IsNullOrEmpty(actor.weapon) || !WeaponRigs.Get(actor.weapon).HasMuzzle) return;
            var mount=BaseMount(actor);
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
                delta=target-BaseMount(actor);
                angle=Mathf.Atan2(delta.y,delta.x);
                float side=delta.x<0?-1:1;
                float lateral=side*rig.muzzleY;
                // Solve the barrel ray through the cursor, including its authored vertical offset.
                angle-=Mathf.Asin(Mathf.Clamp(lateral/delta.magnitude,-1,1));
            }
            else actor.weaponReach=Mathf.Min(delta.magnitude,MeleeReachLimit(actor));
            actor.weaponAngle=angle*Mathf.Rad2Deg;
        }
        private static Vector2 BaseMuzzle(ActorState actor)=>new RangedWeaponPose(BaseMount(actor),actor.weaponAngle,Flip(actor),WeaponRigs.Get(actor.weapon)).Muzzle;
        public static float MeleeReachLimit(ActorState actor)=>WeaponCatalog.Get(actor.weapon).holdingArea.RadiusFor(actor.climbing);
        public static Vector2 Origin(Vector2 actorPosition)=>actorPosition+new Vector2(0,.8f);
        public static Vector2 Origin(ActorState actor)=>actor.Position+new Vector2(0,.8f*Anatomy.StanceHeightRatio(actor));
        public static Vector2 Tip(string weapon,Vector2 origin,Vector2 direction,float progress,float? restReach=null)
        {
            var definition=WeaponCatalog.Get(weapon);
            var keys=definition.meleeMotion??throw new System.InvalidOperationException("Нет траектории оружия: "+weapon);
            float rest=Mathf.Clamp(restReach??definition.range,0,definition.holdingArea.radius);
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
