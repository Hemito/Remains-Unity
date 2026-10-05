using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class NpcLootBinding {public string actor;public ContainerLootTable table;}
    [Serializable] public sealed class NpcLayout
    {public string patch,level;public ActorState[] actors;public ContentExpansion expansion;public NpcLootBinding[] loot=Array.Empty<NpcLootBinding>();}
    public sealed class RoomNpcContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия начальной расстановки NPC")] private string patch;
        [SerializeField,InspectorName("Добавочное обновление NPC")] private ContentExpansion expansion=new();
        public void Configure(string value)=>patch=value;
        public void ConfigureExpansion(string previous,string[] additions)=>expansion=new ContentExpansion
        {previousPatch=previous,additions=(string[])additions.Clone()};
        public static NpcLayout Read(RoomAuthoring room,string level)
        {
            var content=room.GetComponent<RoomNpcContent>();
            if(content==null || !content.enabled) return null;
            if(string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level)) throw new InvalidOperationException("Не задана версия или уровень NPC: "+room.name);
            var placements=room.GetComponentsInChildren<RoomActorPlacement>(true).Where(room.Included).ToArray();
            return new NpcLayout {patch=level+":"+content.patch,level=level,
                expansion=ContentExpansionRules.InLevel(content.expansion,level),actors=placements.Select(p=>p.ReadNpc(room,level)).ToArray(),
                loot=placements.Select(p=>new NpcLootBinding {actor=level+":"+p.Read(room).id,table=p.ReadInventory()}).Where(b=>b.table!=null).ToArray()};
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(patch)) Debug.LogWarning($"Комната {name}: не задана версия NPC.",this);
            if(expansion!=null && string.IsNullOrWhiteSpace(expansion.previousPatch) && expansion.additions?.Length>0)
                Debug.LogWarning($"Комната {name}: ID дополнения NPC не действуют без предыдущей версии.",this);
        }
    }
}
