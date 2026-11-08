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
        private static readonly (Key key,string action)[] definitions=
        {
            (Key.A,"Движение влево"),(Key.D,"Движение вправо"),(Key.W,"Вверх / встать"),(Key.S,"Вниз / присесть"),
            (Key.Space,"Прыжок"),(Key.LeftShift,"Бег / модификатор"),(Key.Tab,"Инвентарь"),(Key.Escape,"Меню / закрыть окно"),
            (Key.R,"Перезарядка"),(Key.E,"Взаимодействие / помощь"),(Key.Q,"Телекинез"),(Key.G,"Бросить гранату"),
            (Key.H,"Аптечка"),(Key.F,"Удар копытом"),
            (Key.Digit1,"Ячейка 1"),(Key.Digit2,"Ячейка 2"),(Key.Digit3,"Ячейка 3"),(Key.Digit4,"Ячейка 4"),
            (Key.Digit5,"Ячейка 5"),(Key.Digit6,"Ячейка 6"),(Key.Digit7,"Ячейка 7"),(Key.Digit8,"Ячейка 8"),
            (Key.Digit9,"Ячейка 9"),(Key.Digit0,"Ячейка 0"),(Key.Minus,"Ячейка −"),(Key.Equals,"Ячейка =")
        };
        public static readonly KeyboardBindings Defaults=new(new Key[definitions.Length]);
        public static int Count=>definitions.Length;
        private readonly Key[] keys;
        private readonly string[] labels;
        private KeyboardBindings(Key[] keys)
        {
            this.keys=keys;labels=new string[keys.Length];
            for(int i=0;i<labels.Length;i++) labels[i]=KeyLabel(KeyFor((PlayerBinding)i));
            if(KeyFor(PlayerBinding.Sprint)==Key.LeftShift) labels[(int)PlayerBinding.Sprint]="Shift";
        }
        public static string ActionName(PlayerBinding action)=>definitions[(int)action].action;
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
        public string Label(PlayerBinding action)=>labels[(int)action];
        private static string KeyLabel(Key key)=>key switch
        {
            Key.Space=>"Пробел",Key.Escape=>"Esc",Key.LeftShift=>"Лев. Shift",Key.RightShift=>"Прав. Shift",
            Key.LeftCtrl=>"Лев. Ctrl",Key.RightCtrl=>"Прав. Ctrl",Key.LeftAlt=>"Лев. Alt",Key.RightAlt=>"Прав. Alt",
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
            {error="Эту клавишу нельзя назначить.";return false;}
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
                    return $"Клавиша {KeyLabel(key==KeyFor(other) || key==Alternate(other)?key:alternate)} уже назначена действию «{ActionName(other)}».";
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
                LoadWarning="Файл управления повреждён или недоступен. Загружены стандартные клавиши.";
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
