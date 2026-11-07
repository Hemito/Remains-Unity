using System;
using System.Collections.Generic;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public static class ShortcutIconBindings
    {
        public static void Validate(Button[] buttons,Image[] icons)
        {
            if(buttons==null || icons==null || buttons.Length==0 || buttons.Length!=icons.Length)
                throw new InvalidOperationException("Быстрые клавиши: не совпадают явные ссылки кнопок и иконок.");
            var unique=new HashSet<Image>();
            var uniqueButtons=new HashSet<Button>();
            for(int i=0;i<buttons.Length;i++)
                if(buttons[i]==null || !uniqueButtons.Add(buttons[i]) || icons[i]==null || !unique.Add(icons[i]) || icons[i].gameObject==buttons[i].gameObject ||
                    !icons[i].transform.IsChildOf(buttons[i].transform))
                    throw new InvalidOperationException("Быстрые клавиши: отсутствует отдельная иконка нужной кнопки, позиция "+i+".");
        }
    }
}
