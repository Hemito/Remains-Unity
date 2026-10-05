using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // One live pointer target shared by hints, highlighting and the E action.
    public sealed class InteractionFocus
    {
        public InteractionTarget Target {get;private set;}
        private Vector2? cursor;
        public void Step(WorldState world,ActorState actor,GameplayRuleSet rules,Vector2? pointerWorld,bool blocked,IReadOnlyList<TravelPoint> points,
            IReadOnlyList<MechanismInteractionPoint> mechanisms=null)
        {
            cursor=pointerWorld;
            Target=blocked || cursor==null?default:InteractionSelection.Select(world,actor,rules,cursor.Value,points,mechanisms);
        }
        public void Clear() {cursor=null;Target=default;}
    }
}
