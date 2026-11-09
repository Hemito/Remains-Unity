using Remains.Content;

namespace Remains.Domain
{
    public static class LightTestItems
    {
        public const string Ball="item:light-ball",Stick="item:glow-stick",Flashlight="item:test-flashlight";
        public static bool IsHandheldTool(ItemStack item)=>item?.definition==Flashlight;
        public static bool IsLight(string weapon)=>WeaponCatalog.Get(weapon).IsThrowableLight;
        public static bool IsContactProjectile(string weapon)=>WeaponCatalog.Get(weapon).throwableLight is {Configured:true,contactProjectile:true};
        // Lifetime follows the existing grenade simulation: seconds of active, unpaused level time; it is saved.
        public static bool Expire(WorldState world,GrenadeState state)
        {
            if(state==null || state.remaining>0 || !IsLight(state.weapon)) return false;
            return world.grenades.Remove(state);
        }
    }
}
