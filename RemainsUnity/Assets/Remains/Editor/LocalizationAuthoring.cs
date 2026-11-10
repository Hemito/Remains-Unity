using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LocalizationAuthoring
    {
        public const string AssetPath="Assets/Remains/Resources/Content/localization.json";
        [MenuItem("Remains/Локализация/Импортировать все языки из DLC…")]
        public static void ImportMenu()
        {string folder=EditorUtility.OpenFolderPanel("Папка DLC с lang.xml","","");if(!string.IsNullOrEmpty(folder))Import(folder);}
        public static async void RunBatch()
        {
            try
            {
                string source=Environment.GetEnvironmentVariable("REMAINS_LOCALIZATION_DLC");
                var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-localization-source");
                if(at>=0 && at+1<args.Length)source=args[at+1];
                if(string.IsNullOrEmpty(source))throw new InvalidOperationException("Укажите -localization-source <DLC> или REMAINS_LOCALIZATION_DLC.");
                Import(source);LocalizationChecks.Run();await LocalizationChecks.RenderSnapshots();EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Import(string folder)
        {
            var languages=XDocument.Load(Path.Combine(folder,"lang.xml")).Root.Elements("lang").ToArray();
            var parsed=new Dictionary<string,XElement>(StringComparer.Ordinal);var provenance=new JArray();
            var sourceFiles=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var language in languages)
            {
                string code=(string)language.Attribute("id"),file=(string)language.Attribute("file");
                if(string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(file) || Path.GetFileName(file)!=file)throw new InvalidDataException("Некорректный язык/путь lang.xml.");
                byte[] bytes=File.ReadAllBytes(Path.Combine(folder,file));string source=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
                using var sha=SHA256.Create();string hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                int repairs=0;
                // One known source defect in Polish H&H. Never alter source files or conceal arbitrary XML errors.
                if(code=="pl" && source.Contains("H&H Magnum"))
                {repairs=Regex.Matches(source,"H&H Magnum").Count;source=source.Replace("H&H Magnum","H&amp;H Magnum");Debug.LogWarning("Импорт pl: экранирован исходный H&H Magnum, замен="+repairs+", SHA256="+hash);}
                parsed.Add(code,XDocument.Parse(source,LoadOptions.PreserveWhitespace).Root);
                sourceFiles.Add(code,hash);
                provenance.Add(new JObject{["language"]=code,["file"]=file,["sha256"]=hash,["ampersandRepairs"]=repairs});
            }
            if(!parsed.ContainsKey("ru"))throw new InvalidDataException("В DLC нет русского исходного языка.");
            var russian=Flatten(parsed["ru"]);var packs=new List<LocalizationLanguage>();var coverage=new JArray();
            var exported=ExportedContentCatalog.Load();
            foreach(var language in languages)
            {
                string code=(string)language.Attribute("id");var root=parsed[code];var values=Flatten(root);
                var entries=values.Select(pair=>new LocalizationEntry{key=pair.Key,text=pair.Value,russian=russian.TryGetValue(pair.Key,out var ru)?ru:null}).ToList();
                AddUnityLabels(entries,code);
                var names=ItemNames(root,exported);var pack=new LocalizationLanguage{code=code,nativeName=language.Value.Trim(),sourceSha256=sourceFiles[code],sourceXml=root.ToString(SaveOptions.DisableFormatting),entries=entries.ToArray(),items=names};
                packs.Add(pack);
                string Tokens(string value)=>string.Join("|",Regex.Matches(value,@"[@#]\d+").Cast<Match>().Select(m=>m.Value).OrderBy(x=>x,StringComparer.Ordinal));
                coverage.Add(new JObject{["language"]=code,["sourceEntries"]=values.Count,["itemNames"]=names.Length,
                    ["duplicateSourceIds"]=new JArray(root.Elements().GroupBy(e=>e.Name.LocalName+":"+(string)e.Attribute("id"),StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key)),
                    ["placeholderDifferences"]=new JArray(values.Where(p=>russian.TryGetValue(p.Key,out string ru) && Tokens(ru)!=Tokens(p.Value)).Select(p=>p.Key)),
                    ["missingRussianKeys"]=new JArray(russian.Keys.Except(values.Keys).OrderBy(x=>x,StringComparer.Ordinal))});
            }
            string languageFolder=Path.ChangeExtension(AssetPath,null);Directory.CreateDirectory(languageFolder);
            foreach(var pack in packs)
            {
                var payload=new LocalizationManifest{schema=1,sourceLanguage="ru",languages=new[]{pack}};
                string text=JsonConvert.SerializeObject(payload,Formatting.Indented);GameLocalization.Parse(text,false);
                File.WriteAllText(Path.Combine(languageFolder,pack.code+".json"),text,new UTF8Encoding(false));
            }
            var manifest=new LocalizationManifest{schema=1,sourceLanguage="ru",languages=packs.Select(p=>new LocalizationLanguage{code=p.code,nativeName=p.nativeName,sourceSha256=p.sourceSha256,entries=Array.Empty<LocalizationEntry>(),items=Array.Empty<ItemTextDefinition>()}).ToArray()};
            string json=JsonConvert.SerializeObject(manifest,Formatting.Indented);GameLocalization.Parse(json);
            File.WriteAllText(AssetPath,json,new UTF8Encoding(false));AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string report=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/localization"));Directory.CreateDirectory(report);
            File.WriteAllText(Path.Combine(report,"import.json"),new JObject{["sources"]=provenance,["coverage"]=coverage}.ToString());
            Debug.Log("Пакетная локализация импортирована: языков="+packs.Count+", ключей="+packs.Sum(x=>x.entries.Length)+". Отчёт .reference/localization/import.json.");
        }
        internal static Dictionary<string,string> Flatten(XElement root)
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            void Visit(XElement parent,string path)
            {
                var counts=new Dictionary<string,int>(StringComparer.Ordinal);
                foreach(var child in parent.Elements())
                {
                    string segment=child.Name.LocalName+((string)child.Attribute("id") is string id?":"+Uri.EscapeDataString(id):"");
                    counts.TryGetValue(segment,out int index);counts[segment]=index+1;
                    string key=path+segment+"["+index+"]";
                    if(!child.HasElements){if(!string.IsNullOrWhiteSpace(child.Value))result.Add(key,child.Value.Trim());}
                    else Visit(child,key+"/");
                }
            }
            Visit(root,"");return result;
        }
        private static ItemTextDefinition[] ItemNames(XElement root,ExportedContentCatalog exported)
        {
            // Name lookup follows Res.txt's first occurrence; complete duplicate records stay in sourceXml/entries.
            var normalized=new XElement(root.Name,root.Elements().GroupBy(e=>e.Name.LocalName+":"+(string)e.Attribute("id"),StringComparer.Ordinal).Select(g=>new XElement(g.First())));
            var index=OriginalItemTextAuthoring.BuildTextIndex(normalized);var names=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var item in exported.items.Where(i=>i.category is "item" or "weapon" or "weapon1"))
            {
                var parameters=XElement.Parse(item.rawXml);
                void Add(string id,int grade)
                {string value=OriginalItemTextAuthoring.Name(index,item.category,item.sourceId,parameters,grade);if(!string.IsNullOrWhiteSpace(value))names.TryAdd(id,value);}
                Add(item.key,0);Add(item.sourceId,0);
                foreach(var binding in item.graphics??Array.Empty<ExportedBinding>())
                    if(binding.role.StartsWith("item-image-grade-",StringComparison.Ordinal) && int.TryParse(binding.role.Substring(17),out int grade))Add(item.key+":grade:"+grade,grade);
            }
            foreach(var pair in new[]{("crowbar","mont"),("pistol","pistol"),("energy","energy"),("heal","heal"),("grenade","grenade"),("caps","money")})
                if(names.TryGetValue(pair.Item2,out string value))names[pair.Item1]=value;
            foreach(var entry in root.Elements("armor"))if((string)entry.Attribute("id") is string id && entry.Element("n") is {} title)names.TryAdd("armor."+id,title.Value.Trim());
            return names.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new ItemTextDefinition{id=x.Key,name=x.Value,description=""}).ToArray();
        }
        private static void AddUnityLabels(List<LocalizationEntry> entries,string code)
        {
            var data=JObject.Parse(File.ReadAllText("Assets/Remains/Editor/LocalizationLabels.json"));
            var codes=((JArray)data["languages"]).Values<string>().ToArray();int column=Array.IndexOf(codes,code);
            if(column<0)throw new InvalidDataException("Нет столбца собственных подписей для языка: "+code);
            foreach(var row in (JArray)data["labels"])
            {
                var values=(JArray)row["values"];
                if(values.Count!=codes.Length)throw new InvalidDataException("Неполная строка собственных переводов: "+row["key"]);
                entries.Add(new LocalizationEntry{key="unity/"+(string)row["key"],russian=(string)values[0],text=(string)values[column]});
            }
        }
    }
}
