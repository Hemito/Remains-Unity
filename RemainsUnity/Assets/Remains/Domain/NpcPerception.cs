using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class NpcPerceptionSettings
    {
        [Min(.01f),InspectorName("Дальность зрения")] public float sightDistance;
        [Min(0),InspectorName("Близкое обнаружение позади")] public float closeDistance;
        [Min(.01f),InspectorName("Время распознавания, с")] public float recognitionSeconds;
        [Min(.01f),InspectorName("Память контакта, с")] public float memorySeconds;
        [Min(0),InspectorName("Дальность реакции на шум")] public float hearingDistance;
        [Min(0),InspectorName("Высота точки зрения")] public float eyeHeight;
        [Min(0),InspectorName("Высота наблюдаемой точки")] public float targetHeight;

        public void Validate()
        {
            if(!float.IsFinite(sightDistance) || sightDistance<=0 || !float.IsFinite(closeDistance) || closeDistance<0 || closeDistance>sightDistance ||
                !float.IsFinite(recognitionSeconds) || recognitionSeconds<=0 || !float.IsFinite(memorySeconds) || memorySeconds<=0 ||
                !float.IsFinite(hearingDistance) || hearingDistance<0 || !float.IsFinite(eyeHeight) || eyeHeight<0 ||
                !float.IsFinite(targetHeight) || targetHeight<0)
                throw new InvalidOperationException("Восприятие NPC: неверные дальности, высоты или время реакции/памяти.");
        }
    }

    // Persist this together with the actor. A simulation owner must not reconstruct it from visuals.
    [Serializable]
    public sealed class NpcPerceptionMemory
    {
        public string candidate;
        public float exposureSeconds;
        public bool hasKnownPosition;
        public float knownX,knownY,remainingSeconds;
        public Vector2 KnownPosition=>new(knownX,knownY);
        public NpcPerceptionMemory Copy()=>(NpcPerceptionMemory)MemberwiseClone();
    }

    // Independent perception policy. ActorState.target remains the sole confirmed target ID.
    // Sampling cadence and authoritative ownership belong to the caller, not this module.
    public sealed class NpcPerception
    {
        public static void CopyState(ActorState source,ActorState destination)
        {
            if(source.player) return;
            if(source.perception==null) throw new InvalidOperationException($"Отсутствует память восприятия: actor={source.id}, type={source.actorType}.");
            destination.target=source.target;destination.perception=source.perception.Copy();
        }
        private readonly float sightSquared,closeSquared,hearingSquared,recognitionSeconds,memorySeconds,eyeHeight,targetHeight;
        public NpcPerception(NpcPerceptionSettings settings)
        {
            if(settings==null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            sightSquared=settings.sightDistance*settings.sightDistance;closeSquared=settings.closeDistance*settings.closeDistance;
            hearingSquared=settings.hearingDistance*settings.hearingDistance;recognitionSeconds=settings.recognitionSeconds;
            memorySeconds=settings.memorySeconds;eyeHeight=settings.eyeHeight;targetHeight=settings.targetHeight;
        }
        // brightness: how lit a subject stands, 0–1 (NpcDarkVision); null: full sight.
        public bool CanSee(ActorState observer,ActorState subject,Func<Vector2,Vector2,bool> clearSight,Func<ActorState,float> brightness=null)
        {
            if(!Eligible(observer,subject)) return false;
            var start=observer.BodyPosition+Vector2.up*eyeHeight;
            var end=subject.BodyPosition+Vector2.up*targetHeight;
            var delta=end-start;float distance=delta.sqrMagnitude;
            if(distance>sightSquared || delta.x*observer.facing<0 && distance>closeSquared) return false;
            if(brightness!=null && distance>closeSquared)
            {
                float share=NpcDarkVision.Share(brightness(subject),NpcDarkVision.Carries(subject));
                if(distance>sightSquared*share*share) return false;
            }
            return clearSight(start,end);
        }
        public void Step(ActorState observer,NpcPerceptionMemory memory,IReadOnlyList<ActorState> subjects,
            Func<Vector2,Vector2,bool> clearSight,float seconds,Func<ActorState,float> brightness=null)
        {
            if(!float.IsFinite(seconds) || seconds<0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if(seconds==0) return;
            memory.remainingSeconds=Math.Max(0,memory.remainingSeconds-seconds);
            if(memory.remainingSeconds==0) memory.hasKnownPosition=false;
            ActorState nearest=null,confirmed=null;float nearestDistance=float.PositiveInfinity;
            foreach(var subject in subjects)
            {
                if(!CanSee(observer,subject,clearSight,brightness)) continue;
                if(subject.id==observer.target) confirmed=subject;
                float distance=(subject.Position-observer.Position).sqrMagnitude;
                if(distance<nearestDistance || distance==nearestDistance && string.CompareOrdinal(subject.id,nearest?.id)<0)
                {nearest=subject;nearestDistance=distance;}
            }
            if(memory.candidate!=nearest?.id) {memory.candidate=nearest?.id;memory.exposureSeconds=0;}
            if(nearest!=null)
            {
                memory.exposureSeconds=Math.Min(recognitionSeconds,memory.exposureSeconds+seconds);
                if(memory.exposureSeconds>=recognitionSeconds) confirmed=nearest;
            }
            observer.target=confirmed?.id;
            if(confirmed!=null) Remember(memory,confirmed.Position);
        }
        public bool Hear(ActorState observer,NpcPerceptionMemory memory,string level,Vector2 perceivedPosition)
        {
            if(observer.down || observer.health<=0 || observer.level!=level || hearingSquared<=0 ||
                !float.IsFinite(perceivedPosition.x) || !float.IsFinite(perceivedPosition.y) ||
                (perceivedPosition-observer.Position).sqrMagnitude>hearingSquared || !string.IsNullOrEmpty(observer.target)) return false;
            // Sound supplies a place to investigate, never an identity or current hidden-player coordinates.
            // True only for news: a louder fight just moves the point, as the countdown changes without a publication.
            bool news=!memory.hasKnownPosition;
            Remember(memory,perceivedPosition);return news;
        }
        // UnitTurret.alarma: for this one look the side it faces does not matter; unseen, it still knows where to turn.
        public bool Alarm(ActorState observer,NpcPerceptionMemory memory,ActorState subject,Func<Vector2,Vector2,bool> clearSight)
        {
            if(!Eligible(observer,subject)) return false;
            Remember(memory,subject.Position);
            var start=observer.BodyPosition+Vector2.up*eyeHeight;
            var end=subject.BodyPosition+Vector2.up*targetHeight;
            if((end-start).sqrMagnitude>sightSquared || !clearSight(start,end)) return false;
            observer.target=subject.id;return true;
        }
        private void Remember(NpcPerceptionMemory memory,Vector2 position)
        {
            memory.hasKnownPosition=true;memory.knownX=position.x;memory.knownY=position.y;memory.remainingSeconds=memorySeconds;
        }
        private static bool Eligible(ActorState observer,ActorState subject)=>
            observer!=null && !observer.down && observer.health>0 && subject!=null && subject.id!=observer.id &&
            subject.player && subject.connected && !subject.down && subject.health>0 && subject.level==observer.level;
    }
}
