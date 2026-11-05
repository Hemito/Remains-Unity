using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.RoomFormat;
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
        [SerializeField,InspectorName("Название в подсказке «лечь на …»")] private string label;
        [SerializeField,InspectorName("Изголовье справа")] private bool headRight;
        public void Configure(string id,float bedWidth,float bedHeight,string name=null,bool right=false)
        {stableId=id;width=bedWidth;height=bedHeight;label=name;headRight=right;}
        public string Label=>label;
        public RoomRest Read(RoomAuthoring root)
        {
            if(string.IsNullOrWhiteSpace(stableId) || !(width>0) || !(height>0))
                throw new InvalidOperationException($"Кровать {name}: нужны устойчивый ID, ширина и высота.");
            var matrix=root.transform.worldToLocalMatrix*transform.localToWorldMatrix;
            Vector2 anchor=matrix.MultiplyPoint3x4(Vector3.zero);
            if(!float.IsFinite(anchor.x) || !float.IsFinite(anchor.y)) throw new InvalidOperationException($"Кровать {name}: некорректное положение.");
            return new RoomRest {id=stableId,x=anchor.x-width*.5f,y=anchor.y,width=width,height=height,label=label,headRight=headRight};
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(stableId)) Debug.LogWarning($"Кровать {name}: не задан ID.",this);
        }
    }

    // The generated factory's places to lie on from its room templates, as level geometry. Objects: AllData bed and couch —
    // their cells' width (size) and the lying height of their art (scy). Decor: the original's back pictures of a stretcher and
    // an operating table (no scy: the height of the wide top row of their art at 50 px per metre, the head by the art). They
    // are the room's pictures, so their side comes from the same placement that draws them.
    public static class FactoryRestAdapter
    {
        public const string BedType="remains:obj:bed";
        private const float PixelsPerMetre=(float)RoomSpriteLayout.SourcePixelsPerMeter;
        private static readonly Dictionary<string,(float height,string label)> Objects=new(StringComparer.Ordinal)
            {{BedType,(28/PixelsPerMetre,"кровать")},{"remains:obj:couch",(20/PixelsPerMetre,"диван")}};
        private static readonly Dictionary<string,(float height,string label,bool headRight)> Decor=new(StringComparer.Ordinal)
            {{"remains:decor:barrow",(43/PixelsPerMetre,"каталку",true)},{"remains:decor:oper",(47/PixelsPerMetre,"операционный стол",false)}};
        public static int Install(FactoryLevelRoot root)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Кровати завода: нужен подготовленный корень.");
            if(root.GetComponentInChildren<RoomRestAuthoring>(true)!=null) throw new InvalidOperationException("Кровати завода: повторная установка запрещена.");
            var placed=new System.Collections.Generic.List<RoomRestAuthoring>();
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                if(!Objects.TryGetValue(placement.Source.Type,out var rest)) continue;
                var definition=root.Catalog.Definitions.Require(placement.Source.Type,placement.Id);
                placed.Add(Place(root,placement.Position,$"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}",
                    Math.Max(1,definition.ObjectSize)*(float)placement.Room.Template.Cell,rest.height,rest.label,false));
            }
            foreach(var room in root.Prepared.Grid.Rooms)
            {
                if(!room.Template.Decor.Any(decor=>Decor.ContainsKey(decor.Type))) continue;
                var issues=new List<string>();
                var pictures=RoomSpriteLayout.Place(room.Template,room.Cell.Mirror,root.Catalog.Definitions,root.Catalog.Art.Manifest,0,new Dictionary<string,string>(),
                    issues,new SortedDictionary<string,int>(StringComparer.Ordinal),new SortedDictionary<string,int>(StringComparer.Ordinal));
                foreach(var picture in pictures)
                {
                    if(picture.Kind!=RoomSpriteKind.Decor || !Decor.TryGetValue(picture.Definition.Id,out var rest)) continue;
                    float cell=(float)room.Template.Cell,width=picture.Definition.DecorWidth*cell;bool flipped=picture.ScaleX<0;
                    // The clip stands from the top-left of its cell; flipped, the point is its right edge. Its bottom is the floor.
                    var bottom=new Vector2((float)picture.X+(flipped?-.5f:.5f)*width,(float)picture.Y-picture.Definition.DecorHeight*cell);
                    placed.Add(Place(root,root.Offset(room)+bottom,$"{room.Cell.X},{room.Cell.Y}:decor{picture.Index}",width,rest.height,rest.label,rest.headRight!=flipped));
                }
            }
            root.Authoring.ConfigurePreparedBeds(placed.Select(b=>b.Read(root.Authoring)).ToArray());
            if(placed.Count>0) Debug.Log($"Завод {root.Prepared.Physics.id}: мест отдыха {placed.Count}: "+
                string.Join(", ",placed.GroupBy(b=>b.Label).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>$"{g.Key} {g.Count()}"))+".");
            return placed.Count;
        }
        private static RoomRestAuthoring Place(FactoryLevelRoot root,Vector2 bottom,string id,float width,float height,string label,bool headRight)
        {
            var node=new GameObject("Лежанка: "+id);
            node.transform.SetParent(root.transform,false);node.transform.localPosition=bottom;
            var rest=node.AddComponent<RoomRestAuthoring>();
            rest.Configure(id,width,height,label,headRight);
            return rest;
        }
    }
}
