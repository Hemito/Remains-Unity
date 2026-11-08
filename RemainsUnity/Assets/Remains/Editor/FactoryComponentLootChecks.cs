using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Keep an empty stage-gated pool distinct from broken catalog wiring. The archive's observed safe warning
    // occurs at stage 1; no positive-weight compp entry is available until stage 3 in the exported source.
    public static class FactoryComponentLootChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var source=GameAssets.Load<FactoryLootCatalog>("Content/FactoryLoot");
            var catalog=UnityEngine.Object.Instantiate(source);
            try
            {
                var entries=catalog.source.items.Where(e=>e.category=="item")
                    .Select(e=>(entry:e,xml:XElement.Parse(e.rawXml)))
                    .Where(e=>(string)e.xml.Attribute("tip")=="compp").ToArray();
                Require(entries.Length>1,"нет исходного пула compp");
                double Number(XElement xml,string key,double missing)=>xml.Attribute(key)==null?missing:
                    double.Parse(xml.Attribute(key).Value,CultureInfo.InvariantCulture);
                catalog.gameStage=1;
                Require(entries.Where(e=>Number(e.xml,"stage",0)<=1).All(e=>Number(e.xml,"chance",1)==0),
                    "исходный состав compp stage1 изменился: перепроверьте отсутствие выдачи");
                for(int i=0;i<100;i++)
                {
                    double roll=(i+.5)/100;
                    Require(catalog.Select("compp",-100,-100,()=>roll)==null,"нулевые веса начали выдавать случайный компонент");
                }
                catalog.gameStage=3;
                var expected=entries.Where(e=>Number(e.xml,"stage",0)<=3&&Number(e.xml,"chance",1)>0)
                    .Select(e=>e.entry.sourceId).ToHashSet(StringComparer.Ordinal);
                Require(expected.Count>1,"нет положительных весов compp stage3");
                var seen=new HashSet<string>(StringComparer.Ordinal);
                var world=new WorldState {worldId="component-loot-proof",seed=104};
                var container=new ContainerState {id="safe",lootCategory="safe",lootGenerationVersion=FactoryContainerLoot.Version};
                world.containers.Add(container);
                for(int i=0;i<100;i++)
                {
                    double roll=(i+.5)/100;
                    string id=catalog.Select("compp",-100,-100,()=>roll);
                    Require(id!=null&&expected.Contains(id),"compp обошёл stage/нулевой вес: "+id);
                    if(!seen.Add(id))continue;
                    var item=catalog.source.CreateFormalItem("item:"+id,0,1);
                    item.id="component-proof:"+id;container.items.Add(item);
                }
                Require(seen.SetEquals(expected),"контролируемый RNG не охватил положительные компоненты stage3");
                string saved=StateJson.ToJson(world);
                var restored=StateJson.FromJson<WorldState>(saved);
                Require(StateJson.ToJson(restored)==saved&&restored.containers.Single().items.Count==expected.Count,
                    "компоненты сейфа потерялись после сохранения");
                // herbs is an explicit request in these branches, not a random zero-weight compp pick.
                Require(catalog.ItemXml("herbs")!=null,"нет явного herbs");
                foreach(string category in new[]{"food","fridge"})
                {
                    var requests=new List<FactoryLootRequest>();
                    var rolls=new Queue<double>(category=="food"?new[]{.3,.25,.25,.25,.1,.25}:new[]{.25,.25,.25,.3,.25,.25,.25});
                    FactoryLootRules.Generate(category,4,1,50,()=>rolls.Dequeue(),r=>{requests.Add(r);return true;},_=>{});
                    Require(requests.Any(r=>r.pool=="compp"&&r.sourceId=="herbs"&&r.count==2),category+": потерян явный herbs ×2");
                }
                var safe=new List<FactoryLootRequest>();
                FactoryLootRules.Generate("safe",4,1,100,()=>.5,r=>{safe.Add(r);return true;},_=>{});
                Require(safe.Count(r=>r.pool=="compp"&&r.sourceId==null)==1,"safe не запросил случайный compp");
                Debug.Log($"FactoryComponentLootChecks: PASS — stage1 empty at 100 controlled rolls; stage3 {seen.Count} positive entries; explicit herbs; safe pool request; saved container roundtrip. Full factory generation/Player acceptance separate.");
            }
            finally {UnityEngine.Object.DestroyImmediate(catalog);}
        }
        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException("compp: "+message);}
    }
}
