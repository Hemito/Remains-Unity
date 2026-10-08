using System;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using Remains.Serialization;

namespace Remains.Runtime
{
    public enum FactoryContinuationObservationStatus {Pending,Ready,Rejected}

    [Serializable] public sealed class FactoryContinuationRouteEvidence
    {
        public int schema=1,driverGeneration,driverLifecycle;
        public int stairFrames;
        public float maxStairFrameSeconds;
        public string actor,observer,world;
        public bool motionVerified,destructionVerified,lootVerified,returned,geometryBlocked;
        public bool middleClimbUp,middleClimbDown,rightAirborne,rightLanding,leftAirborne,leftLanding,
            ascendingSlopes,descendingSlopes,outwardSeam,returnSeam;
        public FactoryRouteMotionWitness[] motion;
        public DoorState initialDoor,destroyedDoor;
        public AttackEvent[] attacks;
        public DoorDamageRequestData[] contacts;
        public DoorDamageReply[] replies;
        public WarehouseLowerLootEvidence loot;
        public ContainerAppearanceState appearance;
    }

    public static class FactoryContinuationRouteProof
    {
        public const string CupId="factory:0:original:plant:27:BNhmeNonzjYCRmpA";
        public static FactoryContinuationRouteEvidence Snapshot(FactoryContinuationRouteEvidence source)
        {
            if(source==null) return null;
            var result=StateJson.Copy(source);
            // StateJson serializes public readonly fields but does not restore them.
            // These immutable value witnesses are safe to copy into an independent array.
            result.motion=source.motion==null?null:(FactoryRouteMotionWitness[])source.motion.Clone();
            return result;
        }
        // A reliable debug barrier can precede the normal unreliable pose sample.
        // Observe the canonical sequence fence; never manufacture a stopped pose.
        public static FactoryContinuationObservationStatus EvaluateObservation(ActorState canonical,string actorId,
            int generation,int lifecycle,int phase,uint motionSequence,RoomLayout room,out string reason)
        {
            string context=canonical==null?$"requestedActor={actorId}, requestedEpoch={generation}/{lifecycle}, phase={phase}, requiredSequence={motionSequence}, canonical=null":
                FormattableString.Invariant($"requestedActor={actorId}, actor={canonical.id}, epoch={canonical.generation}/{canonical.lifecycleSequence}, requestedEpoch={generation}/{lifecycle}, level={canonical.level}, phase={phase}, sequence={canonical.motionSequence}, requiredSequence={motionSequence}, revision={canonical.revision}, x={canonical.x:R}, y={canonical.y:R}, vx={canonical.vx:R}, vy={canonical.vy:R}, climbing={canonical.climbing}, room={room?.id}");
            if(canonical==null || string.IsNullOrEmpty(actorId) || canonical.id!=actorId || canonical.generation!=generation ||
                canonical.lifecycleSequence!=lifecycle || canonical.level!="factory:0" || phase is not (0 or 2) || room?.platforms==null)
            {
                reason="Комната 27: барьер получил другую личность/эпоху/локацию, неизвестный этап или отсутствующий источник геометрии: "+context;
                return FactoryContinuationObservationStatus.Rejected;
            }
            if(ActorMotionReplication.IsNewer(motionSequence,canonical.motionSequence))
            {
                reason="Комната 27: каноническая поза ещё не достигла наблюдаемой последовательности: "+context;
                return FactoryContinuationObservationStatus.Pending;
            }
            float x=phase==0?163.2f:125.38f,y=phase==0?4:.8f;
            bool position=float.IsFinite(canonical.x) && float.IsFinite(canonical.y) &&
                Math.Abs(canonical.x-x)<=.12f && Math.Abs(canonical.y-y)<=.0002f;
            bool velocity=float.IsFinite(canonical.vx) && float.IsFinite(canonical.vy) &&
                Math.Abs(canonical.vx)<=.1f && Math.Abs(canonical.vy)<=.001f;
            bool supported=ActorTerrainMotion.Supported(room,Anatomy.Movement(canonical),canonical.Position);
            if(position && velocity && !canonical.climbing && supported)
            {reason=null;return FactoryContinuationObservationStatus.Ready;}
            reason=FormattableString.Invariant($"Комната 27: достигнутый барьер не совпал с настоящим покоем/опорой: position={position}, velocity={velocity}, supported={supported}, notClimbing={!canonical.climbing}, expectedX={x:R}, expectedY={y:R}; ")+context;
            return FactoryContinuationObservationStatus.Rejected;
        }
        public static void ValidateCup(ContainerState cup)
        {
            if(cup==null || cup.id!=CupId || cup.definition!="cup" || cup.level!="factory:0" ||
                !cup.hasPhysicalBody || Math.Abs(cup.width-.94f)>.0001f || Math.Abs(cup.height-1.6f)>.0001f ||
                Math.Abs(cup.mass-60.16f)>.001f || cup.anchorHeight!=0 || cup.items.Count!=3 ||
                !cup.items.OrderBy(i=>i.definition,StringComparer.Ordinal).Select(i=>i.definition+":"+i.count)
                    .SequenceEqual(new[]{"caps:4","kombu_comp:1","pin:1"}))
                throw new InvalidOperationException("Комната 27: установленный cup28 или три исходные стопки не совпали; содержимое не подменяется.");
        }
        public static bool Destruction(FactoryContinuationRouteEvidence evidence,out string reason)
        {
            reason=null;
            if(evidence==null || evidence.schema!=1 || string.IsNullOrEmpty(evidence.world) || string.IsNullOrEmpty(evidence.actor) ||
                evidence.initialDoor==null || evidence.destroyedDoor==null || evidence.attacks==null || evidence.contacts==null || evidence.replies==null)
                return Reject("Нет исходного объекта и настоящих событий разрушения.",out reason);
            var before=evidence.initialDoor;var after=evidence.destroyedDoor;
            try {before.Validate();after.Validate();} catch(Exception failure) {return Reject(failure.Message,out reason);}
            if(before.id!=FactoryContinuationRouteMotion.SeptumId || before.level!="factory:0" || before.definition!="office-septum" ||
                before.CanInteract || !before.Blocks || before.open || before.integrity==null || before.integrity.maxHealth!=30 ||
                before.integrity.health!=30 || before.integrity.resolvedAttacks.Count!=0 || before.revision!=0 ||
                !after.Destroyed || after.Blocks || after.open || after.CanInteract || after.integrity.health!=0 ||
                after.integrity.resolvedAttacks.Count==0 || after.revision!=after.integrity.resolvedAttacks.Count)
                return Reject("Перегородка не прошла точное исходное HP30 → разрушение без открытия.",out reason);
            var shell=StateJson.Copy(after);shell.integrity=StateJson.Copy(before.integrity);shell.revision=before.revision;
            if(StateJson.ToJson(shell)!=StateJson.ToJson(before)) return Reject("Изменились поля перегородки вне целостности и ревизии.",out reason);
            var ids=after.integrity.resolvedAttacks;
            if(evidence.contacts.Length!=ids.Count || evidence.replies.Length!=ids.Count || evidence.attacks.Select(a=>a.id).Distinct().Count()!=evidence.attacks.Length ||
                evidence.contacts.Select(a=>a.attack?.id).Distinct().Count()!=ids.Count || evidence.replies.Select(a=>a.attack).Distinct().Count()!=ids.Count)
                return Reject("Неоднозначная связь контактов/ответов с attack receipts перегородки.",out reason);
            for(int i=0;i<ids.Count;i++)
            {
                var id=ids[i];var attack=evidence.attacks.SingleOrDefault(a=>a.id==id);
                var contact=evidence.contacts.SingleOrDefault(a=>a.attack?.id==id);var reply=evidence.replies.SingleOrDefault(a=>a.attack==id);
                if(attack==null || contact==null || attack.attacker!=evidence.actor || !attack.sourcePlayer || attack.level!=before.level ||
                    contact.world!=evidence.world || contact.door!=before.id || contact.decisionActor!=evidence.actor ||
                    contact.decisionGeneration!=evidence.driverGeneration || contact.decisionLifecycle!=evidence.driverLifecycle ||
                    contact.sourceGeneration!=evidence.driverGeneration || contact.sourceLifecycle!=evidence.driverLifecycle ||
                    contact.attack.attacker!=attack.attacker || contact.attack.weapon!=attack.weapon || contact.attack.level!=attack.level ||
                    contact.attack.sourceOwner!=attack.sourceOwner || !reply.accepted || !reply.changed || reply.door!=before.id ||
                    reply.revision!=i+1 || reply.health<0 || reply.health>=30 || reply.destroyed!=(i==ids.Count-1) ||
                    i==ids.Count-1 && reply.health!=0)
                    return Reject("Receipt не связан с реальной атакой/контактом/эпохой/ответом игрока: "+id,out reason);
            }
            return true;
        }
        private static bool Reject(string value,out string reason) {reason=value;return false;}
    }
}
