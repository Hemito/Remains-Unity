using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Remains.Content
{
    [Serializable] public sealed class LocalizationLanguage {public string code,nativeName,sourceSha256;public LocalizationEntry[] entries;public ItemTextDefinition[] items;public string sourceXml;}
    [Serializable] public sealed class LocalizationEntry {public string key,russian,text;}
    [Serializable] public sealed class LocalizationManifest {public int schema;public string sourceLanguage;public LocalizationLanguage[] languages;}
    [Serializable] public sealed class LocalizationSourceIndex {public int schema;public LocalizationLanguage[] languages;public LocalizationSourceEntry[] entries;}
    [Serializable] public sealed class LocalizationSourceEntry {public string russian,table,key;}

    // Local presentation only: language never changes stable content IDs, saves or network fingerprints.
    public static class GameLocalization
    {
        public const string Resource="Content/localization",Preference="remains.language";
        public const string OriginalTable="Remains.Original",UiTable="Remains.UI",ItemsTable="Remains.Items";
        private static LocalizationSourceIndex manifest;
        private static Exception initializationFailure;
        private static Task preparation;
        private static UnityLocalizationModProvider modProvider;
        private static Dictionary<string,StringTable> currentTables,russianTables;
        // SelectedLocale changes make Unity's database release its table handles. Keep our own
        // session-lifetime references: cached UnityObject references alone do not retain Addressables assets.
        private static readonly Dictionary<(string Table,string Locale),AsyncOperationHandle<StringTable>> retainedTables=new();
        private static readonly Dictionary<string,LocalizationLanguage> languages=new(StringComparer.Ordinal);
        // This index contains references only. Translations live exclusively in Unity String Tables.
        private static readonly Dictionary<string,LocalizationSourceEntry> byRussian=new(StringComparer.Ordinal);
        private static readonly HashSet<string> warned=new(StringComparer.Ordinal);
        public static event Action Changed;
        public static bool IsReady=>manifest!=null;
        public static string CurrentLanguage {get;private set;}="ru";
        public static IReadOnlyList<LocalizationLanguage> Languages {get{Initialize();return manifest.languages;}}
        public static Task PrepareAsync()
        {
            if(manifest!=null)return Task.CompletedTask;
            return preparation??=PrepareCoreAsync(false);
        }
        public static void Initialize()
        {
            if(initializationFailure!=null)throw new InvalidOperationException("Подготовка Unity Localization ранее завершилась ошибкой. Исправьте пакет и перезапустите игру.",initializationFailure);
            if(manifest!=null)return;
            #if UNITY_EDITOR
            if(!Application.isPlaying && preparation==null)
            {
                // Editor validation commands have no initial-scene activation to block.
                preparation=PrepareCoreAsync(true);preparation.GetAwaiter().GetResult();return;
            }
            #endif
            throw new InvalidOperationException("Unity Localization ещё не готова. Перед созданием игрового UI выполните await GameLocalization.PrepareAsync().");
        }
        private static async Task PrepareCoreAsync(bool synchronous)
        {
            try
            {
                var asset=GameAssets.Load<TextAsset>("Content/localization-index");
                if(asset==null)throw new InvalidOperationException("Нет индекса Unity Localization: Content/localization-index. Выполните пакетный импорт.");
                var prepared=StateJson.FromJson<LocalizationSourceIndex>(asset.text);
                if(prepared?.schema!=1 || prepared.languages==null || prepared.entries==null)throw new InvalidOperationException("Некорректный индекс Unity Localization.");
                foreach(var language in prepared.languages)languages.Add(language.code,language);
                foreach(var entry in prepared.entries)byRussian.Add(entry.russian,entry);
                Debug.Log("Unity Localization: исходный индекс готов, языков="+languages.Count+".");
                // Set before reading AvailableLocales or starting database initialization. In an initial
                // scene, WaitForCompletion can prevent the scene activation needed by Addressables.
                LocalizationSettings.InitializeSynchronously=false;
                if(LocalizationSettings.AvailableLocales is IPreloadRequired localeLoader)
                {
                    var localePreload=localeLoader.PreloadOperation;
                    if(synchronous)localePreload.WaitForCompletion();else await localePreload.Task;
                    if(localePreload.Status!=AsyncOperationStatus.Succeeded)throw new InvalidOperationException("Не загружены языки Unity Localization.");
                }
                await InstallModsAsync(prepared,synchronous);
                Debug.Log("Unity Localization: инициализация Settings.");
                await Complete(LocalizationSettings.InitializationOperation,synchronous);
                Debug.Log("Unity Localization: Settings готовы, загрузка русских таблиц.");
                var russian=await TablesForAsync("ru",synchronous);
                string selected=NormalizeCode(PlayerPrefs.GetString(Preference,"ru"));
                if(!languages.ContainsKey(selected)){Debug.LogWarning("Неизвестный сохранённый язык: "+selected+". Используется исходный ru.");selected="ru";}
                var selectedTables=selected=="ru"?russian:await TablesForAsync(selected,synchronous);
                var locale=LocalizationSettings.AvailableLocales.GetLocale(selected);
                if(locale==null)throw new InvalidOperationException("Не создан Locale Unity Localization: "+selected);
                LocalizationSettings.SelectedLocale=locale;
                await Complete(LocalizationSettings.InitializationOperation,synchronous);
                russianTables=russian;currentTables=selectedTables;CurrentLanguage=selected;
                manifest=prepared;
                LocalizationSettings.SelectedLocaleChanged+=OnLocaleChanged;
                Debug.Log("Unity Localization: выбран язык "+CurrentLanguage+".");
            }
            catch(Exception failure){initializationFailure=failure;throw;}
        }
        private static async Task<T> Complete<T>(AsyncOperationHandle<T> handle,bool synchronous)
        {
            var result=synchronous?handle.WaitForCompletion():await handle.Task;
            if(handle.Status!=AsyncOperationStatus.Succeeded)throw new InvalidOperationException("Ошибка асинхронной операции Unity Localization.",handle.OperationException);
            return result;
        }
        private static async Task InstallModsAsync(LocalizationSourceIndex source,bool synchronous)
        {
            var plan=LocalizationMods.Load(Path.Combine(Application.streamingAssetsPath,LocalizationMods.Folder));
            if(plan.Packages.Count==0)return;
            var handles=new List<AsyncOperationHandle<StringTable>>();
            try
            {
                var tables=new List<StringTable>();
                foreach(var language in source.languages)
                    foreach(string table in new[]{OriginalTable,UiTable,ItemsTable})
                    {
                        // Official Localization table address convention; validated before publishing any mod.
                        var handle=Addressables.LoadAssetAsync<StringTable>(table+"_"+language.code);handles.Add(handle);
                        var loaded=await Complete(handle,synchronous);
                        if(loaded==null || loaded.TableCollectionName!=table || loaded.LocaleIdentifier.Code!=language.code)
                            throw new InvalidDataException("Не загружена базовая String Table для модов: "+table+"/"+language.code);
                        tables.Add(loaded);
                    }
                modProvider=UnityLocalizationModProvider.Install(plan,tables,LocalizationSettings.AvailableLocales.Locales);
                foreach(var locale in modProvider.ExtraLocales)
                {
                    var metadata=new LocalizationLanguage{code=locale.Identifier.Code,nativeName=locale.LocaleName};
                    languages.Add(metadata.code,metadata);
                }
                source.languages=languages.Values.ToArray();
                Debug.Log("Загружены локальные моды перевода: "+string.Join(", ",plan.Packages.Select(p=>p.Id+"@"+p.Version))+". Игровой манифест не изменён.");
            }
            finally {foreach(var handle in handles)if(handle.IsValid())Addressables.Release(handle);}
        }
        public static string NormalizeCode(string code)=>code switch {"ua"=>"uk","jp"=>"ja","zh"=>"zh-Hans","tw"=>"zh-Hant",_=>code};
        private static void OnLocaleChanged(Locale locale)
        {
            if(manifest==null || locale==null)return;
            // Unity can finish a prior locale's preload callback after a newer selection.
            if(LocalizationSettings.SelectedLocale?.Identifier!=locale.Identifier)return;
            if(CurrentLanguage!=locale.Identifier.Code)currentTables=TablesFor(locale.Identifier.Code);
            CurrentLanguage=locale.Identifier.Code;warned.Clear();Changed?.Invoke();
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
        #if UNITY_EDITOR
        public static LocalizationLanguage LoadLanguage(string code)
        {
            var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Remains/Editor/LocalizationSources/"+code+".json");
            if(asset==null)throw new InvalidOperationException("Нет подготовленного языкового файла: "+code);
            var pack=Parse(asset.text,false);
            if(pack.languages.Length!=1 || pack.languages[0].code!=code)throw new InvalidOperationException("Не совпал язык файла: "+code);
            return pack.languages[0];
        }
        #endif
        public static void Select(string code)
        {
            Initialize();code=NormalizeCode(code);if(!languages.ContainsKey(code))throw new ArgumentException("Неизвестный язык: "+code);
            if(CurrentLanguage==code)return;
            Apply(code);PlayerPrefs.SetString(Preference,code);PlayerPrefs.Save();
        }
        private static void Apply(string code)
        {
            var locale=LocalizationSettings.AvailableLocales.GetLocale(code);
            if(locale==null)throw new InvalidOperationException("Не создан Locale Unity Localization: "+code);
            // Explicit locale selection preloads tables. UI refreshes do not initiate bulk work per frame.
            currentTables=code=="ru"?russianTables:TablesFor(code);
            CurrentLanguage=code;warned.Clear();LocalizationSettings.SelectedLocale=locale;
        }
        private static Dictionary<string,StringTable> TablesFor(string code)=>TablesForAsync(code,true).GetAwaiter().GetResult();
        private static async Task<Dictionary<string,StringTable>> TablesForAsync(string code,bool synchronous)
        {
            var locale=LocalizationSettings.AvailableLocales.GetLocale(code);
            if(locale==null)throw new InvalidOperationException("Не создан Locale Unity Localization: "+code);
            var result=new Dictionary<string,StringTable>(StringComparer.Ordinal);
            foreach(string name in new[]{OriginalTable,UiTable,ItemsTable})
            {
                var identity=(name,code);
                if(!retainedTables.TryGetValue(identity,out var handle))
                {
                    handle=LocalizationSettings.StringDatabase.GetTableAsync(name,locale);
                    Addressables.ResourceManager.Acquire(handle);
                    try
                    {
                        var loaded=await Complete(handle,synchronous);
                        if(loaded==null)throw new InvalidOperationException("Нет String Table: "+name+", locale="+code);
                        retainedTables.Add(identity,handle);
                    }
                    catch{Addressables.Release(handle);throw;}
                }
                if(!handle.IsValid() || handle.Result==null)
                    throw new InvalidOperationException("Утеряна удерживаемая String Table: "+name+", locale="+code);
                result.Add(name,handle.Result);
            }
            return result;
        }
        public static string Text(string key,string russian,string context)
        {
            Initialize();
            return Resolve(key.StartsWith("unity/",StringComparison.Ordinal)?UiTable:OriginalTable,key,russian,context);
        }
        public static string UiText(string key,string russian,string context)=>Resolve(UiTable,key,russian,context);
        public static string Format(string key,FormattableString source,string context)
        {
            Initialize();var result=currentTables[UiTable].GetEntry(key);
            if(result!=null && !string.IsNullOrEmpty(result.LocalizedValue))return result.GetLocalizedString(source.GetArguments());
            Warn(UiTable+"/"+key,context);
            var russian=russianTables[UiTable].GetEntry(key);
            return russian!=null?russian.GetLocalizedString(source.GetArguments()):source.ToString(System.Globalization.CultureInfo.GetCultureInfo("ru"));
        }
        private static string Resolve(string table,string key,string russian,string context)
        {
            Initialize();var result=currentTables[table].GetEntry(key);
            if(result!=null && !string.IsNullOrEmpty(result.LocalizedValue))return result.GetLocalizedString();
            Warn(table+"/"+key,context);
            if(CurrentLanguage!="ru")
            {
                var source=russianTables[table].GetEntry(key);
                if(source!=null)return source.GetLocalizedString();
            }
            return russian;
        }
        public static string TranslateRussian(string russian,string context)
        {
            Initialize();if(string.IsNullOrWhiteSpace(russian))return russian;
            if(byRussian.TryGetValue(russian,out var reference))return Resolve(reference.table,reference.key,russian,context);
            // IDs, numbers and user names are not a translation request.
            foreach(char c in russian)if(c>='А' && c<='я' || c is 'ё' or 'Ё'){if(CurrentLanguage!="ru")Warn(russian,context);break;}
            return russian;
        }
        public static string ItemName(string id,string russian,string context)
        {
            return Resolve(ItemsTable,id+"/name",russian,context);
        }
        public static string ItemDescription(string id,string russian,string context)=>Resolve(ItemsTable,id+"/description",russian,context);
        private static void Warn(string key,string context)
        {
            if(warned.Add(CurrentLanguage+"|"+key))Debug.LogWarning($"Нет однозначного перевода: language={CurrentLanguage}, key={key}, context={context}. Показан русский исходный текст.");
        }
    }
}
