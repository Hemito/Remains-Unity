using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    // Read-only comparison of confirmed source aliases and existing authored weapons.
    // No exported recipe becomes playable through this diagnostic.
    public static class ExportedWeaponAdapter
    {
        public const float StepsPerSecond=30,PixelsPerMetre=50;
        public static JArray ExistingAliasReport(ExportedContentCatalog catalog,WeaponAssetCatalog existing)
        {
            var bundles=JArray.Parse(catalog.weaponBundlesJson).OfType<JObject>().ToDictionary(value=>(string)value["id"],StringComparer.Ordinal);
            var known=new Dictionary<string,string>(StringComparer.Ordinal) {{"pistol","p10mm"},{"energy","lasp"},{"spear","spear"},{"grenade","grenade"}};
            var result=new JArray();
            foreach(var asset in existing.AllAssets())
            {
                var definition=asset.CreateDefinition();
                var entry=new JObject {{"runtimeId",definition.id},{"changed",false}};
                if(!known.TryGetValue(definition.id,out var sourceId))
                {entry["status"]="existing-authored; no confirmed source alias";result.Add(entry);continue;}
                entry["sourceId"]=sourceId;
                if(!bundles.TryGetValue(sourceId,out var bundle))throw new InvalidOperationException("Нет исходника подтверждённого alias: "+sourceId);
                var grade=(JObject)((JArray)bundle["grades"])[0];
                entry["behavior"]=grade["parameters"]?["behavior"]?.DeepClone();
                entry["status"]="existing-authored; retained without re-adaptation";
                if(definition.id is "pistol" or "energy")
                {
                    var sections=((JArray)grade["parameters"]["sections"]).OfType<JObject>().ToDictionary(value=>(string)value["name"],value=>(JObject)value["values"]);
                    float Read(string section,string field)=>(float)sections[section][field]["numeric"];
                    var differences=new JArray();
                    void Compare(string name,float actual,float expected){if(Mathf.Abs(actual-expected)>.0001f)differences.Add(new JObject {{"field",name},{"runtime",actual},{"sourceConverted",expected}});}
                    Compare("damage",definition.damage,Read("char","damage"));
                    Compare("cooldown",definition.cooldown,Read("char","rapid")/StepsPerSecond);
                    Compare("magazineSize",definition.magazineSize,Read("ammo","holder"));
                    Compare("reloadSeconds",definition.reloadSeconds,Read("ammo","reload")/StepsPerSecond);
                    Compare("projectileSpeed",definition.projectileSpeed,Read("phis","speed")*StepsPerSecond/PixelsPerMetre);
                    Compare("shotSpreadHalfAngleDegrees",definition.shotSpreadHalfAngleDegrees,Read("phis","deviation")/2);
                    entry["differences"]=differences;
                }
                result.Add(entry);
            }
            return result;
        }

    }
}
