using System;
using UnityEngine;

namespace Remains.Domain
{
    public enum InteractionKind { None, Revival, Container, Door }
    public readonly struct InteractionTarget
    {
        public readonly InteractionKind kind;
        public readonly string id;
        public InteractionTarget(InteractionKind kind,string id) {this.kind=kind;this.id=id;}
    }
    public static class InteractionSelection
    {
        public static InteractionTarget Select(WorldState world,ActorState actor,GameplayRuleSet rules)
        {
            if(world==null || actor==null || actor.down || !actor.connected) return default;
            // Holding E must not open a window or door when an ally can be revived.
            foreach(var other in world.actors)
                if(HealingRules.CanApply(actor,other,false)) return new(InteractionKind.Revival,other.id);
            var nearest=ContainerAccess.Nearest(world,actor,rules.Loot,rules.Deposit);
            var result=nearest==null?default:new InteractionTarget(InteractionKind.Container,nearest.id);
            float distance=nearest==null?float.PositiveInfinity:Vector2.Distance(actor.Position,nearest.Position);
            foreach(var door in world.doors)
            {
                if(rules.DoorAccess.Rejection(actor,door)!=null) continue;
                float next=door.DistanceTo(actor.Position);
                if(next>distance || next==distance && (result.kind!=InteractionKind.Door ||
                    StringComparer.Ordinal.Compare(door.id,result.id)>=0)) continue;
                distance=next;result=new(InteractionKind.Door,door.id);
            }
            return result;
        }
    }
}
