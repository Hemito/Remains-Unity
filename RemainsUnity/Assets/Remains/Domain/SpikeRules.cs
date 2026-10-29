using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Remains.Content;

namespace Remains.Domain
{
    // Spikes as the original loc/Trap.as (step → attKorp), every frame of a simulated level for each unit that springs
    // traps at all (activateTrap ≠ 0: small creatures too) and does not fly:
    //   fall: the unit falls faster than 8 px per frame with its feet in the box — massa·dy/20·damage·(1+0.1·locDif);
    //   rise: the unit moves up with its head in the box — massa·damage·(1+0.1·locDif).
    // massa is the original's unit mass (AllData phis massa / 50 = bodyMassKg / 50). A hit starts the unit's contact
    // cooldown (neujaz = neujazMax), the one box contacts share (PropImpactCooldown); while it lasts spikes do not hurt.
    // Not ported: the support's own velocity in the rise test (osndy: rising on a platform), the difficulty-dependent
    // player cooldown (Pers: 20 and 15 frames on difficulties 3 and 4), spikes falling with their destroyed support tile.
    public static class SpikeRules
    {
        private const float FramesPerSecond=30,PixelsPerMetre=50,OriginalMassUnit=50;
        public const float FallSpeed=8*FramesPerSecond/PixelsPerMetre;   // 4.8 m/s
        // Unit.neujazMax: the player 30 frames (Pers), every other unit 20.
        public const float PlayerCooldown=30/FramesPerSecond,UnitCooldown=20/FramesPerSecond;

        public static float Cooldown(ActorState actor)=>actor.player?PlayerCooldown:UnitCooldown;

        // Whether spikes can hurt this actor at all (Trap.step: activateTrap, not removed; attKorp: not flying).
        public static bool Affects(ActorState actor)
        {
            if(actor==null || actor.health<=0 || actor.down || !ActorPresence.IsPresent(actor)) return false;
            var type=ActorTypes.Get(actor.actorType);
            return type.activateTrap>0 && !NpcCreatureBehaviours.Flies(type.behavior);
        }

        // The damage before protection when this actor is hit by the spike now, otherwise 0.
        public static float Damage(RoomSpike spike,SpikeDefinition definition,ActorState actor)
        {
            if(actor.x<spike.x || actor.x>spike.x+spike.width) return 0;
            float massa=ActorTypes.Get(actor.actorType).bodyMassKg/OriginalMassUnit,difficulty=1+.1f*spike.danger;
            if(definition.attack==SpikeDefinition.Fall)
            {
                if(actor.vy>=-FallSpeed || actor.y<spike.y || actor.y>spike.y+spike.height) return 0;
                float dy=-actor.vy*PixelsPerMetre/FramesPerSecond;
                return massa*dy/20*definition.damage*difficulty;
            }
            float head=actor.y+Anatomy.Movement(actor).height;
            if(actor.vy<=0 || head<spike.y || head>spike.y+spike.height) return 0;
            return massa*definition.damage*difficulty;
        }

        // The level's spikes sorted by their left edge, built once per layout: a step looks only at the spikes whose
        // columns contain the actor, not at every spike of the level.
        private static readonly ConditionalWeakTable<RoomLayout,Index> indices=new();
        private sealed class Index
        {
            public RoomSpike[] spikes;public float widest;
        }
        public static void Near(RoomLayout room,float x,List<RoomSpike> result)
        {
            result.Clear();
            if(room.spikes==null || room.spikes.Length==0) return;
            var index=indices.GetValue(room,Build);
            // First spike that may contain x: left edge ≥ x − widest.
            int low=0,high=index.spikes.Length;float from=x-index.widest;
            while(low<high) {int mid=(low+high)/2;if(index.spikes[mid].x<from) low=mid+1;else high=mid;}
            for(int i=low;i<index.spikes.Length && index.spikes[i].x<=x;i++)
                if(x<=index.spikes[i].x+index.spikes[i].width) result.Add(index.spikes[i]);
        }
        private static Index Build(RoomLayout room)
        {
            var sorted=(RoomSpike[])room.spikes.Clone();
            Array.Sort(sorted,(a,b)=>a.x.CompareTo(b.x));
            float widest=0;
            foreach(var spike in sorted)
            {
                if(spike==null || string.IsNullOrWhiteSpace(spike.id) || !float.IsFinite(spike.x) || !float.IsFinite(spike.y) ||
                    !(spike.width>0) || !(spike.height>0) || !float.IsFinite(spike.danger) || spike.danger<0)
                    throw new InvalidOperationException($"Некорректные шипы в геометрии {room.id}: {spike?.id}.");
                SpikeCatalog.Get(spike.definition);
                widest=Math.Max(widest,spike.width);
            }
            return new Index {spikes=sorted,widest=widest};
        }
    }
}
