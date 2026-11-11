using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace Remains.Editor
{
    public static class LocalizationReuseAuthoring
    {
        public static string AdaptLayout(string russian,string translation)
        {
            if(russian.EndsWith(": ",StringComparison.Ordinal))return translation.TrimEnd(' ',':','：')+": ";
            if(russian.EndsWith(" · ",StringComparison.Ordinal))return translation.TrimEnd(' ','·')+" · ";
            return translation;
        }
        public static System.Collections.Generic.Dictionary<string,string> SourceKeys()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/LocalizationReuse/reuse-map.json"));
            return ((JArray)JObject.Parse(File.ReadAllText(path))["mappings"]).ToDictionary(m=>(string)m["table"]+"/"+(string)m["key"],m=>(string)m["originalKey"],StringComparer.Ordinal);
        }
        public static void Apply(LocalizationLanguage[] packs)
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/LocalizationReuse/reuse-map.json"));
            if(!File.Exists(path))throw new InvalidDataException("Нет проверенного сопоставления локализации: "+path);
            var manifest=JObject.Parse(File.ReadAllText(path));
            if((int?)manifest["schema"]!=1 || manifest["mappings"] is not JArray mappings)throw new InvalidDataException("Некорректное сопоставление локализации: "+path);
            var original=packs.Single(p=>p.code=="ru").entries.ToDictionary(e=>e.key,e=>e.text,StringComparer.Ordinal);
            int applied=0;
            foreach(var mapping in mappings)
            {
                string name=(string)mapping["table"],key=(string)mapping["key"],russian=(string)mapping["russian"],source=(string)mapping["originalKey"];
                if(source==null || source.StartsWith("unity/",StringComparison.Ordinal) || !original.ContainsKey(source))throw new InvalidDataException("Сопоставление не ссылается на оригинал: "+key+" -> "+source);
                if((string)mapping["originalRussian"]!=original[source])throw new InvalidDataException("Изменился исходный текст оригинала: "+source);
                var collection=LocalizationEditorSettings.GetStringTableCollection(name);
                var ru=collection?.GetTable("ru") as StringTable;
                if(ru?.GetEntry(key)?.LocalizedValue!=russian)throw new InvalidDataException("Изменился русский исходник сопоставления: "+name+"/"+key);
                foreach(var pack in packs.Where(p=>p.code!="ru"))
                {
                    var entry=pack.entries.FirstOrDefault(e=>e.key==source);
                    if(entry==null || string.IsNullOrWhiteSpace(entry.text)){Debug.LogWarning("В оригинале нет перевода для сопоставления: locale="+pack.code+", key="+source);continue;}
                    var table=(StringTable)collection.GetTable(GameLocalization.NormalizeCode(pack.code));
                    table.AddEntry(key,AdaptLayout(russian,entry.text)).IsSmart=false;EditorUtility.SetDirty(table);applied++;
                }
            }
            Debug.Log("Переиспользование исходной локализации: сопоставлений="+mappings.Count+", переводов="+applied+". Русские строки и ключи сохранены.");
        }
    }
}
