using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class FactoryContainerDefinition
    {
        [InspectorName("Исходный ID")] public string id;
        [InspectorName("Исходные параметры XML")] public string rawXml;
        [InspectorName("Физическое определение")] public PropAsset physical;
    }

    [CreateAssetMenu(menuName="Remains/Исходный лут завода")]
    public sealed class FactoryLootCatalog : ScriptableObject
    {
        [InspectorName("Исходный каталог предметов")] public ExportedContentCatalog source;
        [InspectorName("Хеш исходных определений")] public string sourceHash;
        [InspectorName("Типы контейнеров")] public FactoryContainerDefinition[] definitions=Array.Empty<FactoryContainerDefinition>();
        [InspectorName("Базовая сложность мира")] public double baseDifficulty=1;
        [InspectorName("Добавка сложности на полосу")] public double depthDifficulty=1;
        [InspectorName("Этап доступности предметов")] public int gameStage=1;
        private Dictionary<string,XElement> objectIndex,itemIndex,weaponIndex;
        private Dictionary<string,List<PoolEntry>> pools;
        private struct PoolEntry
        {
            public string id;
            public double weight,level,stage,worth;
        }
        public void Configure(ExportedContentCatalog source,string hash,FactoryContainerDefinition[] definitions)
        {this.source=source;sourceHash=hash;this.definitions=definitions;Reset();Prepare();}
        private void Reset(){objectIndex=null;itemIndex=null;weaponIndex=null;pools=null;}
        private void OnValidate()
        {
            baseDifficulty=Math.Max(0,baseDifficulty);depthDifficulty=Math.Max(0,depthDifficulty);gameStage=Math.Max(0,gameStage);Reset();
            try {Prepare();}
            catch(Exception error) when(error is InvalidOperationException||error is System.Xml.XmlException||error is FormatException)
            {Debug.LogWarning($"Каталог лута {name}: {error.Message}",this);}
        }
        public void Prepare()
        {
            if(objectIndex!=null)return;
            if(source==null||string.IsNullOrWhiteSpace(sourceHash)||definitions==null)
                throw new InvalidOperationException("Не задан исходный каталог/хеш/типы контейнеров завода.");
            source.Validate();
            var objects=new Dictionary<string,XElement>(StringComparer.Ordinal);
            var items=new Dictionary<string,XElement>(StringComparer.Ordinal);
            var weapons=new Dictionary<string,XElement>(StringComparer.Ordinal);
            var nextPools=new Dictionary<string,List<PoolEntry>>(StringComparer.Ordinal);
            foreach(var definition in definitions)
            {
                if(definition==null||string.IsNullOrWhiteSpace(definition.id)||string.IsNullOrWhiteSpace(definition.rawXml))
                    throw new InvalidOperationException("Пустое определение контейнера завода.");
                if(!objects.TryAdd(definition.id,XElement.Parse(definition.rawXml)))
                    throw new InvalidOperationException("Повторное определение контейнера: "+definition.id);
            }
            void Add(string pool,string id,XElement xml,double weight,XElement parameters=null)
            {
                if(string.IsNullOrEmpty(pool))return;
                if(double.IsNaN(weight)||double.IsInfinity(weight)||weight<0)
                    throw new InvalidOperationException($"Некорректный вес лута: pool={pool}, item={id}, weight={weight}.");
                if(!nextPools.TryGetValue(pool,out var entries))nextPools.Add(pool,entries=new List<PoolEntry>());
                entries.Add(new PoolEntry {id=id,weight=weight,level=Number(xml,"lvl"),stage=Number(parameters??xml,"stage"),worth=Number(parameters??xml,"worth")});
            }
            foreach(var entry in source.items)
            {
                if(entry.category!="item"&&entry.category!="weapon")continue;
                var xml=XElement.Parse(entry.rawXml);
                string id=entry.sourceId;
                var index=entry.category=="weapon"?weapons:items;
                if(!index.TryAdd(id,xml))throw new InvalidOperationException("Повторное определение лута: "+entry.key);
                if(entry.category=="weapon")
                {
                    double tip=Number(xml,"tip");var common=xml.Element("com");
                    if(tip>0&&tip<4&&common!=null)
                    {
                        Add("weapon",id,xml,Number(common,"chance"),common);
                        if(common.Attribute("uniq")!=null)Add("uniq",id+"^1",xml,Number(common,"uniq"),common);
                    }
                }
                else
                {
                    Add((string)xml.Attribute("tip"),id,xml,Number(xml,"chance",1));
                    Add((string)xml.Attribute("tip2"),id,xml,Number(xml,"chance2",Number(xml,"chance")));
                }
            }
            objectIndex=objects;itemIndex=items;weaponIndex=weapons;pools=nextPools;
        }
        public XElement DefinitionXml(string id)
        {Prepare();return objectIndex.TryGetValue(id,out var xml)?xml:throw new InvalidOperationException("Нет исходного типа контейнера: "+id);}
        public XElement ItemXml(string id)
        {
            Prepare();if(string.IsNullOrEmpty(id))throw new InvalidOperationException("Пустой ID предмета лута.");
            if(id.StartsWith("item:",StringComparison.Ordinal))return RequireXml(itemIndex,id.Substring(5),id);
            if(id.StartsWith("weapon:",StringComparison.Ordinal))return RequireXml(weaponIndex,BaseId(id.Substring(7)),id);
            id=BaseId(id);
            if(itemIndex.TryGetValue(id,out var xml)||weaponIndex.TryGetValue(id,out xml))return xml;
            throw new InvalidOperationException("Нет исходных параметров предмета лута: "+id);
        }
        private static XElement RequireXml(Dictionary<string,XElement> index,string key,string original)
        =>index.TryGetValue(key,out var xml)?xml:throw new InvalidOperationException("Нет исходных параметров предмета лута: "+original);
        private static string BaseId(string id)
        {int suffix=id.IndexOf('^');return suffix<0?id:id.Substring(0,suffix);}
        public string Select(string pool,double maxLevel,int worth,Func<double> random)
        {
            Prepare();if(random==null)throw new ArgumentNullException(nameof(random));
            if(!pools.TryGetValue(pool,out var entries))return null;
            bool filter=pool!="book"&&(maxLevel>0||worth>0||gameStage>0);
            double total=0;int count=0;PoolEntry only=default;
            foreach(var entry in entries)
            {
                if(filter&&((gameStage>0&&entry.stage>gameStage)||(maxLevel!=-100&&entry.level>maxLevel)||(worth!=-100&&entry.worth!=worth)))continue;
                total+=entry.weight;count++;only=entry;
            }
            if(count==0)return null;
            // Original returns a single eligible item even when its chance is zero.
            if(count==1)return only.id;
            double sample=random()*total,cumulative=0;
            foreach(var entry in entries)
            {
                if(filter&&((gameStage>0&&entry.stage>gameStage)||(maxLevel!=-100&&entry.level>maxLevel)||(worth!=-100&&entry.worth!=worth)))continue;
                cumulative+=entry.weight;if(cumulative>sample)return entry.id;
            }
            return null;
        }
        private static double Number(XElement xml,string attribute,double missing=0)
        {var value=xml.Attribute(attribute);return value==null?missing:double.Parse(value.Value,CultureInfo.InvariantCulture);}
    }
}
