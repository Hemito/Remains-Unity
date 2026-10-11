using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Content
{
    // Explicitly unlocked placements only. Original randomized locks need a separate
    // persisted lock contract; never silently turn these into ordinary unlocked doors.
    public sealed class FactoryDoorAdapter : MonoBehaviour
    {
        [SerializeField,InspectorName("Деревянная дверь")] private DoorView wood;
        [SerializeField,InspectorName("Металлическая дверь")] private DoorView metal;
        [SerializeField,InspectorName("Большая металлическая дверь")] private DoorView large;
        [SerializeField,InspectorName("Напольный люк")] private DoorView hatch;
        private bool installed;
        public int Installed {get;private set;}
        public void Configure(DoorView wooden,DoorView metallic,DoorView big,DoorView floorHatch)
        {wood=wooden;metal=metallic;large=big;hatch=floorHatch;}
        public void Install(FactoryLevelRoot root)
        {
            if(installed || root==null || root.Prepared==null || root.transform!=transform || root.Rooms.childCount!=root.Prepared.Grid.Rooms.Count)
                throw new InvalidOperationException("Адаптер дверей: нужен подготовленный корень; повторная установка запрещена.");
            if(wood==null || metal==null || large==null || hatch==null)
                throw new InvalidOperationException("Адаптер дверей: не заданы сериализованные префабы дверей и люка.");
            var skipped=new Dictionary<string,List<string>>(StringComparer.Ordinal);
            var accepted=new List<(FactoryPlacedObject source,DoorView prefab,bool open)>();
            foreach(var placement in root.Objects)
            {
                var obj=placement.Source;
                var prefab=obj.Type switch {"remains:obj:door1"=>wood,"remains:obj:door2"=>metal,"remains:obj:door3"=>large,"remains:obj:hatch1"=>hatch,_=>null};
                if(prefab==null) continue;
                string reason=null;
                if(!obj.Params.TryGetValue("lock",out var value) || value!="0")
                    reason="Исходный замок (в том числе случайный замок типа) ещё не поддержан";
                else if(obj.Items.Count!=0 || obj.Links.Count!=0 || obj.Extra!=null || obj.Params.Keys.Any(key=>key!="lock" && key!="open"))
                    reason="Параметры, содержимое или связи двери ещё не поддержаны";
                if(reason!=null)
                {
                    if(!skipped.TryGetValue(reason,out var ids)) skipped.Add(reason,ids=new List<string>());
                    ids.Add($"{placement.Id} ({obj.Type}, {placement.Room.Template.Template}, code={obj.Code})");
                    continue;
                }
                if(prefab.Definition==null) throw new InvalidOperationException("Адаптер дверей: у префаба нет определения: "+obj.Type);
                prefab.Definition.Validate();
                if(prefab.Definition.CapabilityVersion!=1 || prefab.Definition.Id!=obj.Type.Substring("remains:obj:".Length))
                    throw new InvalidOperationException("Адаптер дверей: префаб не соответствует типу "+obj.Type);
                // Interact.as treats presence of @open as the initial open instruction.
                accepted.Add((placement,prefab,obj.Params.ContainsKey("open")));
            }
            var created=new List<GameObject>();
            try
            {
                foreach(var entry in accepted)
                {
                    var view=Instantiate(entry.prefab,transform,false);created.Add(view.gameObject);
                    view.name="Дверь: "+entry.source.Id;
                    view.transform.localPosition=entry.source.Position;
                    if(entry.prefab==hatch) view.Artwork.flipX=entry.source.Room.Cell.Mirror;
                    string localId=$"{entry.source.Room.Cell.X},{entry.source.Room.Cell.Y}:{entry.source.Source.Id}";
                    view.gameObject.AddComponent<RoomDoorAuthoring>().Configure(localId,entry.open,entry.prefab==wood || entry.prefab==hatch?"door:wood":"floor");
                }
                if(GetComponent<RoomDoorContent>()!=null) throw new InvalidOperationException("Корень завода уже содержит расстановку дверей.");
                // A new additive patch installs hatches in existing worlds; the installer
                // validates and preserves all previously installed door states.
                var content=gameObject.AddComponent<RoomDoorContent>();
                var hatches=accepted.Where(entry=>entry.prefab==hatch)
                    .Select(entry=>$"{entry.source.Room.Cell.X},{entry.source.Room.Cell.Y}:{entry.source.Source.Id}").ToArray();
                content.Configure(hatches.Length==0?"base:factory-generated:doors-v1":"base:factory-generated:doors-v2");
                if(hatches.Length>0) content.ConfigureExpansion(new ContentExpansion {previousPatch="base:factory-generated:doors-v1",additions=hatches});
                Installed=accepted.Count;installed=true;
            }
            catch
            {
                foreach(var item in created) {if(Application.isPlaying) Destroy(item);else DestroyImmediate(item);}
                throw;
            }
            foreach(var entry in accepted) root.HideObjectArtwork(entry.source);
            foreach(var pair in skipped)
                Debug.LogWarning($"Завод {root.Prepared.Physics.id}: {pair.Key}; пропущено {pair.Value.Count} дверей. ID: {string.Join("; ",pair.Value)}",this);
        }
    }
}
