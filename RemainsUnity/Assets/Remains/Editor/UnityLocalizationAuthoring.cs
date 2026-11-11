using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Remains.Editor
{
    // Uses the official editor API: table/shared entry identities survive subsequent imports.
    public static class UnityLocalizationAuthoring
    {
        public const string Root="Assets/Remains/Localization";
        public const string IndexPath="Assets/Remains/Resources/Content/localization-index.json";
        public const string Original="Remains.Original",Ui="Remains.UI",Items="Remains.Items";

        [MenuItem("Remains/Локализация/Перенести каталоги в Unity String Tables")]
        public static void Migrate()
        {
            Import(ReadPacks());
        }
        private static LocalizationLanguage[] ReadPacks()=>Directory.GetFiles(Path.ChangeExtension(LocalizationAuthoring.AssetPath,null),"*.json")
            .OrderBy(x=>x,StringComparer.Ordinal).SelectMany(path=>GameLocalization.Parse(File.ReadAllText(path),false).languages).ToArray();
        public static void RunBatch()
        {
            try {Migrate();Debug.Log("Unity String Tables: миграция завершена.");EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunChecksBatch()
        {
            try {RunChecks();Debug.Log("Unity Localization checks PASS: locales, tables, source index, reimport identities.");EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunChecks()
        {
            string[] codes={"ru","en","uk","zh-Hans","zh-Hant","pl","de","es","ja"};
            var locales=LocalizationEditorSettings.GetLocales();
            Require(codes.All(code=>locales.Count(l=>l.Identifier.Code==code)==1),"Нет одного из девяти исходных Locale.");
            Require(LocalizationEditorSettings.ActiveLocalizationSettings!=null,"Не заданы официальные LocalizationSettings.");
            var identities=Identities();
            var index=JObject.Parse(File.ReadAllText(IndexPath));
            Require((int?)index["schema"]==1,"Неверная схема индекса исходников.");
            var russianKeys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var reference in (JArray)index["entries"])
            {
                string text=(string)reference["russian"],name=(string)reference["table"],key=(string)reference["key"];
                Require(russianKeys.Add(text),"Повторный русский исходник в индексе: "+text);
                var table=(StringTable)LocalizationEditorSettings.GetStringTableCollection(name)?.GetTable("ru");
                Require(table?.GetEntry(key)?.LocalizedValue==text,"Индекс не соответствует русской String Table: "+name+"/"+key);
            }
            foreach(string name in new[]{Original,Ui,Items})
            {
                var collection=LocalizationEditorSettings.GetStringTableCollection(name);
                Require(collection!=null && collection.SharedData.Entries.Count>0,"Пустая/отсутствующая коллекция: "+name);
                Require(codes.All(code=>collection.GetTable(code)!=null),"Не все девять таблиц присутствуют в "+name);
            }
            var original=LocalizationEditorSettings.GetStringTableCollection(Original);
            Require(original.StringTables.All(table=>table.Values.All(entry=>!entry.IsSmart)),"Исходные @/# тексты ошибочно помечены Smart String.");
            var packs=ReadPacks();
            var translations=packs.ToDictionary(p=>p.code,p=>p.entries.ToDictionary(e=>e.key,e=>e.text,StringComparer.Ordinal),StringComparer.Ordinal);
            foreach(var source in RegisteredSources())
            {
                var ui=(StringTable)LocalizationEditorSettings.GetStringTableCollection(Ui).GetTable("ru");
                Require(ui.GetEntry((string)source["key"])?.LocalizedValue==(string)source["russian"],"Нет зарегистрированной подписи: "+source["key"]);
                if((string)source["kind"]!="text")continue;
                var legacy=FindLegacySource(packs,(string)source["russian"],translations);
                if(legacy==null)continue;
                foreach(var pack in packs.Where(p=>p.code!="ru"))
                    if(translations[pack.code].TryGetValue(legacy.key,out string expected))
                    {
                        var table=(StringTable)LocalizationEditorSettings.GetStringTableCollection(Ui).GetTable(LocaleCode(pack.code));
                        Require(table.GetEntry((string)source["key"])?.LocalizedValue==expected,"У подписи потерян существующий перевод: "+pack.code+"/"+source["key"]);
                    }
            }
            Migrate();
            var after=Identities();
            Require(identities.Count==after.Count && identities.All(pair=>after.TryGetValue(pair.Key,out string id)&&id==pair.Value),"Повторный импорт изменил GUID или shared entry ID.");
        }
        private static Dictionary<string,string> Identities()
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string name in new[]{Original,Ui,Items})
            {
                var collection=LocalizationEditorSettings.GetStringTableCollection(name);
                Require(collection!=null,"Нет коллекции: "+name);
                result.Add(name,AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(collection.SharedData)));
                foreach(var entry in collection.SharedData.Entries)result.Add(name+"/"+entry.Key,entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach(var table in collection.StringTables)result.Add(name+"@"+table.LocaleIdentifier.Code,AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(table)));
            }
            return result;
        }
        private static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException(message);}
        public static string LocaleCode(string code)=>code switch {"ua"=>"uk","jp"=>"ja","zh"=>"zh-Hans","tw"=>"zh-Hant",_=>code};

        public static void Import(LocalizationLanguage[] packs)
        {
            if(packs==null || packs.Length==0 || packs.Count(p=>p.code=="ru")!=1)
                throw new InvalidDataException("Для String Tables требуется один исходный русский каталог.");
            var sources=RegisteredSources().Concat(LocalizationPrefabAuthoring.Collect()).ToArray();
            Directory.CreateDirectory(Root+"/Locales");
            Directory.CreateDirectory(Root+"/Tables");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var settings=LocalizationEditorSettings.ActiveLocalizationSettings;
            if(settings==null)
            {
                settings=ScriptableObject.CreateInstance<LocalizationSettings>();
                AssetDatabase.CreateAsset(settings,Root+"/LocalizationSettings.asset");
                LocalizationEditorSettings.ActiveLocalizationSettings=settings;
            }
            LocalizationSettings.Instance=settings;
            var locales=new List<Locale>();
            foreach(var pack in packs)
            {
                string code=LocaleCode(pack.code);
                var locale=LocalizationEditorSettings.GetLocale(code);
                if(locale==null)
                {
                    locale=Locale.CreateLocale(code);
                    AssetDatabase.CreateAsset(locale,Root+"/Locales/"+code+".asset");
                    LocalizationEditorSettings.AddLocale(locale);
                }
                locale.LocaleName=pack.nativeName;
                EditorUtility.SetDirty(locale);locales.Add(locale);
            }
            LocalizationSettings.ProjectLocale=locales.Single(x=>x.Identifier.Code=="ru");
            LocalizationSettings.InitializeSynchronously=true;
            settings.GetStartupLocaleSelectors().Clear();
            settings.GetStartupLocaleSelectors().Add(new SpecificLocaleSelector{LocaleId="ru"});
            // Selection is explicitly restored by GameLocalization, Russian is the source locale.
            EditorUtility.SetDirty(settings);
            var collections=new[]{Original,Ui,Items}.ToDictionary(name=>name,name=>Collection(name,locales),StringComparer.Ordinal);
            var russianItems=ReadRussianItems();
            foreach(var pack in packs)
            {
                string code=LocaleCode(pack.code);
                var original=(StringTable)collections[Original].GetTable(code);
                var ui=(StringTable)collections[Ui].GetTable(code);
                var items=(StringTable)collections[Items].GetTable(code);
                foreach(var entry in pack.entries)
                    (entry.key.StartsWith("unity/",StringComparison.Ordinal)?ui:original).AddEntry(entry.key,entry.text);
                foreach(var item in pack.items)items.AddEntry(item.id+"/name",item.name);
                if(code=="ru")foreach(var item in russianItems.Values)
                {items.AddEntry(item.id+"/name",item.name);items.AddEntry(item.id+"/description",item.description);}
                foreach(var table in new[]{original,ui,items})EditorUtility.SetDirty(table);
            }
            var russianUi=(StringTable)collections[Ui].GetTable("ru");
            var legacyTranslations=packs.ToDictionary(p=>p.code,p=>p.entries.ToDictionary(e=>e.key,e=>e.text,StringComparer.Ordinal),StringComparer.Ordinal);
            foreach(var source in sources)
            {
                var entry=russianUi.AddEntry((string)source["key"],(string)source["russian"]);
                // Scanner templates are .NET composite formats; Unity formats their typed arguments.
                entry.IsSmart=false;
                if((string)source["kind"]!="text")continue;
                var legacy=FindLegacySource(packs,(string)source["russian"],legacyTranslations);
                if(legacy==null)continue;
                foreach(var pack in packs.Where(p=>p.code!="ru"))
                {
                    if(!legacyTranslations[pack.code].TryGetValue(legacy.key,out string translated))continue;
                    var table=(StringTable)collections[Ui].GetTable(LocaleCode(pack.code));
                    table.AddEntry((string)source["key"],translated).IsSmart=false;
                    EditorUtility.SetDirty(table);
                }
            }
            EditorUtility.SetDirty(russianUi);
            foreach(var collection in collections.Values)
            {collection.SetPreloadTableFlag(true);EditorUtility.SetDirty(collection.SharedData);EditorUtility.SetDirty(collection);}
            var addressables=AddressableAssetSettingsDefaultObject.GetSettings(true);
            addressables.BuildAddressablesWithPlayerBuild=AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            EditorUtility.SetDirty(addressables);
            WriteIndex(packs,sources);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Unity Localization: языков={packs.Length}, коллекций={collections.Count}, исходный=ru; Addressables включён в Player build.");
        }
        private static StringTableCollection Collection(string name,IList<Locale> locales)
        {
            var collection=LocalizationEditorSettings.GetStringTableCollection(name);
            if(collection==null)collection=LocalizationEditorSettings.CreateStringTableCollection(name,Root+"/Tables",locales);
            else foreach(var locale in locales)if(collection.GetTable(locale.Identifier)==null)collection.AddNewTable(locale.Identifier);
            return collection;
        }
        private static Dictionary<string,ItemTextDefinition> ReadRussianItems()
        {
            var result=new Dictionary<string,ItemTextDefinition>(StringComparer.Ordinal);
            foreach(string resource in new[]{ItemTextCatalog.OriginalResource,ItemTextCatalog.Resource})
            {
                string path="Assets/Remains/Resources/"+resource+".json";
                var manifest=JsonConvert.DeserializeObject<ItemTextManifest>(File.ReadAllText(path));
                if(manifest?.schema!=1 || manifest.items==null)throw new InvalidDataException("Некорректный русский каталог предметов: "+path);
                foreach(var item in manifest.items)result[item.id]=item;
            }
            return result;
        }
        private static void WriteIndex(LocalizationLanguage[] packs,JObject[] sources)
        {
            var russian=packs.Single(p=>p.code=="ru");
            var candidates=russian.entries.Where(e=>!string.IsNullOrWhiteSpace(e.text)).GroupBy(e=>e.text,StringComparer.Ordinal);
            var translated=packs.ToDictionary(p=>p.code,p=>p.entries.ToDictionary(e=>e.key,e=>e.text,StringComparer.Ordinal),StringComparer.Ordinal);
            var entries=new JArray();
            foreach(var group in candidates)
            {
                var selected=FindLegacySource(packs,group.Key,translated);
                if(selected==null)continue;
                entries.Add(new JObject{["russian"]=group.Key,["table"]=selected.key.StartsWith("unity/",StringComparison.Ordinal)?Ui:Original,["key"]=selected.key});
            }
            var indexed=entries.Select(e=>(string)e["russian"]).ToHashSet(StringComparer.Ordinal);
            foreach(var source in sources)
                if((string)source["kind"]=="text" && indexed.Add((string)source["russian"]))
                    entries.Add(new JObject{["russian"]=(string)source["russian"],["table"]=Ui,["key"]=(string)source["key"]});
            var index=new JObject{["schema"]=1,["languages"]=new JArray(packs.Select(p=>new JObject{["code"]=LocaleCode(p.code),["nativeName"]=p.nativeName,["sourceSha256"]=p.sourceSha256})),["entries"]=entries};
            File.WriteAllText(IndexPath,index.ToString(Formatting.Indented),new UTF8Encoding(false));
        }
        private static JObject[] RegisteredSources()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/LocalizationSourceScan/source-manifest.json"));
            var manifest=JObject.Parse(File.ReadAllText(path));
            if((int?)manifest["schemaVersion"]!=1 || (string)manifest["sourceLanguage"]!="ru" || manifest["entries"] is not JArray entries)
                throw new InvalidDataException("Некорректный реестр русских подписей: "+path);
            var registered=entries.OfType<JObject>().Where(e=>(bool?)e["registered"]==true).ToArray();
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in registered)
                if(string.IsNullOrWhiteSpace((string)entry["key"]) || entry["russian"]?.Type!=JTokenType.String || !keys.Add((string)entry["key"]))
                    throw new InvalidDataException("Пустой/повторный зарегистрированный ключ подписи: "+entry["key"]);
            return registered;
        }
        private static LocalizationEntry FindLegacySource(LocalizationLanguage[] packs,string russian,Dictionary<string,Dictionary<string,string>> translated)
        {
            var candidates=packs.Single(p=>p.code=="ru").entries.Where(e=>e.text==russian).ToArray();
            if(candidates.Length==0)return null;
            var priority=candidates.FirstOrDefault(e=>e.key.StartsWith("unity/",StringComparison.Ordinal));
            if(priority!=null)return priority;
            // A repeated Russian label is reusable only when every locale agrees on its meaning.
            if(packs.Any(p=>candidates.Select(e=>translated[p.code].TryGetValue(e.key,out string text)?text:null).Distinct(StringComparer.Ordinal).Count()>1))return null;
            return candidates[0];
        }
    }
}
