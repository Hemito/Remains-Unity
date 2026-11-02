using System;
using UnityEngine;

namespace Remains.Domain
{
    // Lying on a bed (user 2026-11-01): E at a bed lays the pony on it in one of two poses — on its belly (the last frame
    // of the fall, `down`) or on its back (an authored pose, clip BackClip) — and W or Space gets it up. The state is the
    // actor's own (ActorState.rest/restPose, sent with its motion); the bed is room geometry (RoomLayout.beds).
    public static class BedRest
    {
        public const string Belly="belly",Back="back";
        public const string BellyClip="down",BackClip="bedBack";
        // From the bed's nearest edge, metres; the pointer box reaches this far above its top.
        public const float Reach=1.2f,PointerAbove=.5f;

        public static bool Active(ActorState actor)=>!string.IsNullOrEmpty(actor.rest);
        // Hooves up: the back pose stands on nothing, so it has no foot-support silhouette (the bed carries the body).
        public static bool HasFootSupport(string clip)=>clip!=BackClip;
        public static string Clip(ActorState actor)=>actor.restPose==Back?BackClip:BellyClip;
        public static Rect Bounds(RoomRest bed)=>new(bed.x,bed.y,bed.width,bed.height+PointerAbove);

        public static string Rejection(ActorState actor,RoomRest bed)
        {
            if(actor==null || bed==null) return "Нет кровати.";
            if(actor.down || !actor.connected) return "Без сознания нельзя лечь.";
            if(Active(actor)) return "Уже лежит.";
            if(actor.climbing || ActorHoldRules.Active(actor)) return "Сначала освободитесь.";
            float dx=Mathf.Max(0,Mathf.Max(bed.x-actor.x,actor.x-(bed.x+bed.width)));
            if(dx>Reach || Mathf.Abs(actor.y-bed.y)>bed.height+Reach) return "Подойдите ближе к кровати.";
            return null;
        }

        // On the bed's top at its middle, still; the pose time past the clip so its last frame shows at once.
        public static void LieDown(ActorState actor,RoomRest bed,string pose)
        {
            if(pose!=Belly && pose!=Back) throw new ArgumentException("Неизвестная поза на кровати: "+pose);
            string reason=Rejection(actor,bed);
            if(reason!=null) throw new InvalidOperationException($"Лечь на кровать {bed?.id} нельзя: {reason}");
            actor.rest=bed.id;actor.restPose=pose;
            actor.x=bed.x+bed.width*.5f;actor.y=bed.y+bed.height;actor.vx=actor.vy=0;actor.crouched=false;
            actor.poseTime=ActorPose.PassiveSettleSeconds;actor.poseSequence=unchecked(actor.poseSequence+1);
            actor.revision++;
        }

        // Up from the bed, standing where it lay; gravity takes it from the bed's top.
        public static void GetUp(ActorState actor)
        {
            if(!Active(actor)) return;
            actor.rest=null;actor.restPose=null;
            ActorPose.Select(actor,"idle");actor.poseSequence=unchecked(actor.poseSequence+1);
            actor.revision++;
        }

        // A resting body's frame: knocked down or gone, it is off the bed; W or Space gets it up; otherwise it lies still.
        // True while it still rests.
        public static bool Step(ActorState actor,bool up)
        {
            if(!Active(actor)) return false;
            if(actor.down || !actor.connected || up) {GetUp(actor);return false;}
            actor.vx=actor.vy=0;
            return true;
        }
    }
}
