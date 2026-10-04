using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    public interface ITelekinesisRule
    {
        string Rejection(ActorState actor,PropState prop,bool alreadyHeld);
        Vector2 Target(ActorState actor,Vector2 cursor);
        float ManaPerSecond(ActorState actor,PropState prop);
    }
    // Optional extension preserves existing code-mod implementations of ITelekinesisRule.
    public interface ITelekinesisSightRule
    {
        string SightRejection(ActorState actor,PropState prop,RoomLayout room);
    }
    // Optional: code mods retaining their own resource economy need not opt in.
    public interface ITelekinesisRecoveryRule
    {
        float RecoveryAcceleration(ActorState actor);
    }
    // Mass is in kilograms, distance in Unity metres, time in seconds.
    public sealed class MassLimitedTelekinesisRule : ITelekinesisRule,ITelekinesisSightRule,ITelekinesisRecoveryRule,IActorTelekinesisRule
    {
        private readonly TelekinesisSettings settings;
        public MassLimitedTelekinesisRule(TelekinesisSettings settings)
        {this.settings=settings??throw new ArgumentNullException(nameof(settings));settings.Validate();}
        public string Rejection(ActorState actor,PropState prop,bool alreadyHeld)
            =>Rejection(actor,new TelekinesisTarget(prop),alreadyHeld);
        public string Rejection(ActorState actor,TelekinesisTarget prop,bool alreadyHeld)
        {
            if(!float.IsFinite(prop.Mass) || prop.Mass<0) return $"Некорректная масса объекта {prop.Id}: {prop.Mass}.";
            if(!prop.Movable) return "Этот объект закреплён.";
            if(prop.Mass>settings.maximumMass) return $"Слишком тяжело: {prop.Mass:0.#} кг. Предел подъёма — {settings.maximumMass:0.#} кг.";
            if(actor.down) return "Нельзя удерживать объект без сознания.";
            if(actor.mana<=0) return "Не хватает маны для телекинеза.";
            if(!alreadyHeld && actor.mana<settings.minimumGrabMana)
                return $"Для захвата нужно не менее {settings.minimumGrabMana:0.#} маны.";
            if(prop.Level!=actor.level) return "Объект находится в другой локации.";
            if(Vector2.Distance(actor.Position,prop.Position)>(alreadyHeld?settings.releaseRange:settings.range))
                return "Объект вне досягаемости телекинеза.";
            return null;
        }
        public string SightRejection(ActorState actor,PropState prop,RoomLayout room)
            =>SightRejection(actor,new TelekinesisTarget(prop),room);
        public string SightRejection(ActorState actor,TelekinesisTarget prop,RoomLayout room)
        {
            var origin=actor.Position+Vector2.up*(Anatomy.Movement(actor).height*.75f);
            return ShotObstacles.FirstContact(room,origin,prop.Center).HasValue?
                "Объект не виден: между вами препятствие.":null;
        }
        public Vector2 Target(ActorState actor,Vector2 cursor)
            =>actor.Position+Vector2.ClampMagnitude(cursor-actor.Position,settings.range);
        public float ManaPerSecond(ActorState actor,PropState prop)
            =>ManaPerSecond(actor,new TelekinesisTarget(prop));
        public float ManaPerSecond(ActorState actor,TelekinesisTarget prop)
            =>prop.Mass<=settings.freeMass?0:settings.manaPerSecondPerRootKg*Mathf.Sqrt(prop.Mass);
        public float RecoveryAcceleration(ActorState actor)=>settings.recoveryAcceleration;
    }
}
