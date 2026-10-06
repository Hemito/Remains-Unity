using System;
using UnityEngine;

namespace Remains.Domain
{
    public enum InteractionKind { None, Revival, Container, Door, Map, Exit, Pickup, Mechanism }
    public readonly struct InteractionTarget
    {
        public readonly InteractionKind kind;
        public readonly string id;
        public readonly Rect bounds;
        public InteractionTarget(InteractionKind kind,string id) :this(kind,id,default) { }
        public InteractionTarget(InteractionKind kind,string id,Rect bounds) {this.kind=kind;this.id=id;this.bounds=bounds;}
    }
    public static class InteractionSelection
    {
        // Pointerless compatibility for tools/mods. Live input uses the cursor overload exclusively.
        public static InteractionTarget Select(WorldState world,ActorState actor,GameplayRuleSet rules,
            System.Collections.Generic.IReadOnlyList<TravelPoint> travelPoints=null)
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
                if(!door.CanInteract || rules.DoorAccess.Rejection(actor,door)!=null) continue;
                float next=door.DistanceTo(actor.Position);
                if(next>distance || next==distance && (result.kind!=InteractionKind.Door ||
                    StringComparer.Ordinal.Compare(door.id,result.id)>=0)) continue;
                distance=next;result=new(InteractionKind.Door,door.id);
            }
            if(travelPoints!=null) foreach(var point in travelPoints)
            {
                if(point.Rejection(actor)!=null) continue;
                float next=point.Distance(actor);
                if(next>distance || next==distance && StringComparer.Ordinal.Compare(point.id,result.id)>=0) continue;
                distance=next;result=new(point.kind==TravelPointKind.Map?InteractionKind.Map:InteractionKind.Exit,point.id);
            }
            return result;
        }

        public static InteractionTarget Select(WorldState world,ActorState actor,GameplayRuleSet rules,Vector2 cursor,
            System.Collections.Generic.IReadOnlyList<TravelPoint> travelPoints=null,
            System.Collections.Generic.IReadOnlyList<MechanismInteractionPoint> mechanismPoints=null)
        {
            if(world==null || actor==null || actor.down || !actor.connected || !float.IsFinite(cursor.x) || !float.IsFinite(cursor.y)) return default;
            foreach(var other in world.actors)
                if(HealingRules.CanApply(actor,other,false)) return new(InteractionKind.Revival,other.id,InventorySource.BodyBounds(other));
            InteractionTarget selected=default;float distance=float.PositiveInfinity,centerDistance=float.PositiveInfinity;
            foreach(var container in world.containers)
            {
                if(container.id.StartsWith(InventorySource.BodyPrefix,StringComparison.Ordinal))
                    throw new InvalidOperationException($"ID контейнера использует префикс инвентарей тел: {container.id}.");
                ConsiderSource(container);
            }
            foreach(var other in world.actors)
            {
                ConsiderSource(InventorySource.FromBody(other));
            }
            foreach(var door in world.doors)
                if(door.CanInteract && rules.DoorAccess.Rejection(actor,door)==null) Consider(InteractionKind.Door,door.id,door.InteractionBounds);
            if(travelPoints!=null) foreach(var point in travelPoints)
                if(point.Rejection(actor)==null) Consider(point.kind==TravelPointKind.Map?InteractionKind.Map:InteractionKind.Exit,point.id,point.Bounds);
            if(mechanismPoints!=null) foreach(var point in mechanismPoints)
                if(MechanismButtonAccess.Rejection(actor,point.Button,point.Radius)==null)
                    Consider(InteractionKind.Mechanism,point.Button.id,point.Bounds);
            return selected;

            void ConsiderSource(InventorySource source)
            {
                if(!ContainerAccess.Accessible(actor,source,rules.Loot,rules.Deposit)) return;
                Consider(source.IsLooseItem?InteractionKind.Pickup:InteractionKind.Container,source.id,source.Bounds);
            }
            void Consider(InteractionKind kind,string id,Rect bounds)
            {
                var closest=new Vector2(Mathf.Clamp(cursor.x,bounds.xMin,bounds.xMax),Mathf.Clamp(cursor.y,bounds.yMin,bounds.yMax));
                float next=(cursor-closest).sqrMagnitude,center=(cursor-bounds.center).sqrMagnitude;
                if(next>distance || next==distance && (center>centerDistance || center==centerDistance &&
                    ((int)kind>(int)selected.kind || kind==selected.kind && StringComparer.Ordinal.Compare(id,selected.id)>=0))) return;
                distance=next;centerDistance=center;selected=new InteractionTarget(kind,id,bounds);
            }
        }
    }
}
