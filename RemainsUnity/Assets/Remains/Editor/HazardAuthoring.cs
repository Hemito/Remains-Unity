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
    // Traps and charges from the original data in one pass (no per-object hand authoring): every hazard unit the factory
    // templates place (CreatureSources.HazardKind) becomes a HazardDefinition with its weapons — a trap's bite (AllData
    // comb damage) and a charge's blast (UnitDamager.setWeapon tip 3: damexpl 250, expl 200 px, scaled like the mines
    // by the Unity grenade, MineContentAuthoring.BlastScale) — and their combat sounds. Sizes are AllData phis sX/sY
    // (50 px per metre); visibility is the classes' 300 px.
    //   batch -executeMethod Remains.Editor.HazardAuthoring.RunBatch
    public static class HazardAuthoring
    {
        private const string CatalogPath="Assets/Remains/Resources/"+HazardCatalog.Resource+".asset";
        private const string CombatAudioPath="Assets/Remains/Resources/Presentation/combat-audio.json";
        private const float PixelsPerMetre=50,OriginalVisibility=300;
        // UnitDamager.setWeapon (tipDamager 3) and the disarm loot LootGen.lootCont "bomb" (three dinamit).
        private const float ChargeBlastDamage=250,ChargeBlastRadius=200;
        private const string ChargeLoot="weapon:dinamit";private const int ChargeLootCount=3;

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var catalog=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var rows=((JArray)catalog["definitions"]).OfType<JObject>().ToArray();
            var sounds=((JArray)catalog["resources"]).OfType<JObject>().Where(r=>(string)r["kind"]=="sound")
                .SelectMany(r=>((JArray)r["names"]).Select(n=>(name:(string)n,id:(string)r["id"]))).GroupBy(s=>s.name).ToDictionary(g=>g.Key,g=>g.First().id,StringComparer.Ordinal);
            var exported=ExportedContentCatalog.Load();
            var weapons=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset)??throw new InvalidOperationException("Нет каталога оружия.");
            var (damageScale,radiusScale)=MineContentAuthoring.BlastScale(exported,weapons);
            var serialized=new SerializedObject(weapons);var list=serialized.FindProperty("weapons");

            // Template objects grouped by the hazard unit they place.
            var objects=new SortedDictionary<string,List<string>>(StringComparer.Ordinal);var classes=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var template in CreatureSources.FactoryObjects(catalog))
            {
                if(template.problem!=null || template.units.Length!=1) continue;
                string unit=template.units[0];
                if(CreatureSources.HazardKind(template.className,CreatureSources.UnitXml(catalog,unit))==null) continue;
                if(!objects.TryGetValue(unit,out var placed)) objects.Add(unit,placed=new List<string>());
                placed.Add(template.objectId);classes[unit]=template.className;
            }
            if(objects.Count==0) throw new InvalidDataException("В шаблонах завода нет ни одной ловушки.");

            var definitions=new List<HazardDefinition>();var audio=new List<JObject>();
            foreach(var pair in objects)
            {
                string id=pair.Key;
                var row=rows.Single(r=>(string)r["category"]=="unit" && (string)r["id"]==id);
                var unit=XElement.Parse((string)row["rawXml"]);
                string kind=CreatureSources.HazardKind(classes[id],unit);
                var phis=unit.Element("phis");var comb=unit.Element("comb");
                string artwork=((JArray)row["graphics"]).OfType<JObject>().FirstOrDefault(g=>(string)g["role"]=="appearance")?["resource"]?.ToString()
                    ??throw new InvalidDataException($"Ловушка {id}: нет вида Res.getVis в выгрузке.");
                if(exported.Resource(artwork).frames is not {Length:>0}) throw new InvalidDataException($"Ловушка {id}: вид {artwork} не импортирован.");
                var definition=new HazardDefinition
                {
                    id=id,kind=kind,title=((string)unit.Element("n")??id).Trim(),objects=pair.Value.ToArray(),
                    width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,health=Num(comb,"hp"),skin=Num(comb,"skin",0),
                    visibility=OriginalVisibility/PixelsPerMetre,artwork=artwork
                };
                if(kind==HazardRules.Trap)
                {
                    definition.biteWeapon=id+"-bite";
                    definition.snapSound=Sound(sounds,"trap_a",id);definition.rearmSound=Sound(sounds,"trap_reload",id);
                    MineContentAuthoring.Store(new WeaponDefinition {id=definition.biteWeapon,damage=Num(comb,"damage"),range=definition.width,
                        cooldown=1,holdingArea=new HoldingArea(),stopAtFirstActor=true},list);
                    audio.Add(new JObject {["weapon"]=definition.biteWeapon,["clip"]=AudioClipSource.ExportedPrefix+definition.snapSound,["kind"]="melee",["minGain"]=1,["maxGain"]=1});
                }
                else
                {
                    definition.blastWeapon=id+"-blast";definition.disarmItem=ChargeLoot;definition.disarmCount=ChargeLootCount;
                    exported.CreateFormalItem(ChargeLoot,0,1);
                    MineContentAuthoring.Store(new WeaponDefinition {id=definition.blastWeapon,explosive=true,damage=ChargeBlastDamage*damageScale,
                        range=ChargeBlastRadius*radiusScale,cooldown=1,holdingArea=new HoldingArea()},list);
                    audio.Add(new JObject {["weapon"]=definition.blastWeapon,["clip"]="grenade-explosion",["kind"]="explosion",["minGain"]=1,["maxGain"]=1});
                }
                definition.Validate();
                definitions.Add(definition);
                Debug.Log($"Ловушка {id} ({kind}, «{definition.title}»): объекты {string.Join(", ",definition.objects)}, {definition.width:F2}×{definition.height:F2} м, "+
                    $"здоровье {definition.health}, кожа {definition.skin}, вид {artwork}{(kind==HazardRules.Trap?", укус "+Num(comb,"damage"):$", взрыв {ChargeBlastDamage*damageScale:F1} в радиусе {ChargeBlastRadius*radiusScale:F2} м")}.");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(weapons);

            var asset=AssetDatabase.LoadAssetAtPath<HazardCatalog>(CatalogPath);
            bool created=asset==null;if(created) asset=ScriptableObject.CreateInstance<HazardCatalog>();
            asset.Configure(definitions.ToArray());
            if(created) AssetDatabase.CreateAsset(asset,CatalogPath);
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            WriteAudio(audio);
            Debug.Log($"Ловушки завода: описаний {definitions.Count}.");
        }

        // The generated weapons' entries replace their previous ones in the combat sound catalog.
        private static void WriteAudio(List<JObject> generated)
        {
            var json=JObject.Parse(File.ReadAllText(CombatAudioPath));var entries=(JArray)json["entries"];
            var ids=generated.Select(e=>(string)e["weapon"]).ToHashSet(StringComparer.Ordinal);
            var kept=entries.OfType<JObject>().Where(e=>!ids.Contains((string)e["weapon"])).ToList();
            var lines=kept.Concat(generated).Select(e=>"    "+e.ToString(Newtonsoft.Json.Formatting.None));
            File.WriteAllText(CombatAudioPath,"{\n  \"entries\": [\n"+string.Join(",\n",lines)+"\n  ]\n}\n");
            AssetDatabase.ImportAsset(CombatAudioPath,ImportAssetOptions.ForceUpdate);
        }
        private static string Sound(Dictionary<string,string> sounds,string name,string id)=>sounds.TryGetValue(name,out string resource)?resource:
            throw new InvalidDataException($"Ловушка {id}: нет исходного звука {name}.");
        private static float Num(XElement element,string name,float? fallback=null)
        {
            string value=(string)element?.Attribute(name);
            if(value!=null) return float.Parse(value,CultureInfo.InvariantCulture);
            return fallback??throw new InvalidDataException($"Нет атрибута {name} в {element?.Name}.");
        }
    }
}
