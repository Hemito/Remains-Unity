using System;
using Remains.Serialization;
using Newtonsoft.Json.Linq;

namespace Remains.Domain
{
    public enum MechanismOperationOutcome {Rejected,Unchanged,Changed,Duplicate}
    public readonly struct MechanismOperationResult
    {
        public MechanismOperationOutcome Outcome {get;}
        public string Reason {get;}
        public bool Changed=>Outcome==MechanismOperationOutcome.Changed;
        public MechanismOperationResult(MechanismOperationOutcome outcome,string reason=null) {Outcome=outcome;Reason=reason;}
    }

    // Mutates only canonical domain records. The caller coordinates pause, authentication,
    // persistence/recovery and publish-after-commit; Changed is not a durable acknowledgement.
    public sealed class MechanismOperations
    {
        private readonly WorldState world;
        private readonly IButtonRepairRule repairRule;
        private readonly Func<string,bool> supportedRepairState;
        public MechanismOperations(WorldState world,IButtonRepairRule repairRule=null,Func<string,bool> supportedRepairState=null)
        {
            MechanismPersistence.ValidateWorld(world,supportedRepairState:supportedRepairState);
            if(world.mechanismVersion!=1) throw new InvalidOperationException("Перед операциями механизмов требуется явная миграция мира "+world.worldId);
            this.world=world;this.repairRule=repairRule??new HoldButtonRepairRule();this.supportedRepairState=supportedRepairState??MechanismRepairStates.BuiltIn;
        }
        public MechanismOperationResult TryActivate(ActorState actor,MechanismButtonState button,MovingPlatformState platform,
            MovingPlatformDefinition type,double elapsed,long expectedRevision,float accessRadius,string operationId)
        {
            var duplicate=Transaction(operationId);if(duplicate.HasValue) return duplicate.Value;
            if(actor==null || !world.actors.Contains(actor)) return Rejected("Персонаж не является текущей записью мира.");
            if(!Canonical(button,platform)) return Rejected("Кнопка или платформа не являются текущими записями мира.");
            ValidatePlatform(platform,type);button.ValidateLink(platform,supportedRepairState);ValidateElapsed(elapsed);
            string reason=MechanismButtonAccess.Rejection(actor,button,accessRadius);
            if(reason!=null) return Rejected(reason);
            if(button.repairState!=MechanismRepairStates.Ready) return Rejected("Кнопка не исправна: требуется завершённый ремонт.");
            if(platform.removed) return Rejected("Платформа больше недоступна.");
            if(button.revision!=expectedRevision) return Rejected("Состояние кнопки уже изменилось. Повторите взаимодействие.");
            // An elapsed segment is still active until Complete explicitly commits its endpoint.
            if(platform.segment!=null) return Rejected("Платформа уже движется или ожидает фиксации прибытия.");
            CanIncrement(world.revision,"world="+world.worldId);CanIncrement(button.revision,"button="+button.id);CanIncrement(platform.revision,"platform="+platform.id);
            var segment=new MovingPlatformSegment {sequence=platform.revision+1,fromEndpoint=platform.endpoint,toEndpoint=1-platform.endpoint,
                startElapsedSeconds=elapsed,durationSeconds=type.durationSeconds};
            segment.Validate(platform.id);
            button.pressed=!button.pressed;button.revision++;
            platform.segment=segment;platform.revision++;
            Finish(operationId);return new MechanismOperationResult(MechanismOperationOutcome.Changed);
        }
        public MechanismOperationResult ApplyRepair(ActorState actor,MechanismButtonState button,ButtonRepairCandidate candidate,
            float accessRadius,string operationId)
        {
            var duplicate=Transaction(operationId);if(duplicate.HasValue) return duplicate.Value;
            if(actor==null || !world.actors.Contains(actor)) return Rejected("Персонаж не является текущей записью мира.");
            if(candidate==null || candidate.AttemptId!=operationId) return Rejected("Кандидат ремонта не соответствует ID операции.");
            if(button==null || !world.mechanismButtons.Contains(button)) return Rejected("Кнопка ремонта не является текущей записью мира.");
            button.Validate(supportedRepairState);
            button.ValidateLink(world.movingPlatforms.Find(p=>p.id==button.platformId),supportedRepairState);
            if(!candidate.MatchesActor(actor) || !candidate.MatchesTarget(button)) return Rejected("Персонаж или кнопка изменились после начала ремонта.");
            string access=MechanismButtonAccess.Rejection(actor,button,accessRadius);
            if(access!=null) return Rejected(access);
            string reason=repairRule.Rejection(actor,button,accessRadius);
            if(reason!=null) return Rejected(reason);
            CanIncrement(world.revision,"world="+world.worldId);CanIncrement(button.revision,"button="+button.id);
            button.repairState=candidate.RepairState;button.revision++;
            Finish(operationId);return new MechanismOperationResult(MechanismOperationOutcome.Changed);
        }
        public MechanismOperationResult Complete(MovingPlatformState platform,MovingPlatformDefinition type,double elapsed,long expectedSegmentSequence)
        {
            if(platform==null || !world.movingPlatforms.Contains(platform)) return Rejected("Платформа не является текущей записью мира.");
            ValidatePlatform(platform,type);ValidateElapsed(elapsed);
            if(platform.removed) return Rejected("Платформа больше недоступна.");
            var segment=platform.segment;
            if(segment==null) return new MechanismOperationResult(MechanismOperationOutcome.Unchanged);
            if(segment.sequence!=expectedSegmentSequence) return Rejected("Переход платформы изменился: завершение устарело.");
            if(elapsed<segment.EndElapsedSeconds) return new MechanismOperationResult(MechanismOperationOutcome.Unchanged);
            CanIncrement(world.revision,"world="+world.worldId);CanIncrement(platform.revision,"platform="+platform.id);
            string archiveKey="remains:mechanism:retired-segment:"+segment.sequence;
            JToken archive=null;
            if(segment.extensions?.Count>0)
            {
                if(platform.extensions?.ContainsKey(archiveKey)==true) throw new InvalidOperationException($"Архив перехода уже занят; неизвестные данные не заменены: platform={platform.id}, key={archiveKey}.");
                // Archive only unknown payload. Known transition fields are deliberately
                // retired, and must not become untyped numbers in an extension record.
                var preserved=StateJson.Copy(segment);
                var payload=new JObject();
                foreach(var field in preserved.extensions) payload.Add(field.Key,field.Value?.DeepClone()??JValue.CreateNull());
                archive=payload;
            }
            if(archive!=null)
            {
                platform.extensions??=new System.Collections.Generic.Dictionary<string,JToken>(StringComparer.Ordinal);
                platform.extensions.Add(archiveKey,archive);
            }
            platform.endpoint=segment.toEndpoint;platform.segment=null;platform.revision++;world.revision++;
            return new MechanismOperationResult(MechanismOperationOutcome.Changed);
        }
        private MechanismOperationResult? Transaction(string id)
        {
            if(string.IsNullOrWhiteSpace(id) || !id.StartsWith("mechanism:",StringComparison.Ordinal) || id.Length=="mechanism:".Length)
                return Rejected("Для операции механизма нужен ID mechanism:<уникальный суффикс>.");
            if(world.completedTransactions.Contains(id)) return new MechanismOperationResult(MechanismOperationOutcome.Duplicate,"Операция ранее завершена; повтор не применён.");
            return InventoryTransactions.Contains(world,id)?Rejected("ID операции занят незавершённой общей транзакцией."):null;
        }
        private bool Canonical(MechanismButtonState button,MovingPlatformState platform)=>button!=null && platform!=null &&
            world.mechanismButtons.Contains(button) && world.movingPlatforms.Contains(platform);
        private void Finish(string id) {world.completedTransactions.Add(id);world.revision++;}
        private static MechanismOperationResult Rejected(string reason)=>new(MechanismOperationOutcome.Rejected,reason);
        private static void ValidateElapsed(double elapsed)
        {if(!double.IsFinite(elapsed) || elapsed<0) throw new ArgumentOutOfRangeException(nameof(elapsed),"Время локации должно быть конечным неотрицательным числом.");}
        private static void CanIncrement(long revision,string context)
        {if(revision<0 || revision==long.MaxValue) throw new InvalidOperationException("Некорректный/исчерпанный счётчик механизма: "+context);}
        private static void ValidatePlatform(MovingPlatformState platform,MovingPlatformDefinition type)
        {
            platform.Validate(type);
            if(platform.segment!=null && platform.segment.sequence>platform.revision)
                throw new InvalidOperationException("Последовательность перехода выше revision платформы "+platform.id);
        }
    }
}
