using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class HazardLayout
    {
        public string patch,level;
        public ContentExpansion expansion;
        public HazardState[] hazards=Array.Empty<HazardState>();
    }

    // A level's traps and charges as content placements (installed once per patch, then saved world state), as mines.
    public sealed class RoomHazardContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия расстановки ловушек")] private string patch;
        [SerializeField,InspectorName("Добавочное обновление ловушек")] private ContentExpansion expansion=new();
        public void Configure(string value)=>patch=value;
        public void ConfigureExpansion(ContentExpansion value)=>expansion=StateJson.Copy(value);
        public static HazardLayout Read(RoomAuthoring room,string level)
        {
            var content=room.GetComponent<RoomHazardContent>();
            if(content==null || !content.enabled) return null;
            if(string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException($"Комната {room.name}: не задан уровень или версия расстановки ловушек.");
            return new HazardLayout {patch=level+":"+content.patch,level=level,expansion=ContentExpansionRules.InLevel(content.expansion,level),
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
            // An older world gets only the additions of its version (ContentExpansionRules): a trap it lost stays lost.
            var selected=ContentExpansionRules.SelectedIds(world,layout.patch,layout.level,layout.expansion,layout.hazards.Select(h=>h?.id));
            if(selected!=null) layout=new HazardLayout {patch=layout.patch,level=layout.level,hazards=layout.hazards.Where(h=>selected.Contains(h.id)).ToArray()};
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
    // v2 adds the random traps of the trap points (FactoryRandomEncounters: "trap" units).
    public static class FactoryHazardAdapter
    {
        public const string PatchV1="base:factory-generated:hazards-v1",Patch="base:factory-generated:hazards-v2";
        public static int Install(FactoryLevelRoot root,IReadOnlyList<FactoryRandomEncounters.Encounter> plan)
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
            var random=new List<string>();
            foreach(var encounter in plan.Where(e=>e.Kind==RandomEncounterRules.Trap))
            {
                var definition=HazardCatalog.ForObject(encounter.Unit);
                if(definition==null) continue;   // not a hazard unit: mines and the gaps belong to the other adapters
                var node=new GameObject($"Случайная ловушка: {definition.title}: {encounter.Point.Id}");
                node.transform.SetParent(root.transform,false);node.transform.localPosition=encounter.Point.Position;
                node.AddComponent<RoomHazardPlacement>().Configure(encounter.LocalId,definition.id);
                random.Add(encounter.LocalId);count++;
            }
            // A world of v1 gets only the random traps; without any, the layout is v1 as before.
            var content=root.gameObject.AddComponent<RoomHazardContent>();content.Configure(random.Count>0?Patch:PatchV1);
            if(random.Count>0) content.ConfigureExpansion(new ContentExpansion {previousPatch=PatchV1,additions=random.ToArray()});
            Debug.Log($"Завод {root.Prepared.Physics.id}: случайных ловушек поставлено {random.Count}.");
            return count;
        }
        private const string CreatureObjectPrefix="remains:obj:";
    }
}
