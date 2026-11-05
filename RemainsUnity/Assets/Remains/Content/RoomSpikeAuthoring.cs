using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // Spikes of a room as level geometry (RoomLayout.spikes): fixed, the same on every peer, not saved. The node stands
    // on the bottom centre of the spikes' cell; the box comes from the definition (SpikeCatalog).
    public sealed class RoomSpikeAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID шипов")] private string stableId;
        [SerializeField,InspectorName("Описание (SpikeCatalog)")] private string definition;
        [SerializeField,Min(0),InspectorName("Высота клетки, м")] private float cell=.8f;
        [SerializeField,Min(0),InspectorName("Опасность комнаты (locDifLevel)")] private float danger;
        public void Configure(string id,string spikes,float cellHeight,float roomDanger) {stableId=id;definition=spikes;cell=cellHeight;danger=roomDanger;}
        public RoomSpike Read(RoomAuthoring root)
        {
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(definition) || !(cell>0) || !float.IsFinite(danger) || danger<0)
                throw new InvalidOperationException($"Шипы {name}: нужны устойчивый ID, описание, высота клетки и опасность.");
            var spikes=SpikeCatalog.Get(definition);
            var matrix=root.transform.worldToLocalMatrix*transform.localToWorldMatrix;
            Vector2 anchor=matrix.MultiplyPoint3x4(Vector3.zero);
            if(!float.IsFinite(anchor.x) || !float.IsFinite(anchor.y)) throw new InvalidOperationException($"Шипы {name}: некорректное положение.");
            // Trap.as: floor — Y1 = Y − sY (the box on the cell's bottom); otherwise Y1 = Y − tileY (from the cell's top).
            float bottom=spikes.bottomAnchored?anchor.y:anchor.y+cell-spikes.height;
            return new RoomSpike {id=stableId,definition=spikes.id,x=anchor.x-spikes.width*.5f,y=bottom,width=spikes.width,height=spikes.height,danger=danger};
        }
        // Read validates the complete placement. AddComponent is followed by Configure;
        // OnValidate must not warn for that still-empty transient construction step.
    }

    // The generated factory's spikes from its room templates: an object whose AllData obj is spikes (SpikeCatalog
    // .ForObject: "spikes", "fspikes") is one spikes box with the danger of its room (half the floor, as the enemies).
    public static class FactorySpikeAdapter
    {
        public static int Install(FactoryLevelRoot root)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Шипы завода: нужен подготовленный корень.");
            if(root.GetComponentInChildren<RoomSpikeAuthoring>(true)!=null) throw new InvalidOperationException("Шипы завода: повторная установка запрещена.");
            var placed=new System.Collections.Generic.List<RoomSpikeAuthoring>();
            SpikeArtworkCatalog artwork=null;
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                string type=placement.Source.Type;
                if(!type.StartsWith(ObjectPrefix,StringComparison.Ordinal)) continue;
                var definition=SpikeCatalog.ForObject(type.Substring(ObjectPrefix.Length));
                if(definition==null) continue;
                if(placement.Source.Params.Count>0 || placement.Source.Items.Count>0 || placement.Source.Links.Count>0 || placement.Source.Extra!=null)
                {Debug.LogWarning($"Шипы завода с параметрами или связями пока не поддержаны и пропущены: {placement.Id} ({type}).");continue;}
                var node=new GameObject($"{definition.title}: {placement.Id}");
                node.transform.SetParent(root.transform,false);node.transform.localPosition=placement.Position;
                var spikes=node.AddComponent<RoomSpikeAuthoring>();
                spikes.Configure($"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}",definition.id,
                    (float)placement.Room.Template.Cell,placement.Room.Cell.Y/2f);
                if(artwork==null)
                {
                    artwork=GameAssets.Load<SpikeArtworkCatalog>(SpikeArtworkCatalog.Resource)
                        ??throw new InvalidOperationException("Нет каталога двухслойной графики шипов: "+SpikeArtworkCatalog.Resource);
                    artwork.Validate();
                }
                var visual=UnityEngine.Object.Instantiate(artwork.Prefab,node.transform,false);
                // Location.as: (ny + 1) * tileY - 1; in Unity the source Y-down becomes bottom + one source pixel.
                // The mechanics above keep their existing cell anchor; only the source artwork uses the pixel offset.
                visual.transform.localPosition=new Vector3(0,1f/50,0);
                visual.Initialize(artwork.Require(definition.id),placement.Id);
                placed.Add(spikes);
            }
            // The factory's geometry is prepared before its content: the spikes join it here, the nodes stay for the editor.
            root.Authoring.ConfigurePreparedSpikes(placed.Select(s=>s.Read(root.Authoring)).ToArray());
            Debug.Log($"Завод {root.Prepared.Physics.id}: шипов поставлено {placed.Count}.");
            return placed.Count;
        }
        private const string ObjectPrefix="remains:obj:";
    }
}
