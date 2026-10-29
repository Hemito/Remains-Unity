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
    // Spikes from the original data in one pass: every AllData obj with tip='trap' (loc/Trap.as getXmlParam) becomes a
    // SpikeDefinition — box sX or size × tileX wide, sY or wid × tileY high (50 px per metre), damage and tipdam,
    // att 1 falling / 2 rising, floor (box on the cell's bottom).
    //   batch -executeMethod Remains.Editor.SpikeAuthoring.RunBatch
    public static class SpikeAuthoring
    {
        private const string CatalogPath="Assets/Remains/Resources/"+SpikeCatalog.Resource+".asset";
        private const float PixelsPerMetre=50,TilePixels=40;   // World.tileX = World.tileY

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var catalog=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var definitions=new List<SpikeDefinition>();
            foreach(var row in ((JArray)catalog["definitions"]).OfType<JObject>().Where(r=>(string)r["category"]=="obj"))
            {
                var obj=XElement.Parse((string)row["rawXml"]);
                if((string)obj.Attribute("tip")!="trap") continue;
                string id=(string)row["id"];
                int att=(int)Num(obj,"att",1);
                var definition=new SpikeDefinition
                {
                    id=id,title=((string)obj.Attribute("n")??id).Trim(),objects=new[]{id},
                    attack=att switch {1=>SpikeDefinition.Fall,2=>SpikeDefinition.Rise,_=>throw new InvalidDataException($"Шипы {id}: неизвестный способ удара att={att}.")},
                    damage=Num(obj,"damage"),damageType=DamageTypes.FromOriginal((int)Num(obj,"tipdam",0)),
                    width=(Num(obj,"sX",0)>0?Num(obj,"sX"):Num(obj,"size")*TilePixels)/PixelsPerMetre,
                    height=(Num(obj,"sY",0)>0?Num(obj,"sY"):Num(obj,"wid")*TilePixels)/PixelsPerMetre,
                    bottomAnchored=obj.Attribute("floor")!=null
                };
                definition.Validate();
                definitions.Add(definition);
            }
            if(definitions.Count==0) throw new InvalidDataException("В выгрузке оригинала нет шипов (obj tip='trap').");
            definitions.Sort((a,b)=>string.CompareOrdinal(a.id,b.id));
            var asset=AssetDatabase.LoadAssetAtPath<SpikeCatalog>(CatalogPath);
            if(asset==null) {asset=ScriptableObject.CreateInstance<SpikeCatalog>();AssetDatabase.CreateAsset(asset,CatalogPath);}
            asset.Configure(definitions.ToArray());
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            Debug.Log("Каталог шипов: "+string.Join("; ",definitions.Select(d=>$"{d.id} {d.attack} урон {d.damage} {d.damageType} {d.width}×{d.height} м{(d.bottomAnchored?" снизу клетки":"")}")));
        }

        private static float Num(XElement node,string name,float? fallback=null)
        {
            var value=(string)node.Attribute(name);
            if(value==null) return fallback??throw new InvalidDataException($"Шипы {(string)node.Attribute("id")}: нет атрибута {name}.");
            return float.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture);
        }
    }
}
