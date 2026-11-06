using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class OriginalItemTextChecks
    {
        public static void RunBatch()
        {try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            CheckRules();WeaponCatalog.Load();
            var manual=GameAssets.Load<TextAsset>(ItemTextCatalog.Resource)??throw new InvalidOperationException("Нет ручных текстов предметов.");
            var original=GameAssets.Load<TextAsset>(ItemTextCatalog.OriginalResource)??throw new InvalidOperationException("Сначала выполните OriginalItemTextAuthoring.RunBatch.");
            ItemTextChecks.CheckText(manual.text);
            var warnings=new List<string>();var combined=new ItemTextCatalog(manual.text,warnings.Add);combined.ImportMissing(original.text);
            var manualEntries=StateJson.FromJson<ItemTextManifest>(manual.text).items;
            var importedEntries=StateJson.FromJson<ItemTextManifest>(original.text).items;
            Require(manualEntries.Length==91,"Изменён ручной набор91подписи.");
            foreach(var entry in manualEntries)
                Require(combined.Name(entry.id,"проверка ручных подписей")==entry.name && combined.Description(entry.id,"проверка ручных подписей")==entry.description,"Импорт заменил явный текст: "+entry.id);
            foreach(var entry in importedEntries)
            {
                Require(combined.Contains(entry.id),"Пропущен импортированный ID: "+entry.id);
                if(manualEntries.Any(e=>e.id==entry.id))continue;
                Require(combined.Name(entry.id,"проверка импорта")==entry.name && combined.Description(entry.id,"проверка импорта")==entry.description,"Каталог читает другие данные: "+entry.id);
            }
            var installed=ItemTextCatalog.Load();
            var inventoryIds=OriginalItemTextAuthoring.InventoryDefinitions(ExportedContentCatalog.Load());
            foreach(var weapon in WeaponCatalog.All.Where(w=>inventoryIds.Contains(w.id)))
                Require(installed.Contains(weapon.id),"Нет русского текста рабочего оружия: "+weapon.id);
            foreach(string npc in new[]{"turret1","turret2","turret3","turret4","turret5","turret6","turret7","robolaser","robospark"})
                Require(!inventoryIds.Contains("weapon:"+npc+":grade:0"),"Внутренний выстрел NPC попал в инвентарный каталог: "+npc);
            Require(combined.Name("pistol","проверка") == "10-мм пистолет" && combined.Name("p10","проверка") == "10-мм патроны","Изменена ручная терминология пистолета/патронов.");
            int formal=0;
            foreach(var item in ExportedContentCatalog.Load().items.Where(e=>e.category=="weapon" && !WeaponCatalog.Contains(e.key+":grade:0")))
                if(installed.Contains(item.key+":grade:0"))
                {
                    Require(installed.Description(item.key+":grade:0","проверка неподключённого оружия").Contains("Специальное действие пока не реализовано"),"Описание обещает неподключённое действие: "+item.key);formal++;
                }
            Require(warnings.Count==0,"Известные тексты вызвали предупреждение.");
            CheckAtomicMerge(manual.text);
            Debug.Log($"Русские названия: PASS — {importedEntries.Length} исходных ID, сохранены91ручной текст, неподключённых моделей с явным описанием={formal}; gameplay ID/параметры не менялись.");
        }
        private static void CheckRules()
        {
            var indexed=OriginalItemTextAuthoring.BuildTextIndex(XElement.Parse("<texts><pip id='hp'><n>Здоровье</n></pip><pip id='hp'><n>ОЗ</n></pip><weapon id='same'><n>Одно имя</n></weapon><weapon id='same'><n>Одно имя</n></weapon></texts>"));
            Require(indexed.Count==1 && indexed.ContainsKey("weapon:same"),"Индекс захватывает ненужные UI-ключи или теряет одинаковые подписи.");
            Reject(()=>OriginalItemTextAuthoring.BuildTextIndex(XElement.Parse("<texts><weapon id='conflict'><n>Первое</n></weapon><weapon id='conflict'><n>Другое</n></weapon></texts>")),"Конфликт нужных исходных имён принят.");
            var source=new Dictionary<string,XElement>(StringComparer.Ordinal)
            {
                ["weapon:gun"]=Text("Пистолет"),["weapon:gun^1"]=Text("Улучшенный пистолет"),["weapon:other"]=Text("Винтовка"),
                ["weapon:boom"]=Text("Граната"),["item:spell"]=Text("Заклинание"),["item:round"]=Text("Патрон"),
                ["pip:am_ap"]=Text("Бронебойный"),["item:target"]=Text("Деталь"),["pip:scheme1"]=Text("Схема"),["pip:recipe"]=Text("Рецепт")
            };
            string Name(string category,string id,string xml,int grade=0)=>OriginalItemTextAuthoring.Name(source,category,id,XElement.Parse(xml),grade);
            Require(Name("weapon","gun","<weapon/>")=="Пистолет","Базовое имя оружия.");
            Require(Name("weapon","gun","<weapon/>",1)=="Улучшенный пистолет","Явное имя грейда.");
            Require(Name("weapon","other","<weapon/>",1)=="Винтовка - II","Исходный суффикс второго варианта.");
            Require(Name("item","boom","<item tip='e'/>")=="Граната","Граната должна брать текст оружия.");
            Require(Name("weapon","spell","<weapon spell='1'/>")=="Заклинание","Текст формального заклинания.");
            Require(Name("item","round_ap","<item tip='a' base='round' mod='ap'/>")=="Патрон (Бронебойный)","Базовое имя и модификатор патрона.");
            Require(Name("item","s_target","<item tip='scheme' work='work'/>")=="Схема «Деталь»","Схема без прямой подписи.");
            Require(Name("item","s_target","<item tip='scheme' work='lab'/>")=="Рецепт «Деталь»","Рецепт без прямой подписи.");
            Require(Name("weapon","unknown","<weapon/>")==null,"Неизвестное исходное имя подменено.");
            Reject(()=>Name("item","round_missing","<item base='round' mod='missing'/>"),"Неизвестный модификатор патрона принят.");
        }
        private static XElement Text(string name)=>new("entry",new XElement("n",name));
        private static void CheckAtomicMerge(string manual)
        {
            var catalog=new ItemTextCatalog(manual);int count=catalog.Count;
            Reject(()=>catalog.ImportMissing("{\"schema\":1,\"items\":[{\"id\":\"valid-import\",\"name\":\"Имя\",\"description\":\"Описание\"},{\"id\":\"invalid-import\",\"name\":\"\",\"description\":\"Описание\"}]}"),"Неполный слой принят.");
            Require(catalog.Count==count && !catalog.Contains("valid-import"),"Неудачное слияние частично изменило каталог.");
            catalog.ImportMissing("{\"schema\":1,\"items\":[{\"id\":\"pistol\",\"name\":\"Подмена\",\"description\":\"Подмена\"},{\"id\":\"new-import\",\"name\":\"Новая вещь\",\"description\":\"Обычная вещь\"}]}");
            Require(catalog.Count==count+1 && catalog.Name("pistol","проверка")=="10-мм пистолет" && catalog.Name("new-import","проверка")=="Новая вещь","Слияние потеряло приоритет явных подписей.");
        }
        private static void Reject(Action action,string reason)
        {try{action();}catch(InvalidOperationException){return;}catch(InvalidDataException){return;}throw new InvalidOperationException(reason);}
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException("Исходные тексты предметов: "+reason);}
    }
}
