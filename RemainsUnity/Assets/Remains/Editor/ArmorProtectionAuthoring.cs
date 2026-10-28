using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ArmorProtectionAuthoring
    {
        private const string Output="Assets/Remains/Resources/Content/armor-protection.json";
        private const string Wardrobe="Assets/Remains/Resources/Content/cosmetic-equipment.json";
        [Serializable] private sealed class WardrobeManifest {public WardrobeEntry[] items;}
        [Serializable] private sealed class WardrobeEntry {public string id,slot;}
        [Serializable] private sealed class ProtectionManifest {public int schema;public string sourceHash;public ProtectionEntry[] items;}
        [Serializable] private sealed class ProtectionEntry {public string id;public float physical,energy,quality;public ResistanceEntry[] resistances;}
        [Serializable] private sealed class ResistanceEntry {public string type;public float value;}
        private static readonly (string attribute,int type)[] Resistances={
            ("bul",0),("blade",1),("phis",2),("fire",3),("expl",4),("laser",5),("plasma",6),
            ("venom",7),("spark",9),("acid",10),("cryo",11),("fang",14),("necro",16)};

        [MenuItem("Remains/Снаряжение/Подготовить защиту брони")]
        public static void Build()
        {
            string sourcePath=Path.GetFullPath("../.reference/original/dlc-ui/scripts/fe/AllData.as");
            string source=File.ReadAllText(sourcePath);
            var definitions=Regex.Matches(source,@"<armor\b[^>]*>[\s\S]*?</armor>").Cast<Match>()
                .Select(match=>XElement.Parse(match.Value)).ToDictionary(element=>(string)element.Attribute("id"),StringComparer.Ordinal);
            var wardrobe=StateJson.FromJson<WardrobeManifest>(File.ReadAllText(Wardrobe));
            if(wardrobe?.items==null) throw new InvalidDataException("Нет подготовленных определений гардероба.");
            var profiles=new List<ProtectionEntry>();
            foreach(var item in wardrobe.items.Where(item=>item.slot=="armor.body").OrderBy(item=>item.id,StringComparer.Ordinal))
            {
                if(!item.id.StartsWith("armor.",StringComparison.Ordinal) || !definitions.TryGetValue(item.id.Substring(6),out var armor))
                    throw new InvalidDataException("Нет исходной защиты надетого комплекта: definition="+item.id+".");
                // Current wardrobe IDs have no upgrade state: read original base upd[0], never the strongest grade.
                var update=armor.Elements("upd").FirstOrDefault()??throw new InvalidDataException("Нет исходной базовой ступени брони: "+item.id);
                var resistance=new Dictionary<string,float>(StringComparer.Ordinal);
                foreach(var (attribute,type) in Resistances) resistance.Add(DamageTypes.FromOriginal(type),Read(update,attribute));
                // Armor constructor applies this vulnerability to all body armor, including ordinary clothing.
                if(Read(armor,"tip",1)!=1) throw new InvalidDataException("Гардероб содержит не нательную броню: "+item.id);
                resistance.Add(DamageTypes.FromOriginal(19),-.5f);
                var profile=new ArmorProtectionProfile(item.id,Read(update,"armor"),Read(update,"marmor"),Read(update,"qual"),resistance);
                profiles.Add(new ProtectionEntry {id=profile.Id,physical=profile.Physical,energy=profile.Energy,quality=profile.Quality,
                    resistances=profile.Resistances.OrderBy(pair=>pair.Key,StringComparer.Ordinal)
                        .Select(pair=>new ResistanceEntry {type=pair.Key,value=pair.Value}).ToArray()});
            }
            if(profiles.Count==0 || profiles.Select(profile=>profile.id).Distinct().Count()!=profiles.Count)
                throw new InvalidDataException("Пустой или повторяющийся каталог защиты брони.");
            string json=StateJson.ToJson(new ProtectionManifest {schema=1,
                sourceHash=Remains.Persistence.RecoverySnapshot.Fingerprint(source),items=profiles.ToArray()});
            var parsed=new ArmorProtectionCatalog(json);
            if(parsed.Count!=profiles.Count) throw new InvalidDataException("Изменилось количество определений защиты при проверке.");
            File.WriteAllText(Output,json,new UTF8Encoding(false));AssetDatabase.ImportAsset(Output);
            Debug.Log($"Защита брони подготовлена: {parsed.Count} комплектов, базовая ступень, полное состояние; source={sourcePath}, fingerprint={parsed.Fingerprint}.");
        }
        // Armor.getXmlParam initializes absent fields to zero (tip defaults to body armor=1).
        private static float Read(XElement element,string attribute,float absent=0)
        {
            var text=(string)element.Attribute(attribute);if(text==null) return absent;
            if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value) || !float.IsFinite(value))
                throw new InvalidDataException($"Некорректное поле брони: attribute={attribute}, value={text}.");
            return value;
        }
    }
}
