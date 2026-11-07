using System.Collections.Generic;
using Remains.Content;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Domain
{
    // Friendly fire is on for everyone (user 2026-11-05, PT-20261103-29), so a sapient NPC holds its attack while another
    // one stands on the line to its target instead of hitting it. Asked only by an NPC that sees a target.
    public static class NpcLineOfFire
    {
        // How close to the line a body counts, and the stretch at the shooter's and the target's ends it may stand in.
        public const float BodyRadius=.45f,EndMargin=.3f;
        private const float BodyHeight=.55f;

        public static bool Blocked(IReadOnlyList<ActorState> actors,ActorState shooter,ActorState target,Vector2 from,Vector2 to,bool ranged=false)
        {
            var line=to-from;float length=line.magnitude;
            if(length<=EndMargin*2) return false;
            var along=line/length;
            for(int i=0;i<actors.Count;i++)
            {
                var other=actors[i];
                if(other.player || other.down || other.level!=shooter.level || ReferenceEquals(other,shooter) || ReferenceEquals(other,target)) continue;
                if(ActorPose.IsTacticallyProne(other))
                {
                    var start=from;var end=to;
                    if(ranged && AtlasWeaponSockets.Enabled(shooter) && !string.IsNullOrEmpty(shooter.weapon) &&
                        WeaponRigs.Get(shooter.weapon).HasMuzzle && !TryAimSegment(shooter,to,out start,out end)) continue;
                    if(Anatomy.ProneContact(other,start,end).HasValue) return true;
                    continue;
                }
                // Most of a factory is far from the line: the box before the body's sampled pose and its type.
                var center=new Vector2(other.x,other.y+BodyHeight);
                var offset=center-from;float ahead=Vector2.Dot(offset,along);
                if(ahead<EndMargin || ahead>length-EndMargin || Mathf.Abs(along.x*offset.y-along.y*offset.x)>BodyRadius) continue;
                if(ActorPresence.IsPresent(other) && !ActorPresence.Hidden(other) && SapientPonies.Is(other)) return true;
            }
            return false;
        }

        public static bool ShouldLieProne(IReadOnlyList<ActorState> actors,ActorState front,ActorState target,RoomLayout room)
        {
            var aim=target.BodyPosition+Vector2.up*BodyHeight;
            float forward=Mathf.Sign(aim.x-front.x);
            if(front.facing!=forward || Mathf.Abs(aim.x-front.x)<EndMargin) return false;
            bool prone=ActorPose.IsTacticallyProne(front);
            float height=Anatomy.Movement(front.anatomy).height;
            for(int i=0;i<actors.Count;i++)
            {
                var rear=actors[i];
                float behind=(front.x-rear.x)*forward;
                if(behind<=EndMargin || rear.player || rear.down || rear.health<=0 || rear.climbing || rear.facing!=forward ||
                    rear.level!=front.level || (rear.attackBusy?rear.attackTarget:rear.target)!=target.id ||
                    Mathf.Abs(rear.y-front.y)>height) continue;
                var type=ActorTypes.Get(rear.actorType);
                if(type.raider is not {attacker:RaiderAttacker.Ranged,robot:false,zombie:false} || !type.atlasWeaponSocket ||
                    string.IsNullOrEmpty(rear.weapon) || !WeaponRigs.Get(rear.weapon).HasMuzzle || ActorHoldRules.Active(rear)) continue;
                var weapon=WeaponCatalog.Get(rear.weapon);
                float distance=Vector2.Distance(aim,rear.Position);
                if(distance>Mathf.Min(type.raider.holdDistance,weapon.range) ||
                    !WeaponAmmunition.CanSpendShot(rear,weapon,out _)) continue;
                if(!TryAimSegment(rear,aim,out var start,out var end) || Anatomy.ProneContact(front,start,end).HasValue) continue;
                // While already prone, sample the standing idle frame that would
                // replace it when the front pony gets up.
                bool blocksStanding=Anatomy.Contact(front,start,end,0,out _,standing:prone)!=null;
                if(blocksStanding && RoomVisibility.Clear(room,start,end)) return true;
            }
            return false;
        }

        private static bool TryAimSegment(ActorState shooter,Vector2 aim,out Vector2 start,out Vector2 end)
        {
            var pose=WeaponPose.PreviewAtlasAim(shooter,aim);
            start=pose.Muzzle;
            float reach=Vector2.Dot(aim-start,pose.Direction);
            end=start+pose.Direction*reach;
            return reach>0;
        }

        // Use the real prepared trajectories, including spread and every pellet,
        // before the attack spends ammunition. Respect the projectile's first stop.
        public static bool SafeVolley(IReadOnlyList<ActorState> actors,IReadOnlyList<AttackEvent> volley,RoomLayout room,bool friendlyFire)
        {
            foreach(var shot in volley)
            {
                var weapon=WeaponCatalog.Get(shot.weapon);
                float terrain=weapon.stopAtTerrain?ShotObstacles.FirstContact(room,shot.Origin,shot.End)??1:1;
                ActorState ally=null;float distance=terrain;
                for(int i=0;i<actors.Count;i++)
                {
                    var actor=actors[i];
                    if(!AttackScope.CanAffect(shot,actor,friendlyFire) || !LivingAlly(actor)) continue;
                    if(Anatomy.Contact(actor,shot,out float entry)==null || entry>=terrain) continue;
                    if(!weapon.stopAtFirstActor) return false;
                    if(entry<distance || entry==distance && string.CompareOrdinal(actor.id,ally?.id)<0)
                    {ally=actor;distance=entry;}
                }
                if(ally!=null && !OtherStopsShot(actors,shot,friendlyFire,ally,distance)) return false;
            }
            return true;
        }
        private static bool OtherStopsShot(IReadOnlyList<ActorState> actors,AttackEvent shot,bool friendlyFire,ActorState ally,float distance)
        {
            for(int i=0;i<actors.Count;i++)
            {
                var actor=actors[i];
                if(ReferenceEquals(actor,ally) || !AttackScope.CanAffect(shot,actor,friendlyFire) || LivingAlly(actor)) continue;
                if(Anatomy.Contact(actor,shot,out float entry)!=null &&
                    (entry<distance || entry==distance && string.CompareOrdinal(actor.id,ally.id)<0)) return true;
            }
            return false;
        }
        private static bool LivingAlly(ActorState actor)=>!actor.player && actor.health>0 && SapientPonies.Is(actor);
    }
}
