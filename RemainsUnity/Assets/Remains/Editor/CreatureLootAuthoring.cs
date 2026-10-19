using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Source: Unit.setXml inter.cont, Interact.loot and LootGen.lootDrop.
    // The current Unity design keeps rolled loot in the corpse inventory.
    public static class CreatureLootAuthoring
    {
        public static void RunBatch()
        {
            try
            {
                CreatureAuthoring.Run();FactoryCrawlerAuthoring.RepairCorpsePresentation();
                ActorTypes.Load();CreatureChecks.Run();FactoryCrawlerChecks.Run();ContainerLootChecks.Run();
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static ContainerLootTable Table(string actor,string category,ContainerLootTable initial=null)
        {
            var catalog=Resources.Load<FactoryLootCatalog>("Content/FactoryLoot");
            if(catalog==null) throw new InvalidOperationException("Нет исходного каталога предметов для добычи существ.");
            var rows=new List<ContainerLootEntry>(initial?.entries??Array.Empty<ContainerLootEntry>());
            void Add(string item,float chance,string row=null,string failure=null)
            {
                string key="item:"+item;var xml=catalog.ItemXml(key);
                int count=(int?)xml.Attribute("kol")??1;
                rows.Add(new ContainerLootEntry {id=row??item,definition=key,minimum=count,maximum=count,probability=chance,afterFailure=failure});
            }
            switch(category)
            {
                case "tarakan":case "empty":case "":break; // No lootDrop branch in the original.
                case "scorp":Add("chitin_comp",1);Add("gland",.25f);Add("meat",.1f);break;
                case "slime":Add("acidslime",.75f);break;
                case "pinkslime":Add("pinkslime",.75f);break;
                case "bloodwing":Add("wingmembrane",.2f);Add("vampfang",.16f);Add("meat",.25f);break;
                case "bloodwing2":Add("wingmembrane",.3f);Add("vampfang",.2f);Add("pinkslime",.4f);break;
                case "bloat0":Add("bloatwing",.2f);Add("bloateye",.1f);break;
                case "bloat1":Add("bloatwing",.2f);Add("bloateye",.1f);Add("acidslime",.2f);break;
                case "bloat2":Add("bloatwing",.2f);Add("bloateye",.1f);Add("gland",.1f);break;
                case "bloat3":Add("bloatwing",.3f);Add("bloateye",.2f);Add("molefat",.1f);break;
                case "bloat4":Add("bloatwing",.4f);Add("bloateye",.3f);break;
                case "rat":Add("ratliver",.35f);Add("rattail",.25f);Add("meat",.1f);break;
                case "molerat":Add("ratliver",.5f);Add("molefat",1);Add("meat",.25f);break;
                case "turret":Add("scrap",.5f);Add("frag",.35f);Add("batt",.2f);Add("energ",.2f,failure:"batt");break;
                case "turret1":Add("scrap",.5f,"scrap1");Add("scrap",.5f,"scrap2");Add("frag",.85f);Add("magus_comp",.52f);
                    Add("batt",.4f);Add("energ",.8f,failure:"batt");break;
                default:throw new InvalidOperationException($"Не перенесена таблица добычи существа: actor={actor}, cont={category}.");
            }
            var table=new ContainerLootTable {id=actor+"-inventory",version="source-lootDrop-v1",entries=rows.ToArray()};table.Validate();return table;
        }
        internal static void Install(string path,string actor,XElement unit,NpcSpawnAsset spawn,ContainerLootTable initial=null)
        {
            var table=Table(actor,(string)unit.Attribute("cont")??(string)unit.Attribute("id"),initial);
            var asset=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(path);
            bool fresh=asset==null;if(fresh)asset=ScriptableObject.CreateInstance<ContainerLootAsset>();
            asset.Configure(table);
            if(fresh)AssetDatabase.CreateAsset(asset,path);else EditorUtility.SetDirty(asset);
            spawn.SetInventory(asset);EditorUtility.SetDirty(spawn);
        }
    }
}
