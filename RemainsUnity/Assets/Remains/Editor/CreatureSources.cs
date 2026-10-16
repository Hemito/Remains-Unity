using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remains.Editor
{
    // Which original creatures the game's levels need, resolved as the original creates them: a room template holds an
    // object (AllData obj, tip="unit"); Unit.create builds its class (cl) with the obj's cid, and the class picks the unit
    // definition — the cid itself, the object's id, a random variant (UnitMonstrik: scorp → scorp1|scorp2) or the class
    // default when there is no cid (UnitTrap → mtrap). The content import and the creature authoring share this list.
    public static class CreatureSources
    {
        public const string TemplateFolder="Assets/Remains/Rooms/Original/plant";
        public const string ObjectPrefix="remains:obj:";
        // Classes with their own world systems, not actors (Mine: world.mines).
        private static readonly HashSet<string> separate=new(StringComparer.Ordinal) {"Mine"};
        private static readonly Dictionary<string,string> classDefaults=new(StringComparer.Ordinal) {{"UnitTrap","mtrap"},{"UnitDamager","damshot"}};
        private static readonly Dictionary<string,string[]> randomVariants=new(StringComparer.Ordinal) {{"scorp",new[]{"scorp1","scorp2"}},
            {"bloat",new[]{"bloat0","bloat1","bloat2","bloat3","bloat4"}}};   // UnitBloat: tr = floor(random × 5) without a parameter

        public sealed class TemplateObject
        {
            public string objectId,className;
            public string[] units=Array.Empty<string>();   // unit definitions it may become (more than one: random variant)
            public string problem;                          // why it resolves to no unit definition
        }

        public static IReadOnlyList<TemplateObject> FactoryObjects(JObject catalog)
        {
            var rows=((JArray)catalog["definitions"]).OfType<JObject>().ToArray();
            var units=new HashSet<string>(rows.Where(r=>(string)r["category"]=="unit").Select(r=>(string)r["id"]),StringComparer.Ordinal);
            var objects=rows.Where(r=>(string)r["category"]=="obj").ToDictionary(r=>(string)r["id"],r=>XElement.Parse((string)r["rawXml"]),StringComparer.Ordinal);
            var used=new SortedSet<string>(StringComparer.Ordinal);
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..",TemplateFolder));
            foreach(string file in Directory.GetFiles(folder,"*.room.json"))
                foreach(var item in JObject.Parse(File.ReadAllText(file))["objects"] as JArray ?? new JArray())
                {
                    string type=(string)item["type"];
                    if(type!=null && type.StartsWith(ObjectPrefix,StringComparison.Ordinal)) used.Add(type.Substring(ObjectPrefix.Length));
                }
            var result=new List<TemplateObject>();
            foreach(string id in used)
            {
                if(!objects.TryGetValue(id,out var obj) || (string)obj.Attribute("tip")!="unit") continue;
                string cls=(string)obj.Attribute("cl")??"";
                if(separate.Contains(cls) || cls=="") continue;
                string cid=(string)obj.Attribute("cid");
                var entry=new TemplateObject {objectId=id,className=cls};
                if(cid!=null && units.Contains(cid)) entry.units=new[]{cid};
                else if(randomVariants.TryGetValue(cid??id,out var variants) && variants.All(units.Contains)) entry.units=variants;
                else if(cid==null && units.Contains(id)) entry.units=new[]{id};
                else if(cid==null && classDefaults.TryGetValue(cls,out string fallback) && units.Contains(fallback)) entry.units=new[]{fallback};
                else if(units.Contains(id)) entry.units=new[]{id};
                else entry.problem=$"не найдено определение существа (obj {id}, cid {cid ?? "—"}, класс {cls})";
                result.Add(entry);
            }
            if(result.Count==0) throw new InvalidDataException("В шаблонах завода не найдено ни одного существа: "+folder);
            return result;
        }
        // With their parents: a unit reads its parent's values and sounds first (Unit.getXmlParam; bloat0 → bloat).
        public static HashSet<string> FactoryUnitIds(JObject catalog)
        {
            var parents=((JArray)catalog["definitions"]).OfType<JObject>().Where(r=>(string)r["category"]=="unit" && (string)r["parent"]!=null)
                .ToDictionary(r=>(string)r["id"],r=>(string)r["parent"],StringComparer.Ordinal);
            var result=FactoryObjects(catalog).SelectMany(o=>o.units).ToHashSet(StringComparer.Ordinal);
            foreach(string id in result.ToArray()) if(parents.TryGetValue(id,out string parent)) result.Add(parent);
            return result;
        }

        // Original sounds the creature families play by name (Unit.udarUnit by damage type, Bullet.sound by material,
        // UnitTrap): imported with the content package and referenced as "exported:<resource>".
        public static readonly string[] Sounds={"fang_hit1","fang_hit2","fang_hit3","acid","hit_flesh","hit_slime","electro","hit_necr","trap_a","trap_reload"};

        // A unit's bindings that the pipeline imports: raster animation sheets, the class constructors' own vis symbol
        // (flat frame animation) and sounds. A vclass appearance animates through nested clips the export does not
        // flatten yet; it is listed, not imported.
        public static bool Imported(JObject binding,bool sound)=>sound || (string)binding["role"]=="animation-sheet" ||
            ((string)binding["rule"]??"").Contains("Res.getVis");
    }
}
