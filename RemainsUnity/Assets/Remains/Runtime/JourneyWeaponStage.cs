using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Commands and observations for the three pre-knockdown weapon stages.
    public sealed class JourneyWeaponStage
    {
        private readonly PrototypeRuntime runtime;
        private int firedPhase=-1;
        private float spearHealth;
        public float SpearDamage=>Verified>=2?86-spearHealth:0;
        public int Verified {get;private set;}
        public JourneyWeaponStage(PrototypeRuntime runtime) {this.runtime=runtime;}
        public PlayerCommand Command(int phase)
        {
            if(runtime.Options.Role!="host" || phase<12 || phase>14 || firedPhase==phase) return default;
            firedPhase=phase;
            var victim=runtime.Session.World.Find("player:session-client-1");
            return new PlayerCommand {weapon=phase==12?"crowbar":phase==13?"spear":"energy",
                fire=true,hasAim=true,aim=victim.Position+Vector2.up*.55f};
        }
        public void Verify(int phase)
        {
            var victim=runtime.Session.World.Find("player:session-client-1");
            var host=runtime.Session.World.Find("player:session-host");
            float expected=phase==12?86:phase==13?victim.health:spearHealth-8;
            if(Mathf.Abs(victim.health-expected)>.001f || victim.damageSequence!=phase-11 ||
                phase==13 && (victim.health<10 || victim.health>80))
                throw new InvalidOperationException($"Journey: неверный результат оружия phase={phase}, hp={victim.health}/{expected}, damageSequence={victim.damageSequence}.");
            if(phase==13) spearHealth=victim.health;
            foreach(var actor in runtime.Session.World.actors)
                if(actor.player && (actor.mana!=100 || actor.id!=victim.id && actor.health!=100))
                    throw new InvalidOperationException($"Journey: оружие изменило ману или здоровье наблюдателя {actor.id}.");
            int expectedRounds=phase==14?14:15;
            if(ProbeWeaponSelection.Magazine(host,"energy").rounds!=expectedRounds || InventoryJournal.Count(host,"batt")!=45)
                throw new InvalidOperationException("Journey: неверный расход батарей лазерного пистолета.");
            Verified++;
        }
    }
}
