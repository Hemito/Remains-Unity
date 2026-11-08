using Remains.Domain;
using UnityEngine.InputSystem;

namespace Remains.Gameplay
{
    public static class ItemShortcutInput
    {
        public static Key KeyFor(string slot)
        {
            for(int i=0;i<ItemShortcuts.Keys.Count;i++)
                if(ItemShortcuts.Keys[i]==slot) return KeyboardBindings.Defaults.KeyFor(KeyboardBindings.Shortcut(i));
            throw new System.ArgumentException("Неизвестная быстрая клавиша предмета: "+slot);
        }
        public static string Pressed(Keyboard keyboard,KeyboardBindings bindings=null)
        {
            if(keyboard==null) return null;
            bindings??=KeyboardBindings.Defaults;
            for(int i=0;i<ItemShortcuts.Keys.Count;i++)
                if(bindings.Pressed(keyboard,KeyboardBindings.Shortcut(i))) return ItemShortcuts.Keys[i];
            return null;
        }
    }
}
