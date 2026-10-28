using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class TypedArmorAcceptance
    {
        public static void Prepare()
        {
            try
            {
                ArmorProtectionAuthoring.Build();DamageTypeAuthoring.Run();
                UpdateDescriptions();AssetDatabase.SaveAssets();
                WeaponCatalog.Load();TypedArmorChecks.Run();
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void UpdateDescriptions()
        {
            const string path="Assets/Remains/Resources/Content/item-text.json";
            var root=JObject.Parse(File.ReadAllText(path));var items=(JArray)root["items"];
            foreach(var row in items.OfType<JObject>())
            {
                string id=(string)row["id"];
                if(id?.StartsWith("armor.",StringComparison.Ordinal)!=true) continue;
                row["description"]=id=="armor.pip"?"Комбинезон жителя Стойла.":
                    id=="armor.antirad"?"Защитный костюм. Защита от радиации пока не подключена.":"Нательный комплект снаряжения.";
            }
            File.WriteAllText(path,root.ToString(Formatting.Indented)+"\n",new UTF8Encoding(false));AssetDatabase.ImportAsset(path);
        }
    }
}
