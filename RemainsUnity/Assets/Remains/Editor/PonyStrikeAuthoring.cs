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
    // The body strike of players and sapient ponies (PonyStrikeRules) from the original: AllData weapon "punch" (damage,
    // rapid, tipdam) and WKick.actions (the contact at t_attack == rapid − 8, 8 frames after the start; a punch: segments 60 → 20 px ahead at
    // 10–50 px above the support; a kick with the run key: 70 → 20 px behind at 30 and 50 px, damage ×2; sounds m_med
    // and m_big at 0.1–0.3). Not carried over: knockback (knock, ×1.5 for a kick), stun (dopEffect), destroy of tiles,
    // the higher row when the tile above is weaker, punchDamMult.
    //   batch -executeMethod Remains.Editor.PonyStrikeAuthoring.RunBatch
    public static class PonyStrikeAuthoring
    {
        private const float PixelsPerMetre=50,FramesPerSecond=30,ContactFrames=8,Near=20;

        public static void RunBatch()
        {
            try {Run();AssetDatabase.SaveAssets();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var catalog=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var row=((JArray)catalog["definitions"]).OfType<JObject>().Single(r=>(string)r["category"]=="weapon" && (string)r["id"]=="punch");
            var character=XElement.Parse((string)row["rawXml"]).Element("char")??throw new InvalidDataException("AllData punch: нет char.");
            float rapid=Num(character,"rapid"),damage=Num(character,"damage");
            string type=DamageTypes.FromOriginal((int)Num(character,"tipdam"));
            float contact=ContactFrames/rapid;
            string Sound(string name)=>((JArray)catalog["resources"]).OfType<JObject>()
                .SingleOrDefault(r=>(string)r["kind"]=="sound" && ((JArray)r["names"]).Any(n=>(string)n==name))?["id"]?.ToString()
                ??throw new InvalidDataException("Нет исходного звука "+name+".");

            var weapons=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset)??throw new InvalidOperationException("Нет каталога оружия.");
            var serialized=new SerializedObject(weapons);var list=serialized.FindProperty("weapons");
            WeaponDefinition Strike(string id,float multiplier,float far,bool behind,params float[] heights)=>new()
            {
                id=id,damageType=type,damage=damage*multiplier,cooldown=rapid/FramesPerSecond,range=far/PixelsPerMetre,
                recoil=WeaponRecoilProfile.Disabled(),holdingArea=new HoldingArea(),stopAtFirstActor=false,stopAtTerrain=true,
                strike=new StrikeProfile {behind=behind,near=Near/PixelsPerMetre,far=far/PixelsPerMetre,contact=contact,
                    heights=heights.Select(h=>h/PixelsPerMetre).ToArray()}
            };
            var punch=Strike(PonyStrikeRules.Punch,1,60,false,10,20,30,40,50);
            var kick=Strike(PonyStrikeRules.Kick,2,70,true,30,50);
            foreach(var definition in new[]{punch,kick})
            {
                WeaponCatalogLoader.Validate(definition,"PonyStrikeAuthoring");
                MineContentAuthoring.Store(definition,list);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(weapons);
            HazardAuthoring.WriteAudio(new List<JObject>
            {
                new() {["weapon"]=punch.id,["clip"]=AudioClipSource.ExportedPrefix+Sound("m_med"),["kind"]="melee",["minGain"]=.1f,["maxGain"]=.3f},
                new() {["weapon"]=kick.id,["clip"]=AudioClipSource.ExportedPrefix+Sound("m_big"),["kind"]="melee",["minGain"]=.1f,["maxGain"]=.3f}
            });
            Debug.Log($"Удар телом: punch урон {punch.damage} ({type}), kick {kick.damage}, длительность {punch.cooldown:0.###} с, контакт на {contact:0.###}.");
        }

        private static float Num(XElement element,string name)=>float.Parse((string)element.Attribute(name)??throw new InvalidDataException($"AllData punch: нет {name}."),CultureInfo.InvariantCulture);
    }
}
