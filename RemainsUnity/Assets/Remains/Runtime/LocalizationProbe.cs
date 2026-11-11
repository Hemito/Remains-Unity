using System;
using Remains.Content;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace Remains.Runtime
{
    // Explicit headless Player check: validates the packaged Addressables catalog, not Editor asset loading.
    public static class LocalizationProbe
    {
        public static bool IsRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"--localization-check")>=0;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static async void Run()
        {
            if(!IsRequested)return;
            bool hadPreference=PlayerPrefs.HasKey(GameLocalization.Preference);
            string preference=PlayerPrefs.GetString(GameLocalization.Preference,"");
            try
            {
                Debug.Log("LOCALIZATION PLAYER START");
                await GameLocalization.PrepareAsync();string original=GameLocalization.CurrentLanguage;
                foreach(var language in GameLocalization.Languages)
                {
                    GameLocalization.Select(language.code);
                    if(LocalizationSettings.SelectedLocale.Identifier.Code!=language.code)throw new InvalidOperationException("Не переключился Locale: "+language.code);
                    if(string.IsNullOrWhiteSpace(GameLocalization.UiText("unity/language","Язык","Player probe")))throw new InvalidOperationException("Пустая подпись языка: "+language.code);
                }
                GameLocalization.Select("en");
                GameLocalization.Select("ru");GameLocalization.Select("en");
                var cleanup=Resources.UnloadUnusedAssets();
                while(!cleanup.isDone)await System.Threading.Tasks.Task.Yield();
                string english=GameLocalization.UiText("unity/language","Язык","Player probe");
                if(english!="Language" && english!="Interface language")throw new InvalidOperationException("Не загружена английская String Table: "+english);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"--localization-mod-check")>=0)
                {
                    var locale=LocalizationSettings.AvailableLocales.GetLocale("fr");
                    if(locale==null || english!="Interface language")throw new InvalidOperationException("Не применён пример мода локализации.");
                    var entry=LocalizationSettings.StringDatabase.GetTableEntry("example:ui.Messages","greeting",locale,FallbackBehavior.DontUseFallback);
                    if(entry.Entry?.LocalizedValue!="Bonjour du module de traduction !")throw new InvalidOperationException("Не загружена новая таблица мода.");
                }
                GameLocalization.Select(original);
                Debug.Log("LOCALIZATION PLAYER PASS: locales="+GameLocalization.Languages.Count+", official StringDatabase, packaged tables and optional local mods.");
                Application.Quit(0);
            }
            catch(Exception error){Debug.LogException(error);Application.Quit(1);}
            finally
            {
                if(hadPreference)PlayerPrefs.SetString(GameLocalization.Preference,preference);else PlayerPrefs.DeleteKey(GameLocalization.Preference);
                PlayerPrefs.Save();
            }
        }
    }
}
