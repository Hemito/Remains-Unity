using System;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class HazardLayout
    {
        public string patch,level;
        public HazardState[] hazards=Array.Empty<HazardState>();
    }

    // A level's traps and charges as content placements (installed once per patch, then saved world state), as mines.
    public sealed class RoomHazardContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия расстановки ловушек")] private string patch;
        public void Configure(string value)=>patch=value;
        public static HazardLayout Read(RoomAuthoring room,string level)
        {
            var content=room.GetComponent<RoomHazardContent>();
            if(content==null || !content.enabled) return null;
            if(string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException($"Комната {room.name}: не задан уровень или версия расстановки ловушек.");
            return new HazardLayout {patch=level+":"+content.patch,level=level,
                hazards=room.GetComponentsInChildren<RoomHazardPlacement>(true).Where(room.Included).Select(p=>p.Read(room,level)).ToArray()};
        }
        private void OnValidate() {if(string.IsNullOrWhiteSpace(patch)) Debug.LogWarning($"Комната {name}: не задана версия расстановки ловушек.",this);}
    }

    public sealed class RoomHazardPlacement : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID ловушки")] private string stableId;
        [SerializeField,InspectorName("Существо оригинала (HazardCatalog)")] private string definition;
        public void Configure(string id,string unit) {stableId=id;definition=unit;}
        public HazardState Read(RoomAuthoring room,string level)
        {
            var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(definition))
                throw new InvalidOperationException($"Ловушка {name}: нужны устойчивый ID и описание.");
            return HazardRules.Create(level+":"+stableId,level,HazardCatalog.Get(definition),new Vector2(rect.x,rect.y));
        }
    }

    public static class HazardContentInstaller
    {
        public static bool NeedsInstall(WorldState world,HazardLayout layout)=>layout!=null && !world.contentPatches.Contains(layout.patch);
        public static bool Apply(WorldState world,HazardLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.hazards==null)
                throw new InvalidOperationException("Неполная расстановка ловушек.");
            if(!NeedsInstall(world,layout)) return false;
            ContentEntityIds.Preflight(world,layout.hazards.Select(h=>new ContentEntityIdentity(ContentEntityKind.Hazard,h?.id)),layout.patch);
            foreach(var hazard in layout.hazards)
            {
                if(hazard==null || hazard.level!=layout.level) throw new InvalidOperationException("Ловушка другой локации в расстановке: "+hazard?.id);
                HazardRules.Validate(hazard);
                // One already in the world (saved: closed, damaged, rearmed) is not placed again.
                if(world.hazards.Exists(saved=>saved.id==hazard.id)) continue;
                world.hazards.Add(StateJson.Copy(hazard));
            }
            world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }

    // The generated factory's traps and charges from its room templates (original Location.createUnit): an object
    // whose unit is a hazard (HazardCatalog.ForObject: "trap" → mtrap, "expl1" → damexpl1) is one level hazard.
    public static class FactoryHazardAdapter
    {
        public const string Patch="base:factory-generated:hazards-v1";
        public static int Install(FactoryLevelRoot root)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Ловушки завода: нужен подготовленный корень.");
            if(root.GetComponent<RoomHazardContent>()!=null) throw new InvalidOperationException("Ловушки завода: повторная установка запрещена.");
            int count=0;
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                string type=placement.Source.Type;
                if(!type.StartsWith(CreatureObjectPrefix,StringComparison.Ordinal)) continue;
                var definition=HazardCatalog.ForObject(type.Substring(CreatureObjectPrefix.Length));
                if(definition==null) continue;
                // A charge linked to a trigger (allid: UnitDamager.command "dam") waits for the triggers; it is skipped.
                if(placement.Source.Params.Count>0 || placement.Source.Items.Count>0 || placement.Source.Links.Count>0 || placement.Source.Extra!=null)
                {Debug.LogWarning($"Ловушка завода с параметрами или связями пока не поддержана и пропущена: {placement.Id} ({type}).");continue;}
                var node=new GameObject($"{definition.title}: {placement.Id}");
                node.transform.SetParent(root.transform,false);node.transform.localPosition=placement.Position;
                node.AddComponent<RoomHazardPlacement>().Configure($"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}",definition.id);
                root.HideObjectArtwork(placement);
                count++;
            }
            root.gameObject.AddComponent<RoomHazardContent>().Configure(Patch);
            return count;
        }
        private const string CreatureObjectPrefix="remains:obj:";
    }
}
