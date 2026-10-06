using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Independent 2D hold contract. Sprite placement and both release paths read this pose.
    public static class HeldItemPose
    {
        private static readonly Dictionary<string,(long epoch,bool nonfinite,string level,Rect bounds)> diagnostics=new(StringComparer.Ordinal);
        public const float ClearanceRadius=.2f,SurfaceGap=.01f;
        public static Vector2 Anchor(ActorState actor)=>WeaponPose.Origin(actor);
        public static float ReachLimit(ActorState actor)=>
            (HeldItemSelection.HasSelection(actor)?WeaponRigs.OrdinaryHoldingArea:WeaponCatalog.Get("grenade").holdingArea).RadiusFor(actor.climbing);
        public static Vector2 Mount(ActorState actor)=>Anchor(actor)+new Vector2(actor.heldItemPoseX,actor.heldItemPoseY);
        public static bool Aim(ActorState actor,Vector2 target,RoomLayout room=null)
        {
            var anchor=Anchor(actor);var delta=target-anchor;
            if(!float.IsFinite(delta.sqrMagnitude))
            {
                actor.heldItemPoseValid=false;
                if(ReportNeeded(actor,true)) Debug.LogWarning($"Удерживание: неконечная цель, actor={actor.id}, item={actor.heldItemId ?? actor.equippedWeaponItem}, target={target}.");return false;
            }
            var offset=Vector2.ClampMagnitude(delta,ReachLimit(actor));
            if(room!=null)
            {
                foreach(var surface in RoomSurfaces.All(room))
                {
                    if(surface.oneWay) continue;
                    var contact=SurfaceContact.Entry(surface,anchor,anchor+offset,ClearanceRadius);
                    if(!contact.HasValue) continue;
                    if(contact.Value==0)
                    {
                        actor.heldItemPoseValid=false;
                        if(ReportNeeded(actor,false,surface)) Debug.LogWarning($"Удерживание: исходная опора внутри препятствия, actor={actor.id}, item={actor.heldItemId ?? actor.equippedWeaponItem}, level={actor.level}, anchor={anchor}, bounds=({surface.x},{surface.y},{surface.width},{surface.height}).");return false;
                    }
                    float length=offset.magnitude;
                    offset*=Mathf.Max(0,contact.Value-SurfaceGap/length);
                }
            }
            if(actor.id!=null) diagnostics.Remove(actor.id);
            actor.heldItemPoseX=offset.x;actor.heldItemPoseY=offset.y;actor.heldItemPoseValid=true;
            actor.weaponFlipY=delta.x<0;
            actor.weaponAngle=delta.sqrMagnitude==0?(actor.facing<0?180:0):Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
            return true;
        }
        private static bool ReportNeeded(ActorState actor,bool nonfinite,RoomSurface surface=null)
        {
            if(actor.id==null) return true;
            var key=(actor.weaponSelectionRevision,nonfinite,actor.level,surface==null?default:new Rect(surface.x,surface.y,surface.width,surface.height));
            if(diagnostics.TryGetValue(actor.id,out var previous) && previous.Equals(key)) return false;
            diagnostics[actor.id]=key;
            return true;
        }
        public static bool TryQuickLaunch(ActorState actor,Vector2 aim,RoomLayout room,out HeldLaunch launch)
        {
            // A grenade-specific local sample leaves the ordinary active selection and its pose untouched.
            var grenadePose=new ActorState {id=actor.id,level=actor.level,anatomy=actor.anatomy,crouched=actor.crouched,
                climbing=actor.climbing,facing=actor.facing,x=actor.x,y=actor.y,equippedWeaponItem="grenade:quick"};
            if(!Aim(grenadePose,aim,room)) {launch=default;return false;}
            launch=Capture(grenadePose,aim);return true;
        }
        public static HeldLaunch Capture(ActorState actor,Vector2 aim)
        {
            if(!actor.heldItemPoseValid || !float.IsFinite(actor.weaponAngle) || !float.IsFinite(actor.heldItemPoseX) || !float.IsFinite(actor.heldItemPoseY))
                throw new InvalidOperationException($"Нет готовой позы удерживаемого предмета: actor={actor.id}, item={actor.heldItemId ?? actor.equippedWeaponItem}, selection={actor.weaponSelectionRevision}.");
            var origin=Mount(actor);
            return new HeldLaunch(origin,GrenadeLaunch.Velocity(origin,aim,actor.y,WeaponCatalog.Get("grenade")),actor.weaponAngle,actor.weaponFlipY);
        }
    }
    public readonly struct HeldLaunch
    {
        public readonly Vector2 origin,velocity;
        public readonly float angle;
        public readonly bool mirrored;
        public HeldLaunch(Vector2 origin,Vector2 velocity,float angle,bool mirrored)
        {this.origin=origin;this.velocity=velocity;this.angle=angle;this.mirrored=mirrored;}
    }
}
