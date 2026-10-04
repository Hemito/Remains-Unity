using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class PhysicalLayout
    {
        public string patch,level;
        public PropState[] props=Array.Empty<PropState>();
        public ContainerState[] containers=Array.Empty<ContainerState>();
    }
    public sealed class RoomPhysicalContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия расстановки физических объектов")] private string patch;
        public void Configure(string value)=>patch=value;
        public static PhysicalLayout Read(RoomAuthoring room,string level)
        {
            var content=room.GetComponent<RoomPhysicalContent>();
            if(content==null || !content.enabled) return null; // Training rooms keep their explicit installer.
            if(string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException($"Комната {room.name}: не задан уровень или версия физических объектов.");
            var layout=new PhysicalLayout {patch=level+":"+content.patch,level=level};
            layout.props=room.GetComponentsInChildren<RoomPropPlacement>(true).Where(room.Included).Select(p=>ReadProp(p,room,level,false)).ToArray();
            layout.containers=room.GetComponentsInChildren<RoomContainerPlacement>(true).Where(room.Included).Select(p=>
            {
                var state=(ContainerState)ReadProp(p,room,level,true);
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
            return layout;
        }
        private static PropState ReadProp(RoomPhysicalPlacement marker,RoomAuthoring room,string level,bool container)
        {
            var placement=marker.Read(room);var definition=marker.Definition.CreateDefinition();
            PropState state=container?new ContainerState():new PropState();
            state.id=level+":"+placement.id;state.definition=definition.id;state.level=level;
            state.x=placement.x;state.y=placement.y;state.anchorHeight=0;
            state.mass=definition.mass;state.width=definition.width;state.height=definition.height;
            return state;
        }
        private void OnValidate()
        {if(string.IsNullOrWhiteSpace(patch)) Debug.LogWarning($"Комната {name}: не задана версия физических объектов.",this);}
    }
}
