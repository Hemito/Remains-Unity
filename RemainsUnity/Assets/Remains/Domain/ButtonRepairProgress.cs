using System;

namespace Remains.Domain
{
    // One local ephemeral attempt. It is deliberately absent from WorldState and saves.
    public sealed class ButtonRepairProgress
    {
        private readonly IButtonRepairRule rule;
        private ButtonRepairCandidate captured;
        private double elapsed,duration;
        private float radius;
        public bool Active=>captured!=null;
        public double ElapsedSeconds=>elapsed;
        public double Progress=>Active?elapsed/duration:0;
        public ButtonRepairProgress():this(new HoldButtonRepairRule()) {}
        public ButtonRepairProgress(IButtonRepairRule rule)
        {this.rule=rule??throw new ArgumentNullException(nameof(rule));}

        public ButtonRepairResult Begin(string attemptId,ActorState actor,MechanismButtonState button,double durationSeconds,float accessRadius)
        {
            if(Active) throw new InvalidOperationException("Попытка ремонта уже идёт; сначала явно отмените её.");
            if(string.IsNullOrWhiteSpace(attemptId)) throw new ArgumentException("Не задан ID попытки ремонта.",nameof(attemptId));
            if(!double.IsFinite(durationSeconds) || durationSeconds<=0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds),"Длительность ремонта должна быть конечным положительным числом.");
            if(!float.IsFinite(accessRadius) || accessRadius<=0)
                throw new ArgumentOutOfRangeException(nameof(accessRadius),"Радиус ремонта должен быть конечным положительным числом.");
            ValidateTarget(button);
            if(actor!=null && string.IsNullOrWhiteSpace(actor.id)) throw new InvalidOperationException("Не задан ID ремонтирующего персонажа.");
            string reason=rule.Rejection(actor,button,accessRadius);
            if(reason!=null) return new ButtonRepairResult(ButtonRepairOutcome.Rejected,reason,0,durationSeconds);
            captured=new ButtonRepairCandidate(attemptId,actor,button);
            elapsed=0;duration=durationSeconds;radius=accessRadius;
            return Current(ButtonRepairOutcome.InProgress);
        }

        // Delta is elapsed simulation time of this level, not wall or network time.
        // Cancellation is checked even when the level is paused or delta is zero.
        public ButtonRepairResult Step(ActorState actor,MechanismButtonState button,bool held,bool levelPaused,double simulationDeltaSeconds)
        {
            if(!double.IsFinite(simulationDeltaSeconds) || simulationDeltaSeconds<0)
                throw new ArgumentOutOfRangeException(nameof(simulationDeltaSeconds),"Шаг времени ремонта должен быть конечным неотрицательным числом.");
            if(!Active) return new ButtonRepairResult(ButtonRepairOutcome.Idle,null,0,0);
            if(!held) return Cancel("Ремонт отменён: клавиша E отпущена.");
            if(!captured.MatchesActor(actor)) return Cancel("Ремонт отменён: персонаж, его локация, жизненный цикл или счётчик повреждений изменились.");
            if(!captured.MatchesTarget(button)) return Cancel("Ремонт отменён: выбранная кнопка или её сохранённое состояние изменились.");
            ValidateTarget(button);
            string reason=rule.Rejection(actor,button,radius);
            if(reason!=null) return Cancel(reason);
            if(!levelPaused)
            {
                // Comparing with remaining time avoids overflow on a large valid delta.
                double remaining=duration-elapsed;
                elapsed=simulationDeltaSeconds>=remaining?duration:elapsed+simulationDeltaSeconds;
            }
            if(elapsed<duration) return Current(ButtonRepairOutcome.InProgress);
            var result=Current(ButtonRepairOutcome.Completed,candidate:captured);
            Clear(); // Duplicate Step calls cannot emit a second completion.
            return result;
        }

        public ButtonRepairResult Cancel(string reason)
        {
            if(string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Не задана причина отмены ремонта.",nameof(reason));
            if(!Active) return new ButtonRepairResult(ButtonRepairOutcome.Idle,null,0,0);
            var result=Current(ButtonRepairOutcome.Cancelled,reason);
            Clear();return result;
        }
        private ButtonRepairResult Current(ButtonRepairOutcome outcome,string reason=null,ButtonRepairCandidate candidate=null)
            =>new(outcome,reason,elapsed,duration,candidate);
        private void Clear() {captured=null;elapsed=0;duration=0;radius=0;}
        private static void ValidateTarget(MechanismButtonState button)
        {
            // Shared placement validation; the selected rule decides which state IDs it supports.
            button?.Validate(_=>true);
        }
    }
}
