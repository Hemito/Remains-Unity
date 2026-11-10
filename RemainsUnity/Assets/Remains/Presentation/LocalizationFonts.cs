using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // OS fonts are resolved only at explicit UI creation/language changes, never in Update.
    public static class LocalizationFonts
    {
        private static readonly Dictionary<string,Font> fonts=new(StringComparer.Ordinal);
        private static HashSet<string> installed;
        public static Font ForLanguage(string code)
        {
            if(fonts.TryGetValue(code,out var font) && font!=null)return font;
            if(code is not ("zh" or "tw" or "jp"))return fonts[code]=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            installed??=new HashSet<string>(Font.GetOSInstalledFontNames(),StringComparer.OrdinalIgnoreCase);
            string[] candidates=code=="jp"?new[]{"Yu Gothic","Meiryo","MS Gothic","Noto Sans CJK JP"}:
                code=="tw"?new[]{"Microsoft JhengHei","Noto Sans CJK TC","Microsoft YaHei"}:new[]{"Microsoft YaHei","Noto Sans CJK SC","Microsoft JhengHei"};
            foreach(string name in candidates)if(installed.Contains(name))
            {
                font=Font.CreateDynamicFontFromOSFont(name,18);font.hideFlags=HideFlags.HideAndDontSave;
                font.RequestCharactersInTexture(code=="jp"?"日本語":code=="tw"?"繁體中文":"简体中文",18);
                if(font.material==null)throw new InvalidOperationException("Не создан материал системного шрифта: "+name);
                font.material.hideFlags=HideFlags.HideAndDontSave;
                if(font.material.mainTexture!=null)font.material.mainTexture.hideFlags=HideFlags.HideAndDontSave;
                return fonts[code]=font;
            }
            Debug.LogWarning("Нет установленного шрифта для языка "+code+". Установите системный CJK-шрифт; показан LegacyRuntime, часть знаков может отсутствовать.");
            return fonts[code]=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        public static void ApplyTree(Transform root)
        {
            foreach(var text in root.GetComponentsInChildren<Text>(true))text.font=ForLanguage(GameLocalization.CurrentLanguage);
        }
    }
}
