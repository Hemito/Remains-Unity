using System;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class CreatureDeathLootAuthoring
    {
        public static ContainerLootAsset Install(string actor,XElement unit)
        {
            string path="Assets/Remains/Content/Creatures/"+actor+"-death-loot.asset";
            var table=CreatureLootAuthoring.Table(actor,(string)unit.Attribute("cont")??(string)unit.Attribute("id"));
            table.id=actor+"-death-loot";
            foreach(var row in table.entries)DroppedItemShapeCatalog.Require(row.definition);
            var asset=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(path);bool fresh=asset==null;
            if(fresh)asset=ScriptableObject.CreateInstance<ContainerLootAsset>();asset.Configure(table);
            if(fresh)AssetDatabase.CreateAsset(asset,path);else EditorUtility.SetDirty(asset);
            return asset;
        }
        public static void RunBatch()
        {
            try
            {
                var catalog=CreatureCatalog.Load();var source=ExportedContentCatalog.Load();int count=0;
                foreach(var entry in catalog.Creatures.Where(c=>c.family==NpcCreatureBehaviours.Turret))
                {
                    var unit=XElement.Parse(source.Require("unit:"+entry.sourceId).rawXml);
                    entry.deathLoot=Install(entry.actorType,unit);entry.deathLootHeight=(float)unit.Element("phis").Attribute("sY")/50;
                    entry.notes=entry.notes.Where(n=>n!="добыча (cont) не перенесена").ToArray();count++;
                }
                EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();CreatureDeathLootChecks.Run();
                Debug.Log($"Турели: подготовлено отдельных таблиц принятой гибели {count}; установленное оружие не входит в добычу.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
