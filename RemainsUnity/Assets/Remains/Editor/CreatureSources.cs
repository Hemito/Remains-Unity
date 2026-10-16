using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remains.Editor
{
    // Which original creatures the game's levels need: unit types placed in the factory's room templates
    // (Rooms/Original/plant). The content import and the creature authoring work from the same list.
    public static class CreatureSources
    {
        public const string TemplateFolder="Assets/Remains/Rooms/Original/plant";
        public const string ObjectPrefix="remains:obj:";

        public static HashSet<string> FactoryUnitIds(JObject catalog)
        {
            var units=new HashSet<string>(((JArray)catalog["definitions"]).OfType<JObject>()
                .Where(row=>(string)row["category"]=="unit").Select(row=>(string)row["id"]),StringComparer.Ordinal);
            var result=new HashSet<string>(StringComparer.Ordinal);
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..",TemplateFolder));
            foreach(string file in Directory.GetFiles(folder,"*.room.json"))
                foreach(var item in JObject.Parse(File.ReadAllText(file))["objects"] as JArray ?? new JArray())
                {
                    string type=(string)item["type"];
                    if(type==null || !type.StartsWith(ObjectPrefix,StringComparison.Ordinal)) continue;
                    string id=type.Substring(ObjectPrefix.Length);
                    if(units.Contains(id)) result.Add(id);
                }
            if(result.Count==0) throw new InvalidDataException("В шаблонах завода не найдено ни одного существа: "+folder);
            return result;
        }

        // A unit's bindings that the creature pipeline imports now: raster animation sheets and sounds. Vector
        // appearances animate through nested clips the export does not flatten yet; they are listed, not imported.
        public static bool Imported(JObject binding,bool sound)=>sound || (string)binding["role"]=="animation-sheet";
    }
}
