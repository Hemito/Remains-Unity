using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class ContainerLootBinding {public string container;public ContainerLootTable table;}
    [Serializable] public sealed class PhysicalExpansion : ContentExpansion {}
    [Serializable] public sealed class PhysicalLayout
    {
        public string patch,level;
        public PhysicalExpansion expansion;
        public PropState[] props=Array.Empty<PropState>();
        public ContainerState[] containers=Array.Empty<ContainerState>();
        public ContainerLootBinding[] loot=Array.Empty<ContainerLootBinding>();
    }
    public sealed class RoomPhysicalContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия расстановки физических объектов")] private string patch;
        [SerializeField,InspectorName("Добавочное обновление")] private PhysicalExpansion expansion=new();
        public void Configure(string value)=>patch=value;
        public void ConfigureExpansion(string previous,string[] additions)=>ConfigureExpansion(previous,additions,Array.Empty<ContentExpansionStep>());
        public void ConfigureExpansion(string previous,string[] additions,ContentExpansionStep[] earlierVersions)
        {
            if(additions==null || earlierVersions==null) throw new ArgumentNullException(additions==null?nameof(additions):nameof(earlierVersions));
            expansion=Remains.Serialization.StateJson.Copy(new PhysicalExpansion
                {previousPatch=previous,additions=additions,earlierVersions=earlierVersions});
        }
        public static PhysicalLayout Read(RoomAuthoring room,string level)
        {
            var content=room.GetComponent<RoomPhysicalContent>();
            if(content==null || !content.enabled) return null; // Training rooms keep their explicit installer.
            if(string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException($"Комната {room.name}: не задан уровень или версия физических объектов.");
            var layout=new PhysicalLayout {patch=level+":"+content.patch,level=level};
            layout.expansion=ContentExpansionRules.InLevel(content.expansion,level);
            layout.props=room.GetComponentsInChildren<RoomPropPlacement>(true).Where(room.Included).Select(p=>ReadProp(p,room,level,false)).ToArray();
            layout.containers=room.GetComponentsInChildren<RoomContainerPlacement>(true).Where(room.Included).Select(p=>
            {
                var state=(ContainerState)ReadProp(p,room,level,true);
                var generated=p.ReadSourceState();
                if(generated!=null)
                {
                    if(generated.id!=state.id||generated.level!=state.level)throw new InvalidOperationException("Не совпадает идентичность исходного контейнера: "+state.id);
                    state.items=generated.items;state.extensions=generated.extensions;state.lockLevel=generated.lockLevel;state.mineLevel=generated.mineLevel;
                    state.lootCategory=generated.lootCategory;state.lootGenerationVersion=generated.lootGenerationVersion;return state;
                }
                var items=p.ReadInitialItems();
                if(items==null) throw new InvalidOperationException("Не задан список содержимого: "+state.id);
                foreach(var item in items)
                {
                    if(item==null || string.IsNullOrWhiteSpace(item.id)) throw new InvalidOperationException("Не задан ID начального предмета: "+state.id);
                    item.id=state.id+":"+item.id;
                    state.items.Add(item);
                }
                return state;
            }).ToArray();
            layout.loot=room.GetComponentsInChildren<RoomContainerPlacement>(true).Where(room.Included)
                .Select(p=>new ContainerLootBinding {container=level+":"+p.Read(room).id,table=p.ReadLoot()}).Where(b=>b.table!=null).ToArray();
            return layout;
        }
        private static PropState ReadProp(RoomPhysicalPlacement marker,RoomAuthoring room,string level,bool container)
        {
            var placement=marker.Read(room);var definition=marker.Definition.CreateDefinition();
            PropState state=container?new ContainerState():new PropState();
            state.id=level+":"+placement.id;state.definition=definition.id;state.level=level;
            state.x=placement.x;state.y=placement.y;state.anchorHeight=0;
            state.mass=definition.mass;state.width=definition.width;state.height=definition.height;
            state.movable=definition.movable;state.hasPhysicalBody=definition.hasPhysicalBody;
            PropBodyRules.Validate(state);
            return state;
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(patch)) Debug.LogWarning($"Комната {name}: не задана версия физических объектов.",this);
            if(expansion!=null && string.IsNullOrWhiteSpace(expansion.previousPatch) && (expansion.additions?.Length>0 || expansion.earlierVersions?.Length>0))
                Debug.LogWarning($"Комната {name}: ID дополнения не действуют без предыдущей версии расстановки.",this);
        }
    }
}
