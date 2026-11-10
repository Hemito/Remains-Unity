using System;
using System.Collections.Generic;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class LocalizationLanguage {public string code,nativeName,sourceSha256;public LocalizationEntry[] entries;public ItemTextDefinition[] items;public string sourceXml;}
    [Serializable] public sealed class LocalizationEntry {public string key,russian,text;}
    [Serializable] public sealed class LocalizationManifest {public int schema;public string sourceLanguage;public LocalizationLanguage[] languages;}

    // Local presentation only: language never changes stable content IDs, saves or network fingerprints.
    public static class GameLocalization
    {
        public const string Resource="Content/localization",Preference="remains.language";
        private static LocalizationManifest manifest;
        private static readonly Dictionary<string,LocalizationLanguage> languages=new(StringComparer.Ordinal);
        private static readonly Dictionary<string,string> keyed=new(StringComparer.Ordinal),byRussian=new(StringComparer.Ordinal);
        private static readonly Dictionary<string,ItemTextDefinition> items=new(StringComparer.Ordinal);
        private static readonly HashSet<string> warned=new(StringComparer.Ordinal);
        public static event Action Changed;
        public static string CurrentLanguage {get;private set;}="ru";
        public static IReadOnlyList<LocalizationLanguage> Languages {get{Initialize();return manifest.languages;}}
        public static void Initialize()
        {
            if(manifest!=null)return;
            var asset=GameAssets.Load<TextAsset>(Resource);
            if(asset==null)throw new InvalidOperationException("Нет пакетного каталога локализации: "+Resource);
            var prepared=Parse(asset.text);
            foreach(var language in prepared.languages)languages.Add(language.code,language);
            manifest=prepared;
            string selected=PlayerPrefs.GetString(Preference,"ru");
            if(!languages.ContainsKey(selected)){Debug.LogWarning("Неизвестный сохранённый язык: "+selected+". Используется исходный ru.");selected="ru";}
            Apply(selected);
        }
        public static LocalizationManifest Parse(string json,bool requireRussian=true)
        {
            var value=StateJson.FromJson<LocalizationManifest>(json);
            if(value?.schema!=1 || value.sourceLanguage!="ru" || value.languages==null || value.languages.Length==0)
                throw new InvalidOperationException("Некорректный пакет локализации: нужны schema=1 и исходный язык ru.");
            var codes=new HashSet<string>(StringComparer.Ordinal);
            foreach(var language in value.languages)
            {
                if(language==null || string.IsNullOrWhiteSpace(language.code) || string.IsNullOrWhiteSpace(language.nativeName) || !codes.Add(language.code) || language.entries==null || language.items==null)
                    throw new InvalidOperationException("Некорректный/повторный язык пакета: "+language?.code);
                var keys=new HashSet<string>(StringComparer.Ordinal);
                foreach(var entry in language.entries)if(entry==null || string.IsNullOrWhiteSpace(entry.key) || !keys.Add(entry.key) || entry.text==null)
                    throw new InvalidOperationException("Некорректный/повторный ключ локализации: language="+language.code+", key="+entry?.key);
                var ids=new HashSet<string>(StringComparer.Ordinal);
                foreach(var item in language.items)if(item==null || string.IsNullOrWhiteSpace(item.id) || string.IsNullOrWhiteSpace(item.name) || !ids.Add(item.id))
                    throw new InvalidOperationException("Некорректный перевод предмета: language="+language.code+", id="+item?.id);
            }
            if(requireRussian && !codes.Contains("ru"))throw new InvalidOperationException("В локализации нет исходного ru.");
            return value;
        }
        public static LocalizationLanguage LoadLanguage(string code)
        {
            var asset=GameAssets.Load<TextAsset>(Resource+"/"+code);
            if(asset==null)throw new InvalidOperationException("Нет подготовленного языкового файла: "+code);
            try
            {
                var pack=Parse(asset.text,false);
                if(pack.languages.Length!=1 || pack.languages[0].code!=code)throw new InvalidOperationException("Не совпал язык файла: "+code);
                return pack.languages[0];
            }
            finally{Resources.UnloadAsset(asset);}
        }
        public static void Select(string code)
        {
            Initialize();if(!languages.ContainsKey(code))throw new ArgumentException("Неизвестный язык: "+code);
            if(CurrentLanguage==code)return;
            Apply(code);PlayerPrefs.SetString(Preference,code);PlayerPrefs.Save();Changed?.Invoke();
        }
        private static void Apply(string code)
        {
            var language=LoadLanguage(code);
            CurrentLanguage=code;keyed.Clear();byRussian.Clear();items.Clear();warned.Clear();
            var ambiguous=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in language.entries)
            {
                keyed.Add(entry.key,entry.text);
                if(entry.key.StartsWith("unity/",StringComparison.Ordinal))
                {byRussian[entry.russian]=entry.text;ambiguous.Remove(entry.russian);continue;}
                if(string.IsNullOrWhiteSpace(entry.russian) || ambiguous.Contains(entry.russian))continue;
                if(byRussian.TryGetValue(entry.russian,out string previous) && previous!=entry.text)
                {byRussian.Remove(entry.russian);ambiguous.Add(entry.russian);}
                else byRussian[entry.russian]=entry.text;
            }
            foreach(var item in language.items)items.Add(item.id,item);
        }
        public static string Text(string key,string russian,string context)
        {
            Initialize();
            if(keyed.TryGetValue(key,out string value))return value;
            if(CurrentLanguage=="ru")return russian;
            Warn(key,context);return russian;
        }
        public static string TranslateRussian(string russian,string context)
        {
            Initialize();if(CurrentLanguage=="ru" || string.IsNullOrWhiteSpace(russian))return russian;
            if(byRussian.TryGetValue(russian,out string translated))return translated;
            // IDs, numbers and user names are not a translation request.
            foreach(char c in russian)if(c>='А' && c<='я' || c is 'ё' or 'Ё'){Warn(russian,context);break;}
            return russian;
        }
        public static string ItemName(string id,string russian,string context)
        {
            Initialize();if(CurrentLanguage=="ru")return russian;
            if(id!=null && items.TryGetValue(id,out var item))return item.name;
            return TranslateRussian(russian,context+" item="+id);
        }
        private static void Warn(string key,string context)
        {
            if(warned.Add(CurrentLanguage+"|"+key))Debug.LogWarning($"Нет однозначного перевода: language={CurrentLanguage}, key={key}, context={context}. Показан русский исходный текст.");
        }
    }
}
