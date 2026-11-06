using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Text joins follow original Item.getXmlParam/Weapon.getXmlParam, not the parameter XML's absent <n>.
    // Separate presentation data keeps naming changes outside the gameplay fingerprint.
    public static class OriginalItemTextAuthoring
    {
        public const string AssetPath="Assets/Remains/Resources/Content/original-item-text.json";
        private const string SourceSha256="acd503ab8ec20428c2d625865d10684a26a3c20e0a056ea2ee2be46d9fd9a6ad";
        public static void RunBatch()
        {try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            string source=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/rig-symbols/armor-text-ru.xml"));
            using var sha=SHA256.Create();string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source))).Replace("-","").ToLowerInvariant();
            if(hash!=SourceSha256)throw new InvalidDataException("Не совпал проверенный источник русских текстов: "+source+", SHA256="+hash);
            var xml=XDocument.Load(source).Root??throw new InvalidDataException("Нет корня русского каталога: "+source);
            var index=xml.Elements().Where(e=>e.Attribute("id")!=null).ToDictionary(e=>e.Name.LocalName+":"+(string)e.Attribute("id"),e=>e,StringComparer.Ordinal);
            WeaponCatalog.Load();var catalog=ExportedContentCatalog.Load();var entries=new Dictionary<string,ItemTextDefinition>(StringComparer.Ordinal);var missing=new List<string>();
            foreach(var item in catalog.items.OrderBy(e=>e.category=="item"?0:1).ThenBy(e=>e.key,StringComparer.Ordinal))
            {
                if(item.category is not ("item" or "weapon" or "weapon1"))continue;
                var parameters=XElement.Parse(item.rawXml);
                string name=Name(index,item.category,item.sourceId,parameters,0);
                if(string.IsNullOrWhiteSpace(name)){missing.Add(item.key);continue;}
                Add(item.key,name,item,parameters);Add(item.sourceId,name,item,parameters);
                if(item.category=="weapon")foreach(var binding in item.graphics??Array.Empty<ExportedBinding>())
                {
                    const string prefix="item-image-grade-";
                    if(!binding.role.StartsWith(prefix,StringComparison.Ordinal))continue;
                    if(!int.TryParse(binding.role.Substring(prefix.Length),out int grade)||grade<0)throw new InvalidDataException("Некорректный грейд текста: "+item.key+", role="+binding.role);
                    Add(item.key+":grade:"+grade,Name(index,item.category,item.sourceId,parameters,grade),item,parameters);
                }
            }
            var document=new JObject { ["schema"]=1,["source"]="Remains 1.04 Graphic Update DLC/text_ru.xml",["sourceSha256"]=hash,
                ["nameRules"]="Item.getXmlParam: weapon/explosive, ammo base+mod, scheme/recipe; Weapon.getXmlParam: named ^grade, otherwise - II",
                ["items"]=new JArray(entries.Values.OrderBy(e=>e.id,StringComparer.Ordinal).Select(e=>new JObject { ["id"]=e.id,["name"]=e.name,["description"]=e.description })) };
            File.WriteAllText(AssetPath,document.ToString(Newtonsoft.Json.Formatting.Indented)+"\n");AssetDatabase.ImportAsset(AssetPath);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/weapon-assembly-20261106"));Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"item-text-import.json"),new JObject { ["sourceSha256"]=hash,["entries"]=entries.Count,["untranslated"]=new JArray(missing) }.ToString());
            if(missing.Count>0)Debug.LogWarning("Исходные русские тексты отсутствуют: "+string.Join(", ",missing)+". Технические определения оставлены с диагностикой неизвестного имени.");
            Debug.Log($"Русские названия: {entries.Count} ID; без перевода {missing.Count}. Ручные подписи item-text имеют приоритет.");

            void Add(string id,string title,ExportedItemEntry item,XElement parameters)
            {
                if(entries.ContainsKey(id))return;
                entries.Add(id,new ItemTextDefinition {id=id,name=title,description=Description(id,item,parameters)});
            }
        }
        internal static string Name(IReadOnlyDictionary<string,XElement> index,string category,string id,XElement parameters,int grade)
        {
            string Text(string kind,string key)=>index.TryGetValue(kind+":"+key,out var entry)?entry.Element("n")?.Value.Trim():null;
            bool weapon=category is "weapon" or "weapon1" || (string)parameters.Attribute("tip")=="e";
            string kind=weapon?"weapon":"item";
            if(weapon && (string)parameters.Attribute("spell")=="1")kind="item";
            string name=Text(kind,id);
            if(weapon && grade>0 && name!=null)return Text(kind,id+"^"+grade)??name+" - II";
            if(!weapon && parameters.Attribute("base") is {} basis)
            {
                name=Text("item",basis.Value);
                if(name!=null && parameters.Attribute("mod") is {} modifier)
                {string suffix=Text("pip","am_"+modifier.Value);if(suffix==null)throw new InvalidDataException("Нет подписи модификатора патрона: "+id+", mod="+modifier.Value);name+=" ("+suffix+")";}
            }
            if(name==null && !weapon && (string)parameters.Attribute("tip")=="scheme" && id.StartsWith("s_",StringComparison.Ordinal))
            {
                string target=Text("item",id.Substring(2));
                if(target!=null)name=Text("pip",(string)parameters.Attribute("work")=="work"?"scheme1":"recipe")+" «"+target+"»";
            }
            return name;
        }
        private static string Description(string id,ExportedItemEntry item,XElement parameters)
        {
            if(WeaponCatalog.Contains(id))
            {
                var weapon=WeaponCatalog.Get(id);
                if(weapon.IsGrenade)return weapon.IsFire?"Бросаемое зажигательное оружие.":"Ручная граната.";
                if(WeaponCatalog.IsMine(id))return "Устанавливаемая мина.";
                if(weapon.meleeMotion!=null || weapon.strike?.Configured==true)return "Оружие ближнего боя.";
                return "Оружие. Основная атака подключена; особые свойства оригинала могут быть не реализованы.";
            }
            if(id.StartsWith("item:",StringComparison.Ordinal) && ItemRadiationCures.Amount(id)>0)return "Средство от радиации. Можно использовать.";
            if((string)parameters.Attribute("tip")=="a")return parameters.Attribute("mod")==null?"Боеприпасы.":"Боеприпасы. Особые свойства модификатора пока не реализованы.";
            if((string)parameters.Attribute("tip")=="scheme")return "Схема или рецепт. Изготовление пока не реализовано.";
            return "Подключено как обычный предмет. Специальное действие пока не реализовано.";
        }
    }
}
