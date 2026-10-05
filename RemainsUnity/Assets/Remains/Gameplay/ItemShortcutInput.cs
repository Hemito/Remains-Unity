using Remains.Domain;
using UnityEngine.InputSystem;

namespace Remains.Gameplay
{
    public static class ItemShortcutInput
    {
        public static Key KeyFor(string slot)=>slot switch
        {
            "1"=>Key.Digit1,"2"=>Key.Digit2,"3"=>Key.Digit3,"4"=>Key.Digit4,"5"=>Key.Digit5,"6"=>Key.Digit6,
            "7"=>Key.Digit7,"8"=>Key.Digit8,"9"=>Key.Digit9,"0"=>Key.Digit0,"-"=>Key.Minus,"="=>Key.Equals,
            _=>throw new System.ArgumentException("Неизвестная быстрая клавиша предмета: "+slot)
        };
        public static string Pressed(Keyboard keyboard)
        {
            if(keyboard==null) return null;
            foreach(var slot in ItemShortcuts.Keys) if(keyboard[KeyFor(slot)].wasPressedThisFrame) return slot;
            return null;
        }
    }
}
