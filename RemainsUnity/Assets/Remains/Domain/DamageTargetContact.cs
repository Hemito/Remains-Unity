using System;

namespace Remains.Domain
{
    // Contact at the authoritative damage decision, independent of render pose and sampled foot artwork.
    public readonly struct DamageTargetContact
    {
        public readonly bool Grounded,InWater,OnLadder;
        public DamageTargetContact(bool grounded,bool inWater,bool onLadder)
        {Grounded=grounded;InWater=inWater;OnLadder=onLadder;}
        public float ElectricMultiplier=>Grounded || InWater || OnLadder?1:.5f;
        // Current simulation rooms contain no water volumes or swimming state. Keep that explicit here;
        // a future water system must supply InWater from its authoritative contact instead of a visual effect.
        public static DamageTargetContact InDryRoom(ActorState target,RoomLayout room)
        {
            if(target==null || room==null) throw new ArgumentException("Контакт цели требует актёра и геометрию локации.");
            bool grounded=target.vy<=0 && ActorTerrainMotion.Supported(room,Anatomy.Movement(target),target.Position);
            return new DamageTargetContact(grounded,inWater:false,onLadder:target.climbing);
        }
    }
}
