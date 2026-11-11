using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Remains.Serialization;

namespace Remains.Content
{
    [Serializable] public sealed class LocalizationModOrder {public int schema;public LocalizationModOrderEntry[] mods;}
    [Serializable] public sealed class LocalizationModOrderEntry {public string id,path;public bool? enabled;}
    [Serializable] public sealed class LocalizationModManifest
    {public int schema;public string id,version;public string[] dependencies;public LocalizationModLocaleEntry[] locales;public LocalizationModStringEntry[] strings;}
    [Serializable] public sealed class LocalizationModLocaleEntry {public string code,nativeName;}
    [Serializable] public sealed class LocalizationModStringEntry {public string table,key,locale,value;public bool replace;public string replaces;}

    public sealed class LocalizationModLocale
    {
        public string Code {get;} public string NativeName {get;}
        internal LocalizationModLocale(LocalizationModLocaleEntry value){Code=value.code;NativeName=value.nativeName;}
    }
    public sealed class LocalizationModString
    {
        public string Table {get;} public string Key {get;} public string Locale {get;} public string Value {get;}
        public bool Replace {get;} public string Replaces {get;}
        internal LocalizationModString(LocalizationModStringEntry value)
        {Table=value.table;Key=value.key;Locale=value.locale;Value=value.value;Replace=value.replace;Replaces=value.replaces;}
    }
    public sealed class LocalizationModPackage
    {
        public string Id {get;} public string Version {get;} public int Position {get;}
        public IReadOnlyList<string> Dependencies {get;}
        public IReadOnlyList<LocalizationModLocale> Locales {get;}
        public IReadOnlyList<LocalizationModString> Strings {get;}
        internal LocalizationModPackage(LocalizationModManifest source,int position)
        {
            Id=source.id;Version=source.version;Position=position;
            Dependencies=Array.AsReadOnly((source.dependencies??Array.Empty<string>()).ToArray());
            Locales=Array.AsReadOnly((source.locales??Array.Empty<LocalizationModLocaleEntry>()).Select(x=>new LocalizationModLocale(x)).ToArray());
            Strings=Array.AsReadOnly(source.strings.Select(x=>new LocalizationModString(x)).ToArray());
        }
    }
    public sealed class LocalizationModPlan
    {
        public IReadOnlyList<LocalizationModPackage> Packages {get;}
        internal LocalizationModPlan(IEnumerable<LocalizationModPackage> packages){Packages=Array.AsReadOnly(packages.ToArray());}
    }

    // Presentation-only startup input. Deliberately separate from WeaponCatalogLoader and its gameplay fingerprint.
    // Returns an immutable, fully validated plan; Unity table registration and base collisions are checked by the caller.
    public static class LocalizationMods
    {
        public const string Folder="LocalizationMods";
        private static readonly Regex IdPattern=new(@"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",RegexOptions.CultureInvariant);
        private static readonly Regex LocalePattern=new(@"^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$",RegexOptions.CultureInvariant);
        private static readonly HashSet<string> BaseTables=new(new[]{"Remains.Original","Remains.Items","Remains.UI"},StringComparer.Ordinal);
        private static readonly string[] BaseLocales={"ru","en","uk","zh-Hans","zh-Hant","pl","de","es","ja"};

