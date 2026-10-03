using System;
using Remains.Content;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    // The firing owner selects the trajectory once. Peers validate and retain that trajectory.
    public static class ProjectileLaunch
    {
        public static void Prepare(AttackEvent attack,WeaponDefinition definition,float speed)
        {
            attack.speed=speed;
            var end=attack.Origin+(attack.End-attack.Origin).normalized*definition.FlightRange(speed);
            attack.endX=end.x;attack.endY=end.y;
            Create(attack,definition);
        }
        public static ProjectileState Create(AttackEvent attack,WeaponDefinition definition)
        {
            var speeds=definition.ProjectileSpeedRange;
            float range=Vector2.Distance(attack.Origin,attack.End),expected=definition.FlightRange(attack.speed);
            if(!float.IsFinite(attack.speed) || attack.speed<speeds.x || attack.speed>speeds.y ||
                !float.IsFinite(range) || Mathf.Abs(range-expected)>Mathf.Max(.001f,expected*.00001f))
                throw new ArgumentException($"Некорректная траектория: shot={attack.id}, weapon={attack.weapon}, speed={attack.speed}, range={range}, expected={expected}.");
            var state=new ProjectileState {launch=StateJson.Copy(attack),owner=attack.sourceOwner,generation=1,flightSpeed=attack.speed};
            state.Path();return state;
        }
    }
}
