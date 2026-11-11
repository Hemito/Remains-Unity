using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remains.Gameplay
{
    public enum PlayerBinding
    {
        Left,Right,Up,Down,Jump,Sprint,Inventory,Menu,Reload,Interact,Telekinesis,Grenade,Heal,Strike,
        Slot1,Slot2,Slot3,Slot4,Slot5,Slot6,Slot7,Slot8,Slot9,Slot0,SlotMinus,SlotEquals
    }

    public sealed class KeyboardBindings
    {
        private static readonly (Key key,Func<string> action)[] definitions=
        {
            (Key.A,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.b7d9d6924d7583bf", "Движение влево", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.D,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.98a3f37e1558e5d7", "Движение вправо", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.W,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.13973caa31e8e362", "Вверх / встать", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.S,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.4631754738e7175e", "Вниз / присесть", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),
            (Key.Space,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.5b1b5a05b2d4f568", "Прыжок", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.LeftShift,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.f3758fc6f971587f", "Бег / модификатор", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Tab,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.3c7fd48a2f8d798a", "Инвентарь", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Escape,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.72bc78b12a6cb391", "Меню / закрыть окно", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),
            (Key.R,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.38a1907ceacc134c", "Перезарядка", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.E,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.a7a7c218126169bd", "Взаимодействие / помощь", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Q,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.6bb5d2c7d65ad8f5", "Телекинез", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.G,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.1d2006afc3e40470", "Бросить гранату", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),
            (Key.H,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.f9a8b05f19b36d31", "Аптечка", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.F,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.fa6e9746466df689", "Удар копытом", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),
            (Key.Digit1,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.25ab204b14416ac8", "Ячейка 1", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Digit2,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.9c213364fd1e189e", "Ячейка 2", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Digit3,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.826f5466634835c4", "Ячейка 3", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Digit4,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.5ab5ee84cdf358c0", "Ячейка 4", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),
            (Key.Digit5,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.b79db45439cc35e1", "Ячейка 5", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Digit6,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.42dad65a9e585352", "Ячейка 6", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Digit7,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.614133cd1fb891fa", "Ячейка 7", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Digit8,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.1a644bc1ff59f552", "Ячейка 8", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),
            (Key.Digit9,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.65c481b72964a0c3", "Ячейка 9", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Digit0,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.6a78727bdf2d622e", "Ячейка 0", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Minus,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.58bb9d13ae17570c", "Ячейка −", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs")),(Key.Equals,()=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.d4db0d69cf65e5f0", "Ячейка =", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"))
        };
        public static readonly KeyboardBindings Defaults=new(new Key[definitions.Length]);
        public static int Count=>definitions.Length;
        private readonly Key[] keys;
        private KeyboardBindings(Key[] keys)
        {
            this.keys=keys;
        }
        public static string ActionName(PlayerBinding action)=>definitions[(int)action].action();
        public static PlayerBinding Shortcut(int index)=>(PlayerBinding)((int)PlayerBinding.Slot1+index);
        public Key KeyFor(PlayerBinding action)=>keys[(int)action]==Key.None?definitions[(int)action].key:keys[(int)action];
        private Key Alternate(PlayerBinding action)=>action switch
        {
            PlayerBinding.Sprint when KeyFor(action)==Key.LeftShift=>Key.RightShift,
            PlayerBinding.Menu when KeyFor(action)!=Key.Escape=>Key.Escape,
            _=>Key.None
        };
        public bool Pressed(Keyboard keyboard,PlayerBinding action)
        {
            if(keyboard==null) return false;
            var alternate=Alternate(action);
            return keyboard[KeyFor(action)].wasPressedThisFrame || alternate!=Key.None && keyboard[alternate].wasPressedThisFrame;
        }
        public bool Held(Keyboard keyboard,PlayerBinding action)
        {
            if(keyboard==null) return false;
            var alternate=Alternate(action);
            return keyboard[KeyFor(action)].isPressed || alternate!=Key.None && keyboard[alternate].isPressed;
        }
        public string Label(PlayerBinding action)=>action==PlayerBinding.Sprint && KeyFor(action)==Key.LeftShift?"Shift":KeyLabel(KeyFor(action));
        private static string KeyLabel(Key key)=>key switch
        {
            Key.Space=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.b3e093ff59e95b0c", "Пробел", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"),Key.Escape=>"Esc",Key.LeftShift=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.d4746b33c0eb5096", "Лев. Shift", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"),Key.RightShift=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.3e881abdc56e0bc0", "Прав. Shift", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"),
            Key.LeftCtrl=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.97d686960c33f7ac", "Лев. Ctrl", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"),Key.RightCtrl=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.53810ea6722180b8", "Прав. Ctrl", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"),Key.LeftAlt=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.01d54a35402bf8ba", "Лев. Alt", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"),Key.RightAlt=>Remains.Content.GameLocalization.UiText("source.KeyboardBindings.647fdf726e2c1465", "Прав. Alt", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs"),
            Key.UpArrow=>"↑",Key.DownArrow=>"↓",Key.LeftArrow=>"←",Key.RightArrow=>"→",
            Key.Minus=>"-",Key.Equals=>"=",Key.Backquote=>"`",Key.LeftBracket=>"[",Key.RightBracket=>"]",
            Key.Semicolon=>";",Key.Quote=>"'",Key.Comma=>",",Key.Period=>".",Key.Slash=>"/",Key.Backslash=>"\\",
            >=Key.Digit1 and <=Key.Digit0=>((int)(key-Key.Digit1+1)%10).ToString(),
            _=>key.ToString()
        };
        public bool TryAssign(PlayerBinding action,Key key,out KeyboardBindings result,out string error)
        {
            result=this;
            if(key<=Key.None || key>=Key.IMESelected || !Enum.IsDefined(typeof(Key),key))
            {error=Remains.Content.GameLocalization.UiText("source.KeyboardBindings.4c756198bb339631", "Эту клавишу нельзя назначить.", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs");return false;}
            var edited=(Key[])keys.Clone();
            edited[(int)action]=key;
            var candidate=new KeyboardBindings(edited);
            error=candidate.Conflict(action);
            if(error!=null) return false;
            result=candidate;return true;
        }
        private string Conflict(PlayerBinding action)
        {
            var key=KeyFor(action);var alternate=Alternate(action);
            for(int i=0;i<Count;i++)
            {
                var other=(PlayerBinding)i;
                if(other==action) continue;
                if(key==KeyFor(other) || key==Alternate(other) || alternate!=Key.None && alternate==KeyFor(other))
                    return Remains.Content.GameLocalization.Format("source.KeyboardBindings.5afb602878b40a4d", $"Клавиша {KeyLabel(key==KeyFor(other) || key==Alternate(other)?key:alternate)} уже назначена действию «{ActionName(other)}».", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs");
            }
            return null;
        }
        internal static KeyboardBindings FromOverrides(Dictionary<PlayerBinding,Key> overrides)
        {
            if(overrides==null) throw new InvalidDataException("Файл назначений пуст.");
            var values=new Key[Count];
            foreach(var (action,key) in overrides)
            {
                if((int)action<0 || (int)action>=Count || key<=Key.None || key>=Key.IMESelected || !Enum.IsDefined(typeof(Key),key))
                    throw new InvalidDataException($"Неизвестное назначение {action} = {key}.");
                values[(int)action]=key;
            }
            var bindings=new KeyboardBindings(values);
            for(int i=0;i<Count;i++)
                if(bindings.Conflict((PlayerBinding)i) is string error) throw new InvalidDataException(error);
            return bindings;
        }
        internal Dictionary<PlayerBinding,Key> Overrides()
        {
            var result=new Dictionary<PlayerBinding,Key>();
            for(int i=0;i<Count;i++)
                if(keys[i]!=Key.None && keys[i]!=definitions[i].key) result.Add((PlayerBinding)i,keys[i]);
            return result;
        }
        public PlayerCommand Read(Keyboard keyboard)
        {
            if(keyboard==null) return default;
            bool sprint=Held(keyboard,PlayerBinding.Sprint);
            return new PlayerCommand
            {
                movement=new Vector2((Held(keyboard,PlayerBinding.Right)?1:0)-(Held(keyboard,PlayerBinding.Left)?1:0),
                    (Held(keyboard,PlayerBinding.Up)?1:0)-(Held(keyboard,PlayerBinding.Down)?1:0)),
                jump=Pressed(keyboard,PlayerBinding.Jump),jumpHeld=Held(keyboard,PlayerBinding.Jump),sprint=sprint,
                inventory=Pressed(keyboard,PlayerBinding.Inventory),menu=Pressed(keyboard,PlayerBinding.Menu),
                reload=Pressed(keyboard,PlayerBinding.Reload),interact=Pressed(keyboard,PlayerBinding.Interact),
                revive=Held(keyboard,PlayerBinding.Interact),telekinesis=Held(keyboard,PlayerBinding.Telekinesis),
                grenade=Pressed(keyboard,PlayerBinding.Grenade),heal=Pressed(keyboard,PlayerBinding.Heal),strike=Pressed(keyboard,PlayerBinding.Strike),
                weaponShortcut=ItemShortcutInput.Pressed(keyboard,this),shortcutWholeStack=sprint,throwWholeStack=sprint
            };
        }
    }

    // Per local save directory. The slot IDs in the networked inventory stay independent of physical keys.
    public sealed class InputPreferences
    {
        private readonly string path;
        public KeyboardBindings Current {get;private set;}=KeyboardBindings.Defaults;
        public string LoadWarning {get;private set;}
        public event Action Changed;
        public InputPreferences(string path)
        {
            this.path=path;
            if(path==null || !File.Exists(path)) return;
            try {Current=KeyboardBindings.FromOverrides(JsonConvert.DeserializeObject<Dictionary<PlayerBinding,Key>>(File.ReadAllText(path)));}
            catch(Exception error) when(error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
            {
                LoadWarning=Remains.Content.GameLocalization.UiText("source.KeyboardBindings.5a64bf0f8f10d3ea", "Файл управления повреждён или недоступен. Загружены стандартные клавиши.", "RemainsUnity/Assets/Remains/Gameplay/KeyboardBindings.cs");
                Debug.LogWarning($"{LoadWarning} {path} · {error.Message}");
            }
        }
        public void Save(KeyboardBindings value)
        {
            if(value==null) throw new ArgumentNullException(nameof(value));
            if(path!=null) AtomicSaveFile.Write(path,JsonConvert.SerializeObject(value.Overrides(),Formatting.Indented,new StringEnumConverter()));
            Current=value;LoadWarning=null;Changed?.Invoke();
        }
    }
}