        public static LocalizationModPlan Load(string root)
        {
            if(string.IsNullOrWhiteSpace(root))throw new ArgumentException("Не задана папка модов локализации.",nameof(root));
            root=Path.GetFullPath(root);string orderPath=Path.Combine(root,"load-order.json");
            if(!File.Exists(orderPath))return new LocalizationModPlan(Array.Empty<LocalizationModPackage>()); // Optional, explicitly absent package list.
            try
            {
                var order=StateJson.FromJson<LocalizationModOrder>(Read(orderPath));
                if(order?.schema!=1 || order.mods==null)throw new InvalidDataException("В load-order.json нужны schema=1 и массив mods.");
                var ids=new HashSet<string>(StringComparer.Ordinal);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var manifests=new List<LocalizationModManifest>();var positions=new List<int>();
                for(int index=0;index<order.mods.Length;index++)
                {
                    var declaration=order.mods[index];
                    if(declaration==null || !ValidId(declaration.id) || !ids.Add(declaration.id) || declaration.enabled==null)
                        throw new InvalidDataException($"Некорректная/повторная запись load-order: позиция={index+1}, id={declaration?.id}; enabled должен быть задан явно.");
                    string directory;
                    try
                    {
                        directory=Inside(root,declaration.path);
                        if(!paths.Add(directory))throw new InvalidDataException("Повтор папки мода локализации: "+declaration.path);
                    }
                    catch(Exception failure) when(failure is InvalidDataException || failure is IOException || failure is ArgumentException)
                    {throw new InvalidDataException($"Некорректный путь мода локализации: id={declaration.id}, позиция={index+1}, папка={declaration.path}. {failure.Message}",failure);}
                    if(!declaration.enabled.Value)continue;
                    try
                    {
                        var manifest=StateJson.FromJson<LocalizationModManifest>(Read(Path.Combine(directory,"manifest.json")));
                        if(manifest?.id!=declaration.id)throw new InvalidDataException("ID manifest.json не совпадает с load-order: "+manifest?.id);
                        manifests.Add(manifest);positions.Add(index+1);
                    }
                    catch(Exception failure) when(failure is InvalidDataException || failure is IOException || failure is ArgumentException || failure is Newtonsoft.Json.JsonException)
                    {throw new InvalidDataException($"Не прочитан включённый мод локализации: id={declaration.id}, позиция={index+1}, папка={declaration.path}. {failure.Message}",failure);}
                }
                return Validate(manifests,positions);
            }
            catch(Exception failure) when(failure is InvalidDataException || failure is IOException || failure is ArgumentException || failure is Newtonsoft.Json.JsonException)
            {throw new InvalidDataException("Не загружены моды локализации: "+failure.Message,failure);}
        }
        // The same validation boundary is available to trusted code mods; source DTOs are copied on acceptance.
        public static LocalizationModPlan ValidatePackages(IEnumerable<LocalizationModManifest> manifests)
        {
            if(manifests==null)throw new ArgumentNullException(nameof(manifests));
            var values=manifests.ToArray();return Validate(values,Enumerable.Range(1,values.Length).ToArray());
        }
        public static bool ValidLocaleCode(string code)=>code!=null && LocalePattern.IsMatch(code) && code is not ("ua" or "jp" or "tw" or "zh");
        private static bool ValidId(string id)=>id!=null && id!="base" && IdPattern.IsMatch(id);
        private static LocalizationModPlan Validate(IReadOnlyList<LocalizationModManifest> manifests,IReadOnlyList<int> positions)
        {
            var accepted=new List<LocalizationModPackage>();var sources=new HashSet<string>(StringComparer.Ordinal);
            var locales=new HashSet<string>(BaseLocales,StringComparer.Ordinal);
            var localeNames=new Dictionary<string,(string Name,string Source)>(StringComparer.Ordinal);
            var owners=new Dictionary<(string Table,string Locale,string Key),string>();
            for(int index=0;index<manifests.Count;index++)
            {
                var manifest=manifests[index];int position=positions[index];
                try
                {
                    if(manifest?.schema!=1 || !ValidId(manifest.id) || sources.Contains(manifest.id) || string.IsNullOrWhiteSpace(manifest.version) || manifest.strings==null)
                        throw new InvalidDataException("Нужны schema=1, уникальный id, версия и массив strings.");
                    var dependencies=new HashSet<string>(StringComparer.Ordinal);
                    foreach(string dependency in manifest.dependencies??Array.Empty<string>())
                        if(!ValidId(dependency) || !dependencies.Add(dependency) || !sources.Contains(dependency))
                            throw new InvalidDataException("Не загружена более ранняя/уникальная зависимость: "+dependency);
                    var ownLocales=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var locale in manifest.locales??Array.Empty<LocalizationModLocaleEntry>())
                    {
                        if(locale==null || !ValidLocaleCode(locale.code) || string.IsNullOrWhiteSpace(locale.nativeName) || !ownLocales.Add(locale.code))
                            throw new InvalidDataException("Некорректный/повторный locale; используйте стандартные uk/ja/zh-Hans/zh-Hant: "+locale?.code);
                        if(localeNames.TryGetValue(locale.code,out var previous) && previous.Name!=locale.nativeName)
                            throw new InvalidDataException($"Конфликт имени locale={locale.code}: источник={previous.Source}, имя={previous.Name}, новое={locale.nativeName}.");
                        localeNames[locale.code]=(locale.nativeName,manifest.id);locales.Add(locale.code);
                    }
                    var ownKeys=new HashSet<(string Table,string Locale,string Key)>();
                    foreach(var entry in manifest.strings)
                    {
                        if(entry==null || string.IsNullOrWhiteSpace(entry.table) || string.IsNullOrWhiteSpace(entry.key) || entry.key!=entry.key.Trim() || string.IsNullOrWhiteSpace(entry.value) || !ValidLocaleCode(entry.locale) || !locales.Contains(entry.locale))
                            throw new InvalidDataException($"Некорректная строка: table={entry?.table}, locale={entry?.locale}, key={entry?.key}; новый locale должен быть объявлен в locales.");
                        if(!BaseTables.Contains(entry.table) && !entry.table.StartsWith(manifest.id+".",StringComparison.Ordinal) && !dependencies.Any(id=>entry.table.StartsWith(id+".",StringComparison.Ordinal)))
                            throw new InvalidDataException("Не разрешено пространство таблицы: "+entry.table);
                        var key=(entry.table,entry.locale,entry.key);
                        if(!ownKeys.Add(key))throw new InvalidDataException($"Повтор строки внутри пакета: table={entry.table}, locale={entry.locale}, key={entry.key}.");
                        if(!entry.replace && entry.replaces!=null || entry.replace && string.IsNullOrWhiteSpace(entry.replaces))
                            throw new InvalidDataException($"Несогласованные replace/replaces: table={entry.table}, locale={entry.locale}, key={entry.key}.");
                        if(owners.TryGetValue(key,out string previousSource))
                        {
                            if(!entry.replace || entry.replaces!=previousSource)
                                throw new InvalidDataException($"Конфликт перевода: table={entry.table}, locale={entry.locale}, key={entry.key}, текущий источник={previousSource}, объявлен={entry.replaces??"не указан"}.");
                        }
                        else if(entry.replace && entry.replaces!="base")
                            throw new InvalidDataException($"Замена отсутствующей модовой строки: table={entry.table}, locale={entry.locale}, key={entry.key}, источник={entry.replaces}.");
                        owners[key]=manifest.id;
                    }
                    sources.Add(manifest.id);accepted.Add(new LocalizationModPackage(manifest,position));
                }
                catch(Exception failure) when(failure is InvalidDataException || failure is ArgumentException)
                {throw new InvalidDataException($"Некорректный мод локализации: id={manifest?.id}, позиция={position}. {failure.Message}",failure);}
            }
            return new LocalizationModPlan(accepted);
        }
        private static string Inside(string root,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))throw new InvalidDataException("Папка мода должна быть относительной: "+relative);
            string absolute=Path.GetFullPath(Path.Combine(root,relative));
            if(!absolute.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Папка мода локализации выходит за корень: "+relative);
            return absolute;
        }
        private static string Read(string file)=>new UTF8Encoding(false,true).GetString(File.ReadAllBytes(file)).TrimStart('\uFEFF');
    }
}

