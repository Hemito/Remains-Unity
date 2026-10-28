using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using UnityEngine;

namespace Remains.Editor
{
    // Resource reader only. ActionScript is neither interpreted nor executed.
    internal sealed class PonyWearableXmlSource
    {
        internal sealed class Placement
        {
            public int Id,Depth,ClipDepth;public string Name;
            public Matrix4x4 Matrix=Matrix4x4.identity;public Color Color=Color.white;
            public Placement Copy()=>new() {Id=Id,Depth=Depth,ClipDepth=ClipDepth,Name=Name,Matrix=Matrix,Color=Color};
        }
        internal sealed class Timeline
        {public readonly List<Placement[]> Frames=new();public readonly Dictionary<string,int> Labels=new(StringComparer.Ordinal);}
        private sealed class Definition {public string Type;public float[] Bounds;public XElement Sprite;}
        private readonly Dictionary<int,Definition> definitions=new();
        private readonly Dictionary<int,Timeline> timelines=new();
        public PonyWearableXmlSource(string path)
        {
            using var reader=XmlReader.Create(path,new XmlReaderSettings {IgnoreWhitespace=false,DtdProcessing=DtdProcessing.Prohibit});
            while(reader.Read())
            {
                if(reader.NodeType!=XmlNodeType.Element || reader.Depth!=2)continue;
                string kind=reader.GetAttribute("type"),id=reader.GetAttribute("spriteId")??reader.GetAttribute("shapeId");
                if(id==null || !(kind=="DefineSpriteTag" || kind?.StartsWith("DefineShape",StringComparison.Ordinal)==true))continue;
                XElement element;
                using(var subtree=reader.ReadSubtree()){subtree.Read();element=XElement.Load(subtree);}
                int key=int.Parse(id,CultureInfo.InvariantCulture);
                var value=new Definition {Type=kind};
                if(kind=="DefineSpriteTag")value.Sprite=element;
                else
                {
                    var bounds=element.Element("shapeBounds")??throw new InvalidDataException("Одежда: нет shapeBounds, shape="+key);
                    value.Bounds=new[]{Number(bounds,"Xmin"),Number(bounds,"Ymin"),Number(bounds,"Xmax"),Number(bounds,"Ymax")};
                }
                definitions.Add(key,value);
            }
        }
        public bool IsShape(int id)=>DefinitionOf(id).Bounds!=null;
        public float[] Bounds(int id)=>DefinitionOf(id).Bounds??throw new InvalidDataException("Одежда: маска не является shape="+id);
        private Definition DefinitionOf(int id)=>definitions.TryGetValue(id,out var value)?value:throw new InvalidDataException("Одежда: нет определения shape/sprite="+id);
        public Timeline GetTimeline(int id)
        {
            if(timelines.TryGetValue(id,out var existing))return existing;
            var definition=DefinitionOf(id);
            if(definition.Sprite==null)throw new InvalidDataException("Одежда: ожидается sprite="+id);
            var timeline=new Timeline();var display=new SortedDictionary<int,Placement>();
            foreach(var tag in definition.Sprite.Element("subTags").Elements())
            {
                string type=(string)tag.Attribute("type");
                if(type=="FrameLabelTag")timeline.Labels.Add((string)tag.Attribute("name"),timeline.Frames.Count);
                else if(type?.StartsWith("PlaceObject",StringComparison.Ordinal)==true)
                {
                    int depth=Int(tag,"depth");bool move=(string)tag.Attribute("placeFlagMove")=="true";
                    if(move && !display.TryGetValue(depth,out _))throw new InvalidDataException($"Одежда: move без исходного объекта, sprite={id}, depth={depth}.");
                    var part=move?display[depth].Copy():new Placement {Depth=depth,Name=depth.ToString(CultureInfo.InvariantCulture)};
                    if(tag.Attribute("characterId")!=null)part.Id=Int(tag,"characterId");
                    if(tag.Attribute("name")!=null)part.Name=(string)tag.Attribute("name");
                    if(tag.Attribute("clipDepth")!=null)part.ClipDepth=Int(tag,"clipDepth");
                    if(tag.Element("matrix") is { } matrix)part.Matrix=Matrix(matrix);
                    if(tag.Element("colorTransform") is { } color)part.Color=Tint(color);
                    if(part.Id==0)throw new InvalidDataException($"Одежда: нет characterId, sprite={id}, depth={depth}.");
                    display[depth]=part;
                }
                else if(type?.StartsWith("RemoveObject",StringComparison.Ordinal)==true)display.Remove(Int(tag,"depth"));
                else if(type=="ShowFrameTag")timeline.Frames.Add(display.Values.Select(p=>p.Copy()).ToArray());
            }
            if(timeline.Frames.Count!=Int(definition.Sprite,"frameCount"))throw new InvalidDataException("Одежда: неверное число кадров sprite="+id);
            timelines.Add(id,timeline);return timeline;
        }
        private static int Int(XElement e,string key)=>int.Parse((string)e.Attribute(key)??throw new InvalidDataException("Одежда: нет "+key),CultureInfo.InvariantCulture);
        private static float Number(XElement e,string key,float fallback=float.NaN)
        {
            string raw=(string)e.Attribute(key);
            float value=raw==null?fallback:float.Parse(raw,CultureInfo.InvariantCulture);
            if(!float.IsFinite(value))throw new InvalidDataException("Одежда: нет конечного числа "+key);
            return value;
        }
        private static Matrix4x4 Matrix(XElement e)
        {
            var m=Matrix4x4.identity;m.m00=Number(e,"scaleX",1);m.m10=Number(e,"rotateSkew0",0);m.m01=Number(e,"rotateSkew1",0);m.m11=Number(e,"scaleY",1);
            m.m03=Number(e,"translateX",0);m.m13=Number(e,"translateY",0);
            if(Math.Abs(m.determinant)<1e-12)throw new InvalidDataException("Одежда: вырожденная матрица XML.");return m;
        }
        private static Color Tint(XElement e)
        {
            if((string)e.Attribute("hasAddTerms")=="true")throw new InvalidDataException("Одежда: additive color transform требует отдельной реализации.");
            return new Color(Number(e,"redMultTerm",256)/256,Number(e,"greenMultTerm",256)/256,Number(e,"blueMultTerm",256)/256,Number(e,"alphaMultTerm",256)/256);
        }
    }
}
