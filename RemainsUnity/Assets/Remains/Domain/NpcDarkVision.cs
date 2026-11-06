using System;

namespace Remains.Domain
{
    // How far an NPC notices a player by the light around that player (user 2026-11-06). Not in the original: it scales
    // sight by the location only (Unit.look, loc.visMult). A pony's horn glows while it holds something, so a held item
    // shows even in the dark. Values fit the factory light grid of seed104: below a lamp .86, 6 m away .36, 70 % of the
    // NPC places 0. Close detection stays: a body within closeDistance is noticed in any light.
    public static class NpcDarkVision
    {
        public const float DarkShare=.35f,FullLight=.35f,CarriedLight=.12f;
        public static bool Carries(ActorState actor)=>actor!=null &&
            (!string.IsNullOrEmpty(actor.equippedWeaponItem) || !string.IsNullOrEmpty(actor.heldItemId));
        // The share of the full sight distance at this brightness (0–1).
        public static float Share(float brightness,bool carried)
        {
            if(float.IsNaN(brightness)) throw new ArgumentOutOfRangeException(nameof(brightness));
            float light=Math.Max(Math.Max(0,brightness),carried?CarriedLight:0);
            return Math.Min(1,DarkShare+(1-DarkShare)*light/FullLight);
        }
    }
}
