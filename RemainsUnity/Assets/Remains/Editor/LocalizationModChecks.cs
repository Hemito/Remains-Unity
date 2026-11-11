using System;
using System.IO;
using Remains.Content;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class LocalizationModChecks
    {
        #if UNITY_5_3_OR_NEWER
        public static void RunUnity()
        {
            var assets=new System.Collections.Generic.List<UnityEngine.Object>();
            var bases=new System.Collections.Generic.List<UnityEngine.Localization.Tables.StringTable>();
            var locales=new System.Collections.Generic.List<UnityEngine.Localization.Locale>();
            try
            {
                var russian=UnityEngine.Localization.Locale.CreateLocale("ru");assets.Add(russian);locales.Add(russian);
                foreach(string name in new[]{"Remains.Original","Remains.Items","Remains.UI"})
                {
                    var keys=ScriptableObject.CreateInstance<UnityEngine.Localization.Tables.SharedTableData>();assets.Add(keys);keys.TableCollectionName=name;
                    var table=ScriptableObject.CreateInstance<UnityEngine.Localization.Tables.StringTable>();assets.Add(table);table.SharedData=keys;table.LocaleIdentifier="ru";table.AddEntry("check.key","Исходник");bases.Add(table);
                }
                var patch=Package("check:patch");patch.strings[0].replace=true;patch.strings[0].replaces="base";
                patch.locales=new[]{new LocalizationModLocaleEntry{code="fr",nativeName="Français"}};
                using(var provider=UnityLocalizationModProvider.Install(LocalizationMods.ValidatePackages(new[]{patch}),bases,locales))
                {
                    Require(bases[2].GetEntry("check.key").LocalizedValue=="Исходник","изменён базовый ассет");
                    Require(locales.Count==2,"новый язык не опубликован");
                    foreach(string name in new[]{"Remains.Original","Remains.Items","Remains.UI"})
                    {
                        var empty=provider.ProvideTableAsync<UnityEngine.Localization.Tables.StringTable>(name,locales[1]);
                        try{Require(empty.IsValid() && empty.Result!=null && empty.Result.GetEntry("check.key")==null,"для нового языка не создана пустая таблица, позволяющая русский fallback");}
                        finally{if(empty.IsValid())UnityEngine.AddressableAssets.Addressables.Release(empty);}
                    }
                    var operation=provider.ProvideTableAsync<UnityEngine.Localization.Tables.StringTable>("Remains.UI",russian);
                    try{Require(operation.Result.GetEntry("check.key").LocalizedValue=="Проверка","замена не опубликована");}
                    finally{UnityEngine.AddressableAssets.Addressables.Release(operation);}
                }
                Require(locales.Count==1,"язык не очищен при освобождении");
                var wrongSource=Package("check:wrong-source");wrongSource.strings[0].key="absent";wrongSource.strings[0].replace=true;wrongSource.strings[0].replaces="base";
                Reject(()=>UnityLocalizationModProvider.Install(LocalizationMods.ValidatePackages(new[]{wrongSource}),bases,locales),"check:wrong-source");
                Reject(()=>UnityLocalizationModProvider.Install(LocalizationMods.ValidatePackages(new[]{Package("check:collision")}),bases,locales),"check:collision");
                var noRussian=Package("check:no-russian");noRussian.strings[0].key="new.key";noRussian.strings[0].locale="en";
                noRussian.locales=new[]{new LocalizationModLocaleEntry{code="en",nativeName="English"}};
                Reject(()=>UnityLocalizationModProvider.Install(LocalizationMods.ValidatePackages(new[]{noRussian}),bases,locales),"русского");
                Require(locales.Count==1 && UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.TableProvider==null,"неудачная установка оставила частичный результат");
                Debug.Log("LocalizationModChecks Unity PASS: клонирование базы, явная замена base, новый язык, откат конфликтов и ключей без русского исходника.");
            }
            finally{foreach(var asset in assets)if(asset!=null)UnityEngine.Object.DestroyImmediate(asset);}
        }
        #endif
        public static void Run()
        {
            string temp=Path.GetFullPath(Path.GetTempPath()),root=Path.Combine(temp,"r-loc-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Require(LocalizationMods.Load(root).Packages.Count==0,"отсутствующий список не пуст");
                var first=Package("check:first");var second=Package("check:second");
                second.dependencies=new[]{first.id};second.strings[0].replace=true;second.strings[0].replaces=first.id;
                var plan=LocalizationMods.ValidatePackages(new[]{first,second});
                first.strings[0].value="Изменено после проверки";
                Require(plan.Packages[0].Strings[0].Value=="Проверка","план зависит от изменяемого DTO");
                Require(plan.Packages[1].Strings[0].Replaces=="check:first","потерян источник замены");
                Reject(()=>LocalizationMods.ValidatePackages(new[]{Package("check:first"),Package("check:second")}),"check:second");
                second.strings[0].replaces="other";Reject(()=>LocalizationMods.ValidatePackages(new[]{Package("check:first"),second}),"источник");
                second.dependencies=new[]{"missing"};Reject(()=>LocalizationMods.ValidatePackages(new[]{second}),"зависимость");
                foreach(string code in new[]{"ua","jp","tw","zh","EN",""})
                {var bad=Package("check:bad");bad.strings[0].locale=code;Reject(()=>LocalizationMods.ValidatePackages(new[]{bad}),"check:bad");}
                var french=Package("check:french");french.locales=new[]{new LocalizationModLocaleEntry{code="fr",nativeName="Français"}};
                french.strings[0].locale="fr";Require(LocalizationMods.ValidatePackages(new[]{french}).Packages.Count==1,"новый язык отклонён");
                french.locales=null;Reject(()=>LocalizationMods.ValidatePackages(new[]{french}),"locale");
                var duplicate=Package("check:duplicate");duplicate.strings=new[]{duplicate.strings[0],duplicate.strings[0]};
                Reject(()=>LocalizationMods.ValidatePackages(new[]{duplicate}),"Повтор");
                var wrongTable=Package("check:bad");wrongTable.strings[0].table="other.Table";
                Reject(()=>LocalizationMods.ValidatePackages(new[]{wrongTable}),"пространство");
                var basePatch=Package("check:base");basePatch.strings[0].replace=true;basePatch.strings[0].replaces="base";
                Require(LocalizationMods.ValidatePackages(new[]{basePatch}).Packages.Count==1,"план замены базы отклонён");
                Directory.CreateDirectory(Path.Combine(root,"first"));File.WriteAllText(Path.Combine(root,"first","manifest.json"),StateJson.ToJson(Package("check:first")));
                WriteOrder(root,new LocalizationModOrderEntry{id="check:disabled",path="absent",enabled=false},new LocalizationModOrderEntry{id="check:first",path="first",enabled=true});
                Require(LocalizationMods.Load(root).Packages[0].Position==2,"потеряна позиция после выключенного пакета");
                foreach(string path in new[]{"../outside",Path.GetPathRoot(root),""})
                {WriteOrder(root,new LocalizationModOrderEntry{id="check:first",path=path,enabled=true});Reject(()=>LocalizationMods.Load(root),"мод");}
                WriteOrder(root,new LocalizationModOrderEntry{id="check:first",path="first"});Reject(()=>LocalizationMods.Load(root),"enabled");
                WriteOrder(root,new LocalizationModOrderEntry{id="check:first",path="missing",enabled=true});Reject(()=>LocalizationMods.Load(root),"check:first");
                WriteOrder(root,new LocalizationModOrderEntry{id="check:wrong",path="first",enabled=true});Reject(()=>LocalizationMods.Load(root),"check:wrong");
                WriteOrder(root,new LocalizationModOrderEntry{id="check:first",path="first",enabled=true},new LocalizationModOrderEntry{id="check:alias",path="first/../first",enabled=false});
                Reject(()=>LocalizationMods.Load(root),"Повтор");
                Debug.Log("LocalizationModChecks PASS: неизменяемый план, порядок и выключенные пакеты, зависимости, явные источники замен, новые языки, отказы конфликтов/алиасов/путей/повреждённых включённых пакетов. Коллизии с базовыми Unity-таблицами проверяет применяющий сервис.");
            }
            finally
            {
                string absolute=Path.GetFullPath(root);
                if(!absolute.StartsWith(temp.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("r-loc-",StringComparison.Ordinal))
                    throw new InvalidOperationException("Небезопасный путь очистки проверки локализации.");
                Directory.Delete(absolute,true);
            }
        }
        private static LocalizationModManifest Package(string id)=>new(){schema=1,id=id,version="1",strings=new[]{new LocalizationModStringEntry{table="Remains.UI",key="check.key",locale="ru",value="Проверка"}}};
        private static void WriteOrder(string root,params LocalizationModOrderEntry[] mods)=>File.WriteAllText(Path.Combine(root,"load-order.json"),StateJson.ToJson(new LocalizationModOrder{schema=1,mods=mods}));
        private static void Require(bool success,string reason){if(!success)throw new InvalidOperationException("Моды локализации: "+reason);}
        private static void Reject(Action action,string context)
        {
            try{action();}catch(InvalidDataException error){Require(error.Message.Contains(context),"недостаточно контекста ошибки: "+error.Message);return;}
            throw new InvalidOperationException("Ошибочный пакет локализации принят: "+context);
        }
    }
}

