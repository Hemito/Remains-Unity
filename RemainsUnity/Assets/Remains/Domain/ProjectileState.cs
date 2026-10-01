using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class ProjectileState : ExtensibleState
    {
        public AttackEvent launch;
        public string owner;
        public int generation;
        public uint sequence;
        public float flightSpeed,traveled;
        public bool finished;
        public LinearProjectilePath Path()
        {
            if(launch==null || string.IsNullOrEmpty(launch.id) || string.IsNullOrEmpty(launch.level) || string.IsNullOrEmpty(launch.attacker))
                throw new InvalidOperationException("Сохранённый полёт не содержит ID выстрела, уровня или автора.");
            var delta=launch.End-launch.Origin;
            return new LinearProjectilePath(launch.Origin,delta,flightSpeed,delta.magnitude,traveled,finished);
        }
        public ProjectileState Copy()=>StateJson.Copy(this);
    }
    public static class ProjectileLifecycle
    {
        public static void BeginSession(WorldState world)
        {
            foreach(var flight in world.projectiles)
            {flight.Path();flight.owner=null;flight.generation++;flight.sequence=0;}
        }
        public static void Reassign(WorldState world,IReadOnlyCollection<string> owners)
        {
            var players=world.actors.Where(a=>a.player && a.connected && owners.Contains(a.owner)).ToArray();
            foreach(var flight in world.projectiles)
            {
                if(flight.finished) continue;
                if(players.Any(a=>a.owner==flight.owner && a.level==flight.launch.level)) continue;
                string next=NearbySimulationClient.Select(players,flight.launch.level,flight.Path().Position);
                if(next==flight.owner) continue;
                flight.owner=next;flight.generation++;flight.sequence=0;
            }
        }
        public static bool AcceptProgress(ProjectileState flight,string owner,int generation,uint sequence,float traveled,bool finished)
        {
            float range=Vector2.Distance(flight.launch.Origin,flight.launch.End);
            if(flight.finished || string.IsNullOrEmpty(owner) || owner!=flight.owner || generation!=flight.generation ||
                unchecked((int)(sequence-flight.sequence))<=0 || !float.IsFinite(traveled) || traveled<flight.traveled || traveled>range) return false;
            flight.traveled=traveled;flight.finished=finished || traveled==range;flight.sequence=sequence;
            return true;
        }
    }
}
