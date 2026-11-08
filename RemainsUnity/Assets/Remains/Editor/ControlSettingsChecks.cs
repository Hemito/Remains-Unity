using System;
using System.IO;
using Remains.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Remains.Editor
{
    public static class ControlSettingsChecks
    {
        public static void Run()
        {
            var defaults=KeyboardBindings.Defaults;
            Require(!defaults.TryAssign(PlayerBinding.Reload,Key.E,out var rejected,out var conflict) &&
                ReferenceEquals(rejected,defaults) && conflict.Contains("Взаимодействие"),"Конфликт не называет существующее действие");
            Require(!defaults.TryAssign(PlayerBinding.Jump,Key.RightShift,out _,out _) &&
                !defaults.TryAssign(PlayerBinding.Heal,Key.Escape,out _,out _),"Не учтены резервные клавиши");
            var changed=Assign(defaults,PlayerBinding.Interact,Key.K);
            changed=Assign(changed,PlayerBinding.Slot1,Key.F2);
            changed=Assign(changed,PlayerBinding.Left,Key.LeftArrow);
            changed=Assign(changed,PlayerBinding.Sprint,Key.LeftCtrl);
            changed=Assign(changed,PlayerBinding.Menu,Key.Home);
            changed=Assign(changed,PlayerBinding.Jump,Key.J);
            Require(changed.Label(PlayerBinding.Interact)=="K" && changed.Label(PlayerBinding.Slot1)=="F2" &&
                defaults.Label(PlayerBinding.Interact)=="E" && defaults.Label(PlayerBinding.Slot0)=="0","Неверная подпись или изменение общих стандартных клавиш");

            var path=Path.Combine(Path.GetTempPath(),"Remains-controls-check-"+Guid.NewGuid().ToString("N")+".json");
            try
            {
                var prefs=new InputPreferences(path);int notifications=0;prefs.Changed+=()=>notifications++;
                prefs.Save(changed);
                var reloaded=new InputPreferences(path);
                Require(notifications==1 && reloaded.LoadWarning==null && reloaded.Current.KeyFor(PlayerBinding.Interact)==Key.K &&
                    reloaded.Current.KeyFor(PlayerBinding.Slot1)==Key.F2,"Файл не переживает создание нового экземпляра");
                CheckKeyboard(reloaded.Current);
                prefs.Save(defaults);
                Require(notifications==2 && new InputPreferences(path).Current.KeyFor(PlayerBinding.Interact)==Key.E,"Сброс не сохраняется");
                File.WriteAllText(path,"{\"Interact\":\"R\"}");
                var invalid=new InputPreferences(path);
                Require(invalid.LoadWarning!=null && invalid.Current.KeyFor(PlayerBinding.Interact)==Key.E &&
                    File.ReadAllText(path)=="{\"Interact\":\"R\"}","Некорректный файл уничтожен или оставил конфликт");
            }
            finally {File.Delete(path);File.Delete(path+".previous");}
            Debug.Log("Управление: PASS — конфликты, профиль и сброс, игровые действия при удержании физической клавиши.");
        }

        private static KeyboardBindings Assign(KeyboardBindings previous,PlayerBinding action,Key key)
        {
            if(!previous.TryAssign(action,key,out var next,out var error)) throw new InvalidOperationException(error);
            return next;
        }
        private static void CheckKeyboard(KeyboardBindings bindings)
        {
            var previous=InputSystem.settings.updateMode;
            var keyboard=InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.Update();
                bindings.Read(keyboard);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftArrow,Key.LeftCtrl,Key.K,Key.F2,Key.J,Key.Home));
                InputSystem.Update();
                // Editor input updates maintain held state. Play Mode is required
                // for Unity's wasPressedThisFrame edge tracking.
                var command=bindings.Read(keyboard);
                Require(command.movement==Vector2.left && command.sprint && command.shortcutWholeStack && command.throwWholeStack &&
                    command.revive && command.jumpHeld && bindings.Held(keyboard,PlayerBinding.Menu) &&
                    bindings.Held(keyboard,PlayerBinding.Slot1),"Физические клавиши не дошли до PlayerCommand");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A,Key.RightShift,Key.E,Key.Digit1,Key.Space,Key.Escape));
                InputSystem.Update();
                command=bindings.Read(keyboard);
                Require(command.movement==Vector2.zero && !command.sprint && !command.revive && !command.jumpHeld &&
                    !bindings.Held(keyboard,PlayerBinding.Slot1) && bindings.Held(keyboard,PlayerBinding.Menu),
                    "Прежние клавиши остались активны или исчез безопасный Esc");
            }
            finally {InputSystem.RemoveDevice(keyboard);InputSystem.settings.updateMode=previous;}
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Управление: "+reason);}
    }
}
