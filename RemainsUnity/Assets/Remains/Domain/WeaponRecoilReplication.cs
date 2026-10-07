using System;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    // Recoil is a live pose sample, never a reason to reject valid body motion.
    public static class WeaponRecoilReplication
    {
        public static bool SameSelection(ActorState source,ActorState destination)=>source!=null && destination!=null &&
            source.weaponSelectionRevision==destination.weaponSelectionRevision && source.equippedWeaponItem==destination.equippedWeaponItem &&
            source.weapon==destination.weapon && source.weaponInventoryVersion==destination.weaponInventoryVersion;

        public static void Reset(ActorState actor,long pauseRevision)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor),"Нет актёра для сброса отдачи.");
            if(pauseRevision < -1) throw new ArgumentOutOfRangeException(nameof(pauseRevision),"Некорректная эпоха паузы отдачи.");
            actor.weaponRecoil=CopyNeutral(actor.weaponRecoil,pauseRevision);
        }
        public static void NeutralizeWorld(WorldState world,long pauseRevision)
        {
            if(world==null) throw new ArgumentNullException(nameof(world),"Нет нового мира для сброса отдачи.");
            foreach(var actor in world.actors)
                Normalize(actor,pauseRevision,true,$"новый live мир {world.worldId}");
        }
        public static void Normalize(ActorState actor,long pauseRevision,bool paused,string context,Action<string> warning=null)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor),"Нет актёра для проверки отдачи.");
            bool valid=Validate(actor.weaponRecoil,actor.id,context,warning);
            if(valid && actor.weaponRecoil!=null && actor.weaponRecoil.pauseRevision==pauseRevision &&
                (!paused || actor.weaponRecoil.offsetX==0 && actor.weaponRecoil.angleDegrees==0)) return;
            Reset(actor,pauseRevision);
        }
        public static void Reconcile(ActorState current,ActorState incoming,string localOwner,long pauseRevision,bool paused,
            string context,Action<string> warning=null)
        {
            // Validate incoming even when its sample will be superseded by an owner sample.
            Normalize(incoming,pauseRevision,paused,context,warning);
            if(!ActorMotionReplication.SameLifetime(current,incoming) || !SameSelection(current,incoming))
            {Reset(incoming,pauseRevision);return;}
            if(current.owner==localOwner && localOwner!=null || !ActorMotionReplication.IsNewer(incoming.motionSequence,current.motionSequence))
            {
                Normalize(current,pauseRevision,paused,context+" / текущий",warning);
                incoming.weaponRecoil=current.weaponRecoil.Copy();
            }
            Normalize(incoming,pauseRevision,paused,context,warning);
        }
        public static void ApplyPoseSample(ActorState actor,WeaponRecoilPose sample,long selectionRevision,string definition,
            long pauseRevision,bool paused,string context,Action<string> warning=null)
        {
            bool valid=Validate(sample,actor.id,context,warning);
            // A delayed weapon selection cannot overwrite the present weapon sample;
            // body pose is accepted independently by its own epoch.
            if(selectionRevision!=actor.weaponSelectionRevision)
            {Normalize(actor,pauseRevision,paused,context,warning);return;}
            if(definition!=actor.weapon) {Reset(actor,pauseRevision);return;}
            if(!valid)
            {
                actor.weaponRecoil=CopyNeutral(sample,pauseRevision);return;
            }
            actor.weaponRecoil=sample?.Copy()??WeaponRecoilPose.Neutral(pauseRevision);
            Normalize(actor,pauseRevision,paused,context,warning);
        }
        private static WeaponRecoilPose CopyNeutral(WeaponRecoilPose sample,long pauseRevision)
        {
            // Validate extensions through their DTO contract; copying the dictionary
            // alone would bypass unknown-number and conflicting-field checks.
            var neutral=WeaponRecoilPose.Neutral(pauseRevision);
            neutral.extensions=sample?.extensions;
            return neutral.extensions==null?neutral:StateJson.Copy(neutral);
        }
        private static bool Validate(WeaponRecoilPose sample,string actor,string context,Action<string> warning)
        {
            if(sample==null) return true; // Explicit legacy neutralization is performed by the caller.
            try {sample.Validate(context);return true;}
            catch(InvalidOperationException failure)
            {
                string message=$"Образец отдачи отклонён; поза тела сохранена: actor={actor}, context={context}. {failure.Message}";
                if(warning!=null) warning(message);else Debug.LogWarning(message);
                return false;
            }
        }
    }
}
