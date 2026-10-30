using System;
using UnityEngine;

namespace Remains.Domain
{
    // A player's radiation (UnitPlayer.rad): it accumulates from the doses around the player and caps the health —
    // health is never above maxhp − rad (UnitPlayer.actions, heal); reaching 0 it knocks the player down (the original
    // dies). The player's owner accumulates it, as it decides its own hits; the coordinator never changes it.
    //   dose    drad per frame, the sum of the sources this frame: a radioactive object `rad` at distance d within its
    //           radius radrad (250 px) gives rad·(radrad − d)/radrad (Obj.ggModum → raddamage);
    //   accrual rad += drad/30 per frame (radX 1: no resistance yet), i.e. the dose per second at 30 frames.
    // Revival keeps rad ≤ maxhp − 40 (UnitPlayer.resurect); a cure (heal type 2, item hrad) takes it away.
    public static class PlayerRadiation
    {
        public const float MaximumHealth=100;
        // UnitPlayer.resurect: rad is cut to maxhp − 40, so a revived player has at least 40 health of room.
        public const float RevivedLimit=MaximumHealth-40;
        // Obj.radrad default: 250 px.
        public const float SourceRadius=250/50f;

        // Health a player may have: maxhp − rad (no lower than 0).
        public static float HealthLimit(ActorState actor)=>actor.player?Mathf.Max(0,MaximumHealth-actor.radiation):MaximumHealth;
        // Health a revived player may have: its radiation is cut to RevivedLimit first.
        public static float RevivedHealthLimit(ActorState actor)=>MaximumHealth-Mathf.Min(actor.radiation,RevivedLimit);

        // Obj.ggModum: the dose per frame of a source of `strength` (Obj.radioactiv) at `distance` metres.
        public static float Dose(float strength,float distance,float radius=SourceRadius)
        {
            if(!float.IsFinite(strength) || strength<0 || !float.IsFinite(distance) || distance<0 || !float.IsFinite(radius) || radius<=0)
                throw new ArgumentOutOfRangeException(nameof(strength),$"Некорректный источник радиации: сила {strength}, расстояние {distance}, радиус {radius}.");
            return distance>=radius?0:strength*(radius-distance)/radius;
        }

        // rad += drad/30 per frame: a dose of `dosePerFrame` held for `seconds` adds dosePerFrame × seconds.
        // Returns whether the radiation changed.
        public static bool Accumulate(ActorState actor,float dosePerFrame,float seconds)
        {
            if(!float.IsFinite(dosePerFrame) || dosePerFrame<0 || !float.IsFinite(seconds) || seconds<0)
                throw new ArgumentOutOfRangeException(nameof(dosePerFrame),$"Некорректная доза радиации: actor={actor?.id}, доза {dosePerFrame}, время {seconds}.");
            if(actor==null || !actor.player || actor.down || dosePerFrame==0 || seconds==0) return false;
            float before=actor.radiation;
            actor.radiation=Mathf.Min(MaximumHealth,actor.radiation+dosePerFrame*seconds);
            return actor.radiation!=before;
        }

        // UnitPlayer.heal(n, 2): rad −= n, not below 0; a negative cure (Rad-Cola hrad −33) adds radiation.
        public static void Cure(ActorState actor,float amount)
        {
            if(!float.IsFinite(amount)) throw new ArgumentOutOfRangeException(nameof(amount),$"Некорректное лечение радиации: actor={actor?.id}, {amount}.");
            actor.radiation=Mathf.Clamp(actor.radiation-amount,0,MaximumHealth);
        }

        // After a revival (down → up): rad is cut to RevivedLimit. Returns whether it changed.
        public static bool LimitAfterRevival(ActorState actor)
        {
            if(actor==null || !actor.player || actor.down || actor.radiation<=RevivedLimit) return false;
            actor.radiation=RevivedLimit;return true;
        }
    }
}
