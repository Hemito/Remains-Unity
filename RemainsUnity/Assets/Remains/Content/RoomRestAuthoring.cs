using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // A bed of a room as level geometry (RoomLayout.beds, BedRest): fixed, the same on every peer, not saved. The node
    // stands on the bottom centre of the bed's cells.
    public sealed class RoomRestAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID кровати")] private string stableId;
        [SerializeField,Min(.01f),InspectorName("Ширина, м")] private float width=1;
        [SerializeField,Min(.01f),InspectorName("Высота лежанки, м")] private float height=.56f;
        public void Configure(string id,float bedWidth,float bedHeight) {stableId=id;width=bedWidth;height=bedHeight;}
        public RoomRest Read(RoomAuthoring root)
        {
            if(string.IsNullOrWhiteSpace(stableId) || !(width>0) || !(height>0))
                throw new InvalidOperationException($"Кровать {name}: нужны устойчивый ID, ширина и высота.");
            var matrix=root.transform.worldToLocalMatrix*transform.localToWorldMatrix;
            Vector2 anchor=matrix.MultiplyPoint3x4(Vector3.zero);
            if(!float.IsFinite(anchor.x) || !float.IsFinite(anchor.y)) throw new InvalidOperationException($"Кровать {name}: некорректное положение.");
            return new RoomRest {id=stableId,x=anchor.x-width*.5f,y=anchor.y,width=width,height=height};
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(stableId)) Debug.LogWarning($"Кровать {name}: не задан ID.",this);
        }
    }

    // The generated factory's beds from its room templates: AllData obj bed — its cells' width (size) and the lying
    // height of its art (scy 28 px at 50 px per metre).
    public static class FactoryRestAdapter
    {
        public const string BedType="remains:obj:bed";
        private const float BedHeight=28/50f;
        public static int Install(FactoryLevelRoot root)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Кровати завода: нужен подготовленный корень.");
            if(root.GetComponentInChildren<RoomRestAuthoring>(true)!=null) throw new InvalidOperationException("Кровати завода: повторная установка запрещена.");
            var placed=new System.Collections.Generic.List<RoomRestAuthoring>();
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                if(placement.Source.Type!=BedType) continue;
                var definition=root.Catalog.Definitions.Require(BedType,placement.Id);
                var node=new GameObject("Кровать: "+placement.Id);
                node.transform.SetParent(root.transform,false);node.transform.localPosition=placement.Position;
                var bed=node.AddComponent<RoomRestAuthoring>();
                bed.Configure($"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}",
                    Math.Max(1,definition.ObjectSize)*(float)placement.Room.Template.Cell,BedHeight);
                placed.Add(bed);
            }
            root.Authoring.ConfigurePreparedBeds(placed.Select(b=>b.Read(root.Authoring)).ToArray());
            if(placed.Count>0) Debug.Log($"Завод {root.Prepared.Physics.id}: кроватей {placed.Count}.");
            return placed.Count;
        }
    }
}
