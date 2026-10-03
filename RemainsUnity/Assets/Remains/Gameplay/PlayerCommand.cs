using UnityEngine;

namespace Remains.Gameplay
{
    // Commands contain intent only; both devices and automated scenarios use this contract.
    public struct PlayerCommand
    {
        public Vector2 movement, aim;
        public bool hasAim, jump, jumpHeld, sprint, fire, revive, telekinesis, grenade, heal, inventory, reload, menu;
        public string weapon; // Legacy definition command for v0 actors.
        public string weaponItem,weaponShortcut;
    }
}
