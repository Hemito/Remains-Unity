using System;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class ShortcutIconBindingChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка явных иконок");
            try
            {
                var buttons=new Button[12];var icons=new Image[12];
                for(int i=0;i<buttons.Length;i++)
                {
                    var button=new GameObject("Клавиша "+i,typeof(RectTransform));button.transform.SetParent(root.transform,false);
                    buttons[i]=button.AddComponent<Button>();
                    var icon=new GameObject("Иконка предмета",typeof(RectTransform));icon.transform.SetParent(button.transform,false);icons[i]=icon.AddComponent<Image>();
                }
                ShortcutIconBindings.Validate(buttons,icons);
                foreach(var icon in icons) icon.gameObject.name="Произвольное имя после переименования";
                ShortcutIconBindings.Validate(buttons,icons);
                var first=icons[0];icons[0]=icons[1];Reject(()=>ShortcutIconBindings.Validate(buttons,icons));icons[0]=first;
                icons[0]=null;Reject(()=>ShortcutIconBindings.Validate(buttons,icons));icons[0]=first;
                first.transform.SetParent(buttons[1].transform,false);Reject(()=>ShortcutIconBindings.Validate(buttons,icons));first.transform.SetParent(buttons[0].transform,false);
                icons[0]=buttons[0].gameObject.AddComponent<Image>();Reject(()=>ShortcutIconBindings.Validate(buttons,icons));icons[0]=first;
                var button0=buttons[0];buttons[0]=null;Reject(()=>ShortcutIconBindings.Validate(buttons,icons));buttons[0]=button0;
                buttons[0]=buttons[1];Reject(()=>ShortcutIconBindings.Validate(buttons,icons));buttons[0]=button0;
                Reject(()=>ShortcutIconBindings.Validate(null,icons));Reject(()=>ShortcutIconBindings.Validate(buttons,new Image[1]));
                var group=new GameObject("Промежуточная группа",typeof(RectTransform));group.transform.SetParent(button0.transform,false);
                first.transform.SetParent(group.transform,false);ShortcutIconBindings.Validate(buttons,icons);
                Debug.Log("Явные иконки быстрых клавиш: PASS — переименование/вложенность, отсутствие, дубликаты, чужой слот и фон кнопки.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Reject(Action check)
        {bool rejected=false;try{check();}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidOperationException("Приняты неверные ссылки быстрых клавиш.");}
    }
}
