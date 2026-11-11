using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace Remains.Editor
{
    public static class LocalizationReuseExport
    {
        public static void RunBatch()
        {
            try
            {
                var result=new JArray();
                var old=JObject.Parse(File.ReadAllText("Assets/Remains/Editor/LocalizationSources/ru.json"));
                var names=old["languages"][0]["items"].ToDictionary(x=>(string)x["id"],x=>(string)x["name"],StringComparer.Ordinal);
                foreach(string name in new[]{"Remains.UI","Remains.Items"})
                {
                    var table=(StringTable)LocalizationEditorSettings.GetStringTableCollection(name).GetTable("ru");
                    foreach(var entry in table.Values.OrderBy(e=>e.Key,StringComparer.Ordinal))
                    {
                        if(name=="Remains.Items" && entry.Key.EndsWith("/name",StringComparison.Ordinal) && names.TryGetValue(entry.Key.Substring(0,entry.Key.Length-5),out string original) && original==entry.LocalizedValue)continue;
                        result.Add(new JObject{["table"]=name,["key"]=entry.Key,["russian"]=entry.LocalizedValue,["kind"]=entry.Key.EndsWith("/description",StringComparison.Ordinal)?"description":entry.Key.EndsWith("/name",StringComparison.Ordinal)?"item-name":"ui"});
                    }
                }
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/LocalizationReuse/own-strings.json"));Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,new JObject{["schema"]=1,["entries"]=result}.ToString());
                Debug.Log("Инвентаризация собственных строк: "+result.Count+", "+path);EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
