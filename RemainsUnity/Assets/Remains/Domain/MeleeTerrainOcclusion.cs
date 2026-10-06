using System;
using UnityEngine;
using Remains.Serialization;

namespace Remains.Domain
{
    // Supports are sampled at the same times as the two tip endpoints, never from a later visual pose.
    [Serializable]
    public sealed class MeleeAttackAnchor : ExtensibleState
    {
        public int version=1;
        public float startX,startY,endX,endY;
        public Vector2 Start=>new(startX,startY);
        public Vector2 End=>new(endX,endY);
        public MeleeAttackAnchor() {}
        public MeleeAttackAnchor(Vector2 start,Vector2 end)
        {startX=start.x;startY=start.y;endX=end.x;endY=end.y;}
    }

    // The tip may contact a blocking surface, but cannot hit something beyond the reachable shaft.
    // Uses the deciding client's current geometry, exactly like its current anatomical hit poses.
    public static class MeleeTerrainOcclusion
    {
        private const float ContactPrecision=.00001f;
        public static bool Validate(AttackEvent attack,out string reason)
        {
            var anchor=attack?.meleeAnchor;
            if(anchor==null || anchor.version!=1 || !Finite(anchor.Start) || !Finite(anchor.End) ||
                !Finite(attack.Origin) || !Finite(attack.End) || !float.IsFinite(attack.radius) || attack.radius<0)
            {
                reason=$"Нет корректной опоры сегмента ближнего удара: attack={attack?.id}, source={attack?.attacker}, weapon={attack?.weapon}, level={attack?.level}, anchorVersion={anchor?.version}, anchorStart=({anchor?.startX},{anchor?.startY}), anchorEnd=({anchor?.endX},{anchor?.endY}), origin=({attack?.originX},{attack?.originY}), end=({attack?.endX},{attack?.endY}), radius={attack?.radius}.";
                return false;
            }
            reason=null;return true;
        }

        public static Vector2 ClipTip(RoomLayout room,Vector2 anchor,Vector2 proposedTip,float radius,bool stopAtTerrain)
        {
            if(!stopAtTerrain) return proposedTip;
            var contact=ShotObstacles.FirstContact(room,anchor,proposedTip,out _,radius);
            return contact.HasValue?Vector2.Lerp(anchor,proposedTip,contact.Value):proposedTip;
        }

        // Call only after Validate. fraction is the actual swept contact, not the segment's end.
        public static bool IsReachable(RoomLayout room,AttackEvent attack,float fraction,bool stopAtTerrain)
        {
            if(!stopAtTerrain) return true;
            var anchor=Vector2.Lerp(attack.meleeAnchor.Start,attack.meleeAnchor.End,fraction);
            var tip=Vector2.Lerp(attack.Origin,attack.End,fraction);
            var contact=ShotObstacles.FirstContact(room,anchor,tip,out _,attack.radius);
            return !contact.HasValue || (1-contact.Value)*Vector2.Distance(anchor,tip)<=ContactPrecision;
        }

        // Contact with the boundary is observable, but sliding along it is not entering the solid.
        // The ten-micrometre allowance only separates tangency from numerical penetration.
        public static float ContactLimit(RoomLayout room,AttackEvent attack,bool stopAtTerrain)
        {
            if(!stopAtTerrain) return 1;
            float limit=1;
            foreach(var surface in RoomSurfaces.All(room))
            {
                if(surface.shotsPassThrough) continue;
                var bounds=new Rect(surface.x,surface.y,surface.width,surface.height);
                float radius=Mathf.Max(0,attack.radius-ContactPrecision);
                if(attack.radius<ContactPrecision)
                {
                    float inset=ContactPrecision-attack.radius;
                    if(bounds.width>inset*2 && bounds.height>inset*2)
                        bounds=new Rect(bounds.x+inset,bounds.y+inset,bounds.width-inset*2,bounds.height-inset*2);
                }
                var entry=SurfaceContact.Entry(bounds,attack.Origin,attack.End,radius);
                if(entry.HasValue) limit=Mathf.Min(limit,entry.Value);
            }
            return limit;
        }

        private static bool Finite(Vector2 point)=>float.IsFinite(point.x) && float.IsFinite(point.y);
    }
}
