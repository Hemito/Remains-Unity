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
    // Mass is in kilograms, distance in Unity metres, time in seconds.
    public sealed class MassLimitedTelekinesisRule : ITelekinesisRule
    {
        private readonly TelekinesisSettings settings;
        public MassLimitedTelekinesisRule(TelekinesisSettings settings)
        {this.settings=settings??throw new ArgumentNullException(nameof(settings));settings.Validate();}
        public string Rejection(ActorState actor,PropState prop,bool alreadyHeld)
        {
            if(!float.IsFinite(prop.mass) || prop.mass<0) return $"Некорректная масса объекта {prop.id}: {prop.mass}.";
            if(!prop.movable) return "Этот объект закреплён.";
            if(prop.mass>settings.maximumMass) return $"Слишком тяжело: {prop.mass:0.#} кг. Предел подъёма — {settings.maximumMass:0.#} кг.";
            if(actor.down) return "Нельзя удерживать объект без сознания.";
            if(actor.mana<=0) return "Не хватает маны для телекинеза.";
            if(prop.level!=actor.level) return "Объект находится в другой локации.";
            if(Vector2.Distance(actor.Position,prop.Position)>(alreadyHeld?settings.releaseRange:settings.range))
                return "Объект вне досягаемости телекинеза.";
            return null;
        }
        public Vector2 Target(ActorState actor,Vector2 cursor)
            =>actor.Position+Vector2.ClampMagnitude(cursor-actor.Position,settings.range);
        public float ManaPerSecond(ActorState actor,PropState prop)
            =>prop.mass<=settings.freeMass?0:settings.manaPerSecondPerRootKg*Mathf.Sqrt(prop.mass);
    }
}
