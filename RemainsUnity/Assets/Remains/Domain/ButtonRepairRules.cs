using System;

namespace Remains.Domain
{
    // Eligibility is replaceable; duration and radius are explicit caller-owned data.
    // Implementations inspect the inputs without mutating actor or button state.
    public interface IButtonRepairRule
    {
        string Rejection(ActorState actor,MechanismButtonState button,float accessRadius);
    }

    public sealed class HoldButtonRepairRule : IButtonRepairRule
    {
        public string Rejection(ActorState actor,MechanismButtonState button,float accessRadius)
        {
            if(actor==null) return "Ремонт недоступен: персонаж отсутствует.";
            if(button==null || button.removed) return "Кнопка больше недоступна.";
            if(!actor.connected) return "Ремонт отменён: персонаж отключён.";
            if(actor.down || actor.health<=0) return "Нельзя ремонтировать кнопку без сознания.";
            if(actor.level!=button.level) return "Кнопка находится в другой локации.";
            if(button.repairState==MechanismRepairStates.Ready) return "Кнопка уже исправна.";
            if(button.repairState==MechanismRepairStates.Broken) return "Для сломанной кнопки правило ремонта пока не задано.";
            if(button.repairState!=MechanismRepairStates.NeedsRepair)
                return $"Состояние ремонта '{button.repairState}' не поддерживается правилом удержания E: button={button.id}.";
            if(!float.IsFinite(actor.x) || !float.IsFinite(actor.y) || !float.IsFinite(actor.health))
                return $"Ремонт недоступен: неверные координаты или здоровье персонажа {actor.id}.";
            if(!float.IsFinite(accessRadius) || accessRadius<=0)
                throw new ArgumentOutOfRangeException(nameof(accessRadius),"Радиус ремонта должен быть конечным положительным числом.");
            double dx=(double)actor.x-button.x,dy=(double)actor.y-button.y;
            return dx*dx+dy*dy>(double)accessRadius*accessRadius?"Кнопка вне досягаемости ремонта.":null;
        }
    }

    public enum ButtonRepairOutcome {Idle,InProgress,Completed,Cancelled,Rejected}

    // A proposal for the coordinator, never proof that persistent repair was committed.
    // The attempt ID is supplied once by the caller, not generated every simulation sample.
    public sealed class ButtonRepairCandidate
    {
        public string AttemptId {get;}
        public string ActorId {get;}
        public int ActorGeneration {get;}
        public int ActorLifecycle {get;}
        public int DamageSequence {get;}
        public string ButtonId {get;}
        public string Definition {get;}
        public string Level {get;}
        public string RoomId {get;}
        public string GroupId {get;}
        public string PlatformId {get;}
        public long ExpectedRevision {get;}
        public string ExpectedRepairState {get;}
        public bool ExpectedPressed {get;}
        public float ButtonX {get;}
        public float ButtonY {get;}
        public string RepairState=>MechanismRepairStates.Ready;
        internal ButtonRepairCandidate(string attemptId,ActorState actor,MechanismButtonState button)
        {
            AttemptId=attemptId;ActorId=actor.id;ActorGeneration=actor.generation;ActorLifecycle=actor.lifecycleSequence;
            DamageSequence=actor.damageSequence;ButtonId=button.id;Definition=button.definition;Level=button.level;
            RoomId=button.roomId;GroupId=button.groupId;PlatformId=button.platformId;ExpectedRevision=button.revision;
            ExpectedRepairState=button.repairState;ExpectedPressed=button.pressed;ButtonX=button.x;ButtonY=button.y;
        }
        public bool MatchesActor(ActorState actor)=>actor!=null && actor.id==ActorId && actor.generation==ActorGeneration &&
            actor.lifecycleSequence==ActorLifecycle && actor.damageSequence==DamageSequence && actor.level==Level;
        public bool MatchesTarget(MechanismButtonState button)=>button!=null && !button.removed && button.id==ButtonId &&
            button.definition==Definition && button.level==Level && button.roomId==RoomId && button.groupId==GroupId &&
            button.platformId==PlatformId && button.revision==ExpectedRevision && button.repairState==ExpectedRepairState && button.pressed==ExpectedPressed &&
            button.x==ButtonX && button.y==ButtonY;
    }

    public readonly struct ButtonRepairResult
    {
        public ButtonRepairOutcome Outcome {get;}
        public string Reason {get;}
        public ButtonRepairCandidate Candidate {get;}
        public double ElapsedSeconds {get;}
        public double DurationSeconds {get;}
        public double Progress=>DurationSeconds>0?ElapsedSeconds/DurationSeconds:0;
        internal ButtonRepairResult(ButtonRepairOutcome outcome,string reason,double elapsed,double duration,ButtonRepairCandidate candidate=null)
        {Outcome=outcome;Reason=reason;ElapsedSeconds=elapsed;DurationSeconds=duration;Candidate=candidate;}
    }
}
