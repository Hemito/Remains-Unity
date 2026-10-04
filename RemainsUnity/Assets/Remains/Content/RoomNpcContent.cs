using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class NpcLayout {public string patch,level;public ActorState[] actors;}
    public sealed class RoomNpcContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия начальной расстановки NPC")] private string patch;
        public void Configure(string value)=>patch=value;
        public static NpcLayout Read(RoomAuthoring room,string level)
        {
            var content=room.GetComponent<RoomNpcContent>();
            if(content==null || !content.enabled) return null;
            if(string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level)) throw new InvalidOperationException("Не задана версия или уровень NPC: "+room.name);
            return new NpcLayout {patch=level+":"+content.patch,level=level,actors=room.GetComponentsInChildren<RoomActorPlacement>(true)
                .Where(room.Included).Select(p=>p.ReadNpc(room,level)).ToArray()};
        }
    }
}
