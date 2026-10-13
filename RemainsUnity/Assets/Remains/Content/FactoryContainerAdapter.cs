using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    public sealed class FactoryContainerAdapter : MonoBehaviour
    {
        public const string Patch="base:factory-generated:physical-v2-loot-v1";
        [SerializeField,InspectorName("Каталог контейнеров и исходного лута")] private FactoryLootCatalog catalog;
        public int Installed {get;private set;}
        private bool installed;
        public void Configure(FactoryLootCatalog value)=>catalog=value;
        public void Install(FactoryLevelRoot root,WorldState world)
        {
            if(installed||catalog==null||root==null||root.Prepared==null||world==null)throw new InvalidOperationException("Контейнеры завода: нужны исходный каталог, подготовленный корень и мир; повторная установка запрещена.");
            catalog.Prepare();
            var types=catalog.definitions.ToDictionary(value=>"remains:obj:"+value.id,StringComparer.Ordinal);
            var limits=new Dictionary<string,int>(world.factoryLootLimits,StringComparer.Ordinal);
            var artwork=new Remains.Presentation.InventoryArtwork();
            var additions=new List<string>();var warnings=new List<string>();
            var nodes=new List<GameObject>();
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                if(!types.TryGetValue(placement.Source.Type,out var definition))continue;
                var allowed=new HashSet<string>(new[]{"cont","lock","lockch","low","mine","minech","locklevel","locktip","minetip","prize"},StringComparer.Ordinal);
                if(definition.physical==null||placement.Source.Links.Count>0||placement.Source.Extra!=null||placement.Source.Params.Keys.Any(key=>!allowed.Contains(key)))
                {warnings.Add($"{placement.Id}: type={definition.id}, неподдержанные параметры/связи или физическое определение");continue;}
                var before=new Dictionary<string,int>(limits,StringComparer.Ordinal);
                ContainerState state;
                try
                {
                    var saved=world.containers.Find(value=>value.id==placement.Id);
                    state=saved!=null?StateJson.Copy(saved):FactoryContainerLoot.Generate(catalog,world,placement,definition,root.Prepared.Grid.Layout.Index,limits,artwork);
                    state.level=root.Prepared.Level;
                }
                catch(NotSupportedException error)
                {limits=before;warnings.Add($"{placement.Id}: type={definition.id}, {error.Message}");continue;}
                var node=new GameObject("Контейнер: "+placement.Id);nodes.Add(node);
                node.transform.SetParent(transform,false);node.transform.localPosition=placement.Position;
                string localId=$"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}";
                var marker=node.AddComponent<RoomContainerPlacement>();marker.Configure(localId,definition.physical);marker.SetSourceState(state);
                additions.Add(localId);root.HideObjectArtwork(placement);
            }
            var content=root.GetComponent<RoomPhysicalContent>()??throw new InvalidOperationException("Контейнеры завода: сначала подготовьте физические объекты.");
            content.Configure(Patch);content.ConfigureExpansion("base:factory-generated:physical-v1",additions.ToArray());
            Installed=additions.Count;installed=true;
            foreach(string issue in warnings)Debug.LogWarning($"Контейнер завода пока не подключён: world={world.worldId}, seed={world.seed}, {issue}.",this);
        }
    }
}
