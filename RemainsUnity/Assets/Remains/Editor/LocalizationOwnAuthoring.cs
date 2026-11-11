using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

namespace Remains.Editor
{
    public static class LocalizationOwnAuthoring
    {
        public static void ExportExistingBatch()
        {
            try
            {
                var result=new JArray();
                foreach(string name in new[]{"Remains.UI","Remains.Items"})
                {
                    var collection=LocalizationEditorSettings.GetStringTableCollection(name);
                    foreach(var entry in ((StringTable)collection.GetTable("ru")).Values)
                    {
                        var translations=new JObject();
                        foreach(string code in Locales)
                        {
                            string value=((StringTable)collection.GetTable(code)).GetEntry(entry.Key)?.LocalizedValue;
                            if(!string.IsNullOrEmpty(value))translations[code]=value;
                        }
                        result.Add(new JObject{["table"]=name,["key"]=entry.Key,["russian"]=entry.LocalizedValue,["translations"]=translations});
                    }
                }
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/own-loc-existing.json")),new JObject{["entries"]=result}.ToString());
                EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private const string Marker="Remains: собственный перевод; ";
        private static readonly string[] Locales={"en","uk","zh-Hans","zh-Hant","pl","de","es","ja"};
        public static void Apply()
        {
            var seen=new HashSet<string>(StringComparer.Ordinal);int count=0;
            var prepared=new List<(StringTable Table,string Key,string Value,string File)>();
            foreach(string file in Directory.GetFiles("Assets/Remains/Editor","LocalizationOwn*.json").OrderBy(p=>p,StringComparer.Ordinal))
            {
                var data=JObject.Parse(File.ReadAllText(file));
                if((int?)data["schema"]!=1 || data["entries"] is not JArray entries)throw new InvalidDataException("Некорректный собственный перевод: "+file);
                foreach(var entry in entries)
                {
                    string tableName=(string)entry["table"],key=(string)entry["key"],russian=(string)entry["russian"];
                    if(!seen.Add(tableName+"/"+key))throw new InvalidDataException("Повтор собственного перевода: "+key);
                    var collection=LocalizationEditorSettings.GetStringTableCollection(tableName);
                    if(((StringTable)collection?.GetTable("ru"))?.GetEntry(key)?.LocalizedValue!=russian)throw new InvalidDataException("Изменился русский исходник собственного перевода: "+key);
                    if(entry["translations"] is not JObject translations)throw new InvalidDataException("Нет переводов: "+key);
                    foreach(var translation in translations.Properties())
                    {
                        string code=translation.Name,value=(string)translation.Value;
                        if(!Locales.Contains(code) || string.IsNullOrWhiteSpace(value))throw new InvalidDataException("Неизвестный язык/пустой перевод: "+key+"/"+code);
                        if(Arguments(russian)!=Arguments(value))throw new InvalidDataException("Изменены аргументы шаблона: "+key+"/"+code);
                        var table=(StringTable)collection.GetTable(code);var previous=table.GetEntry(key);
                        if((bool?)entry["replaceExisting"]!=true && previous!=null && !string.IsNullOrEmpty(previous.LocalizedValue) && !previous.GetMetadatas<Comment>().Any(c=>c.CommentText.StartsWith(Marker,StringComparison.Ordinal)))continue;
                        prepared.Add((table,key,value,file));
                    }
                }
            }
            foreach(var entry in prepared)
            {
                var added=entry.Table.AddEntry(entry.Key,entry.Value);added.IsSmart=false;
                if(!added.GetMetadatas<Comment>().Any(c=>c.CommentText.StartsWith(Marker,StringComparison.Ordinal)))added.AddMetadata(new Comment{CommentText=Marker+Path.GetFileName(entry.File)});
                EditorUtility.SetDirty(entry.Table);count++;
            }
            Debug.Log("Собственные переводы: обработано записей="+seen.Count+", значений="+count+"; прежние переводы сохранены.");
        }
        private static string Arguments(string value)=>string.Join("|",Regex.Matches(value??"",@"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})").Cast<Match>().Select(m=>m.Groups[1].Value).OrderBy(x=>x,StringComparer.Ordinal));
        public static void ValidateCoverage()
        {
            int count=0;var missing=new List<string>();
            foreach(string name in new[]{"Remains.UI","Remains.Items"})
            {
                var collection=LocalizationEditorSettings.GetStringTableCollection(name);
                var tables=Locales.ToDictionary(code=>code,code=>(StringTable)collection.GetTable(code));
                foreach(var entry in ((StringTable)collection.GetTable("ru")).Values)
                {
                    count++;
                    foreach(string code in Locales)
                        if(string.IsNullOrWhiteSpace(tables[code].GetEntry(entry.Key)?.LocalizedValue))missing.Add(name+"/"+entry.Key+"/"+code);
                }
            }
            if(missing.Count>0)throw new InvalidDataException("Неполная локализация: пропусков="+missing.Count+". "+string.Join("; ",missing.Take(15)));
            Debug.Log("Покрытие UI и предметов: PASS — "+count+" записей × 8 переводов.");
        }
    }
}
