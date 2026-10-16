using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class LampBreakRequestData : AttackContactRequest
    {
        public string lamp;
    }
    public static class LampBreakRules
    {
        public static string Validate(WorldState world,ActorState participant,LampBreakRequestData request,DoorDamageAttackLedger ledger,out LampState lamp,Func<string,WeaponDefinition> weapons=null)
        {
            lamp=null;
            string reason=AttackContactRules.Validate(world,participant,request,ledger,"лампы",weapons,out var weapon);
            if(reason!=null) return reason;
            lamp=world.lamps.Find(value=>value.id==request.lamp);
            if(lamp==null || lamp.level!=request.attack.level) return "Не найдена лампа в локации атаки.";
            lamp.Validate();
            if(!SurfaceContact.Entry(lamp.Bounds,request.attack.Origin,request.attack.End,weapon.projectileSpeed>0?0:request.attack.radius).HasValue)
                return "Сегмент не касается рисунка лампы.";
            return null;
        }
    }
}
