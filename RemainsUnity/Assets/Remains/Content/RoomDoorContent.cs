using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class DoorLayout {public string patch,level;public DoorState[] doors;public ContentExpansion expansion;}
    public sealed class RoomDoorContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия расстановки дверей (устойчивый ID)")] private string patch;
        [SerializeField,InspectorName("Добавочное обновление дверей")] private ContentExpansion expansion=new();
        public void Configure(string value)=>patch=value;
        public void ConfigureExpansion(ContentExpansion value)=>expansion=Remains.Serialization.StateJson.Copy(value);
        public static DoorLayout Read(RoomAuthoring room,string level)
        {
            var placements=room.GetComponentsInChildren<RoomDoorAuthoring>(true).Where(room.Included).ToArray();
            var content=room.GetComponent<RoomDoorContent>();
            if(content==null && placements.Length==0) return null; // Room intentionally has no authored doors.
            if(content==null || string.IsNullOrWhiteSpace(content.patch))
                throw new InvalidOperationException($"Комната {room.name}: у дверей нет версии расстановки.");
            if(!content.enabled) return null;
            return new DoorLayout {patch=level+":"+content.patch,level=level,expansion=ContentExpansionRules.InLevel(content.expansion,level),doors=placements.Select(p=>
            {
                var door=p.Read(room,level);door.id=level+":"+door.id;return door;
            }).ToArray()};
        }
        private void OnValidate()
        {if(string.IsNullOrWhiteSpace(patch)) Debug.LogWarning($"Комната {name}: не задана версия расстановки дверей.",this);}
    }
}
