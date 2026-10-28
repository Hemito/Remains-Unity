using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Metadata-only refresh: no textures, prefabs, source stats or existing inventories are rebuilt.
    public static class DamageTypeAuthoring
    {
        public static void Run()
        {
            var rows=((JArray)JObject.Parse(File.ReadAllText(ContentPackage.Catalog))["definitions"]).OfType<JObject>()
                .Where(row=>(string)row["category"] is "weapon" or "unit")
                .ToDictionary(row=>(string)row["category"]+":"+(string)row["id"],row=>XElement.Parse((string)row["rawXml"]),StringComparer.Ordinal);
            var catalog=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset)
                ??throw new InvalidOperationException("Нет каталога оружия для типов урона.");
            var weapons=new List<(WeaponAsset asset,WeaponDefinition definition)>();
            foreach(var asset in catalog.AllAssets())
            {
                var definition=asset.CreateDefinition();definition.damageType=WeaponType(rows,definition.id);
                WeaponCatalogLoader.Validate(definition,"обновление типов урона");weapons.Add((asset,definition));
            }
            var actors=new List<(ActorTypeAsset asset,ActorTypeDefinition definition)>();
            var entityCatalog=new SerializedObject(EntityAssetCatalog.Load());var list=entityCatalog.FindProperty("actors");
            for(int i=0;i<list.arraySize;i++)
            {
                var asset=list.GetArrayElementAtIndex(i).objectReferenceValue as ActorTypeAsset
                    ??throw new InvalidOperationException("Пустой тип персонажа в каталоге, позиция="+i);
                var definition=asset.CreateDefinition();if(definition.naturalAttack==null) continue;
                // The early crawler fixture uses a Unity ID; its source is the original tarakan.
                string source=definition.id=="radroach"?"tarakan":definition.id;
                definition.naturalAttack.damageType=UnitType(rows,source,new HashSet<string>(StringComparer.Ordinal));
                definition.naturalAttack.Validate(definition.id);actors.Add((asset,definition));
            }
            // Publish only after the complete set is resolved and validated.
            foreach(var item in weapons) {item.asset.SetDefinition(item.definition);EditorUtility.SetDirty(item.asset);}
            foreach(var item in actors) {item.asset.SetDefinition(item.definition);EditorUtility.SetDirty(item.asset);}
            AssetDatabase.SaveAssets();
            Debug.Log($"Типы урона: сохранено оружие={weapons.Count}, естественные атаки={actors.Count}; без пересборки графики.");
        }

        private static string WeaponType(Dictionary<string,XElement> rows,string id)
        {
            string source=id;int grade=0;
            if(id.StartsWith("weapon:",StringComparison.Ordinal))
            {
                var parts=id.Split(':');
                if(parts.Length!=4 || parts[2]!="grade" || !int.TryParse(parts[3],NumberStyles.None,CultureInfo.InvariantCulture,out grade))
                    throw new InvalidOperationException("Не распознан ID оружия для типа урона: "+id);
                source=parts[1];
            }
            else switch(id)
            {
                case "pistol":case "npc-shot":source="p10mm";break;
                case "energy":source="lasp";break;
                // Hand-authored blunt melee has no source weapon definition; physical is its explicit gameplay contract.
                case "crowbar":return DamageTypes.FromOriginal(2);
                case "mtrap-bite":return UnitType(rows,"mtrap",new HashSet<string>(StringComparer.Ordinal));
                // UnitDamager.die → Bullet.iExpl explicitly sets Unit.D_EXPL (Bullet.as:698).
                case "damexpl1-blast":return DamageTypes.FromOriginal(4);
            }
            if(!rows.TryGetValue("weapon:"+source,out var xml))
                throw new InvalidOperationException($"Не найден исходный тип урона оружия: weapon={id}, source={source}.");
            var characters=xml.Elements("char").ToArray();
            if(grade<0 || grade>=characters.Length)
                throw new InvalidOperationException($"Нет исходного грейда типа урона: weapon={id}, grade={grade}.");
            // Weapon.getCharParam applies the selected grade's attributes over grade 0. Weapon.tipDamage starts at 0.
            string value=(string)characters[grade].Attribute("tipdam")??(string)characters[0].Attribute("tipdam");
            return DamageTypes.FromOriginal(value==null?0:int.Parse(value,CultureInfo.InvariantCulture));
        }

        private static string UnitType(Dictionary<string,XElement> rows,string id,HashSet<string> visiting)
        {
            if(!visiting.Add(id)) throw new InvalidOperationException("Цикл наследования типов урона существа: "+id);
            if(!rows.TryGetValue("unit:"+id,out var xml)) throw new InvalidOperationException("Нет исходного существа для типа урона: "+id);
            string value=(string)xml.Element("comb")?.Attribute("tipdam");
            if(value!=null) return DamageTypes.FromOriginal(int.Parse(value,CultureInfo.InvariantCulture));
            // Unit.getXmlParam reads its parent first; the declared Unit.tipDamage default is 2, not weapon's 0.
            string parent=(string)xml.Attribute("parent");
            return parent==null?DamageTypes.FromOriginal(2):UnitType(rows,parent,visiting);
        }
    }
}
