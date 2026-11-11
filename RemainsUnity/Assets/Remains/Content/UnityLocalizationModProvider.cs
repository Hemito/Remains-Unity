using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Remains.Content
{
    // Startup-only transaction. The caller owns Addressables handles for baseTables until this method returns.
    // Call after AvailableLocales preload, before initialization/any StringDatabase table request.
    public sealed class UnityLocalizationModProvider : ITableProvider, IDisposable
    {
        private readonly Dictionary<(string Table,string Locale),StringTable> tables=new();
        private readonly List<UnityEngine.Object> owned=new();
        private readonly List<Locale> addedLocales=new();
        private readonly IList<Locale> available;
        private bool installed;
        public IReadOnlyList<Locale> ExtraLocales=>addedLocales.AsReadOnly();
        private UnityLocalizationModProvider(IList<Locale> locales){available=locales;}

        public static UnityLocalizationModProvider Install(LocalizationModPlan plan,IEnumerable<StringTable> baseTables,IList<Locale> locales)
        {
            if(plan==null || baseTables==null || locales==null)throw new ArgumentNullException("Не переданы план, базовые таблицы или языки локализации.");
            if(LocalizationSettings.StringDatabase.TableProvider!=null)throw new InvalidOperationException("Поставщик таблиц локализации уже установлен; нельзя молча заменить его.");
            var result=new UnityLocalizationModProvider(locales);
            try
            {
                result.Prepare(plan,baseTables);
                foreach(var locale in result.addedLocales)locales.Add(locale);
                LocalizationSettings.StringDatabase.TableProvider=result;result.installed=true;
                return result;
            }
            catch{result.Dispose();throw;}
        }

        private void Prepare(LocalizationModPlan plan,IEnumerable<StringTable> baseTables)
        {
            var shared=new Dictionary<string,SharedTableData>(StringComparer.Ordinal);
            var owners=new Dictionary<(string Table,string Locale,string Key),string>();
            var originalKeys=new HashSet<(string Table,string Key)>();
            foreach(var source in baseTables)
            {
                if(source==null || source.SharedData==null || !LocalizationMods.ValidLocaleCode(source.LocaleIdentifier.Code))
                    throw new InvalidDataException("Повреждена базовая Unity-таблица локализации.");
                string name=source.TableCollectionName,code=source.LocaleIdentifier.Code;
                if(tables.ContainsKey((name,code)))throw new InvalidDataException($"Повтор базовой таблицы: {name}/{code}.");
                if(!shared.TryGetValue(name,out var keys))
                {
                    keys=UnityEngine.Object.Instantiate(source.SharedData);owned.Add(keys);
                    // Runtime cloning must rebuild nonserialized GUID/key caches explicitly.
                    keys.OnAfterDeserialize();
                    if(keys.TableCollectionNameGuid!=source.SharedData.TableCollectionNameGuid)
                        throw new InvalidDataException("Копия потеряла GUID коллекции: "+name);
                    shared.Add(name,keys);
                }
                else if(keys.TableCollectionNameGuid!=source.SharedData.TableCollectionNameGuid)
                    throw new InvalidDataException("Разные GUID базовой коллекции: "+name);
                var clone=UnityEngine.Object.Instantiate(source);owned.Add(clone);clone.SharedData=keys;
                // Rebuild StringTableEntry objects against this clone, rather than retaining
                // managed lookup entries/format caches originating from another locale.
                clone.OnAfterDeserialize();
                if(clone.LocaleIdentifier.Code!=code || clone.Count!=source.Count)
                    throw new InvalidDataException($"Копия потеряла язык/записи: {name}/{code}.");
                tables.Add((name,code),clone);
                foreach(var entry in source.Values)
                {
                    if(string.IsNullOrEmpty(entry.Key))throw new InvalidDataException("У базовой записи отсутствует стабильный ключ: "+name);
                    var copied=clone.GetEntry(entry.Key);
                    if(copied==null || copied.LocalizedValue!=entry.LocalizedValue || copied.Table!=clone)
                        throw new InvalidDataException($"Копия изменила запись: {name}/{code}/{entry.Key}.");
                    owners.Add((name,code,entry.Key),"base");originalKeys.Add((name,entry.Key));
                }
            }
            foreach(string required in new[]{"Remains.Original","Remains.Items","Remains.UI"})
                if(!tables.ContainsKey((required,"ru")))throw new InvalidDataException("Не передана русская базовая таблица: "+required);
            var codes=new HashSet<string>(available.Select(x=>x.Identifier.Code),StringComparer.Ordinal);
            foreach(var package in plan.Packages)
                foreach(var declaration in package.Locales)
                    if(codes.Add(declaration.Code))
                    {var locale=Locale.CreateLocale(declaration.Code);locale.LocaleName=declaration.NativeName;owned.Add(locale);addedLocales.Add(locale);}
            var newKeys=new Dictionary<(string Table,string Key),string>();
            foreach(var package in plan.Packages)
                foreach(var entry in package.Strings)
                {
                    string context=$"id={package.Id}, позиция={package.Position}, table={entry.Table}, locale={entry.Locale}, key={entry.Key}";
                    if(!codes.Contains(entry.Locale))throw new InvalidDataException("Не загружен язык: "+context);
                    var identity=(entry.Table,entry.Locale,entry.Key);
                    bool exists=owners.TryGetValue(identity,out string previous);
                    if(exists ? !entry.Replace || entry.Replaces!=previous : entry.Replace)
                        throw new InvalidDataException($"Конфликт/отсутствующий источник замены: {context}, текущий={previous??"нет"}, заявлен={entry.Replaces??"нет"}.");
                    if(!shared.TryGetValue(entry.Table,out var keys))
                    {keys=NewSharedData(entry.Table);owned.Add(keys);shared.Add(entry.Table,keys);}
                    if(!tables.TryGetValue((entry.Table,entry.Locale),out var table))
                    {table=ScriptableObject.CreateInstance<StringTable>();table.SharedData=keys;table.LocaleIdentifier=entry.Locale;owned.Add(table);tables.Add((entry.Table,entry.Locale),table);}
                    table.AddEntry(entry.Key,entry.Value);owners[identity]=package.Id;
                    if(!originalKeys.Contains((entry.Table,entry.Key)))newKeys[(entry.Table,entry.Key)]=context;
                }
            foreach(var collection in shared)
                foreach(string code in codes)
                    if(!tables.ContainsKey((collection.Key,code)))
                    {
                        var empty=ScriptableObject.CreateInstance<StringTable>();empty.SharedData=collection.Value;empty.LocaleIdentifier=code;
                        owned.Add(empty);tables.Add((collection.Key,code),empty);
                    }
            foreach(var key in newKeys)
                if(!tables.TryGetValue((key.Key.Table,"ru"),out var russian) || string.IsNullOrWhiteSpace(russian.GetEntry(key.Key.Key)?.LocalizedValue))
                    throw new InvalidDataException($"У нового ключа мода нет русского исходника: {key.Value}.");
        }

        private static SharedTableData NewSharedData(string name)
        {
            var keys=ScriptableObject.CreateInstance<SharedTableData>();
            // SDK 1.5.14 exposes the GUID setter only internally. Use its serialized field,
            // then its public deserialization callback, so distinct runtime collections never share Guid.Empty.
            using(var sha=System.Security.Cryptography.SHA256.Create())
            {
                var digest=sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("Remains.Localization.Table:"+name));
                var bytes=new byte[16];Array.Copy(digest,bytes,16);
                string guid=new Guid(bytes).ToString("N");
                JsonUtility.FromJsonOverwrite("{\"m_TableCollectionNameGuidString\":\""+guid+"\"}",keys);
                keys.OnAfterDeserialize();
            }
            keys.TableCollectionName=name;return keys;
        }
        public AsyncOperationHandle<TTable> ProvideTableAsync<TTable>(string tableCollectionName,Locale locale) where TTable:LocalizationTable
        {
            if(typeof(TTable)!=typeof(StringTable) || locale==null || !tables.TryGetValue((tableCollectionName,locale.Identifier.Code),out var table))return default;
            return Addressables.ResourceManager.CreateCompletedOperation(table as TTable,null);
        }
        public void Dispose()
        {
            if(installed && ReferenceEquals(LocalizationSettings.StringDatabase.TableProvider,this))LocalizationSettings.StringDatabase.TableProvider=null;
            foreach(var locale in addedLocales)available.Remove(locale);
            foreach(var asset in owned)if(asset!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(asset);else UnityEngine.Object.DestroyImmediate(asset);
            }
            owned.Clear();tables.Clear();addedLocales.Clear();installed=false;
        }
    }
}



