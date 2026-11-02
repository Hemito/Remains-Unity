using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class PonyFacialAuthoring
    {
        private const string Output="Assets/Remains/Resources/Original/Pony/Nested/expressions.asset";
        [MenuItem("Remains/Пони/Подготовить мимику")]
        public static void Build()
        {
            string root=Path.GetFullPath(".."),source=Path.Combine(root,".reference/original/pfeUI.xml");
            var wanted=new Dictionary<string,XElement>(StringComparer.Ordinal);
            using(var reader=XmlReader.Create(source,new XmlReaderSettings {IgnoreWhitespace=true,IgnoreComments=true,DtdProcessing=DtdProcessing.Prohibit}))
                while(reader.Read())
                {
                    if(reader.NodeType!=XmlNodeType.Element || reader.LocalName!="item")continue;
                    string id=reader.GetAttribute("shapeId"),sprite=reader.GetAttribute("spriteId");
                    if(id is not ("216" or "217" or "202" or "219") && sprite!="220")continue;
                    using(var sub=reader.ReadSubtree())wanted.Add(id??sprite,XElement.Load(sub));
                    if(wanted.Count==5)break;
                }
            if(wanted.Count!=5)throw new InvalidDataException("Мимика: нет исходных eye220 и рисунков202/216/217/219.");
            var places=wanted["220"].Descendants("item").Where(t=>((string)t.Attribute("type")??"").StartsWith("PlaceObject",StringComparison.Ordinal)).ToArray();
            var pupil=places.Single(p=>(string)p.Attribute("characterId")=="203").Element("matrix");
            if(!places.Any(p=>(string)p.Attribute("characterId")=="217") || !places.Any(p=>(string)p.Attribute("characterId")=="219"))
                throw new InvalidDataException("Мимика: исходный eye220 не содержит состояния тревоги и потери сознания.");
            float Read(XElement e,string name,float fallback)=>e?.Attribute(name)==null?fallback:float.Parse((string)e.Attribute(name),CultureInfo.InvariantCulture);
            var reference=wanted["216"].Element("shapeBounds");
            float x0=Read(reference,"Xmin",0),x1=Read(reference,"Xmax",0),y0=Read(reference,"Ymin",0),y1=Read(reference,"Ymax",0);
            var bank=AssetDatabase.LoadAssetAtPath<PonyRenderAsset>("Assets/Remains/Resources/Original/Pony/render.asset")??throw new InvalidDataException("Мимика: нет банка рисунков пони.");
            var anchor=bank.Clips.Single(c=>c.id=="idle").frames[0].parts.Single(p=>p.sourceShape=="216");var inv=anchor.symbolPose.Matrix.inverse;
            Vector3 Point(float x,float y)=>inv.MultiplyPoint3x4(anchor.points[0]+(anchor.points[1]-anchor.points[0])*((x-x0)/(x1-x0))+
                (anchor.points[2]-anchor.points[0])*((y-y1)/(y0-y1)));
            var asset=AssetDatabase.LoadAssetAtPath<PonyFacialAsset>(Output);
            if(asset==null){asset=ScriptableObject.CreateInstance<PonyFacialAsset>();AssetDatabase.CreateAsset(asset,Output);}
            var old=AssetDatabase.LoadAllAssetsAtPath(Output).Where(o=>o is Mesh || o is Material).ToDictionary(o=>o.name,StringComparer.Ordinal);
            T Save<T>(T item) where T:Object
            {
                if(old.TryGetValue(item.name,out var found))
                {if(found is not T)throw new InvalidDataException("Мимика: конфликт ресурса "+item.name);EditorUtility.CopySerialized(item,found);Object.DestroyImmediate(item);EditorUtility.SetDirty(found);return (T)found;}
                AssetDatabase.AddObjectToAsset(item,asset);return item;
            }
            PonyFacialPiece Piece(string shape,string slot,XElement matrix=null)
            {
                string path="Assets/Remains/Resources/Original/Pony/"+shape+".png";
                if(!File.Exists(path))File.Copy(Path.Combine(root,".reference/original/pony-appearance-source/"+shape+".png"),path);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path)??throw new InvalidDataException("Мимика: нет текстуры "+shape);
                var bounds=wanted[shape].Element("shapeBounds");
                float a=Read(matrix,"scaleX",1),b=Read(matrix,"rotateSkew0",0),c=Read(matrix,"rotateSkew1",0),d=Read(matrix,"scaleY",1),tx=Read(matrix,"translateX",0),ty=Read(matrix,"translateY",0);
                Vector3 At(float x,float y)=>Point(a*x+c*y+tx,b*x+d*y+ty);
                float l=Read(bounds,"Xmin",0),r=Read(bounds,"Xmax",0),t=Read(bounds,"Ymin",0),bottom=Read(bounds,"Ymax",0);
                var vertices=new[]{At(l,bottom),At(r,bottom),At(l,t),At(r,t)};
                var mesh=new Mesh {name="expression:"+shape,vertices=vertices,uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},triangles=new[]{0,2,1,2,3,1},colors=new[]{Color.white,Color.white,Color.white,Color.white}};
                mesh.RecalculateBounds();var material=WorldArtworkMaterial.Create(texture);material.name="expression-material:"+shape;
                return new PonyFacialPiece {shape=shape,slot=slot,mesh=Save(mesh),material=Save(material)};
            }
            using var sha=SHA256.Create();string hash=Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n",wanted.OrderBy(p=>p.Key).Select(p=>p.Value.ToString(SaveOptions.DisableFormatting))))));
            asset.Configure(hash,PonyRenderAsset.SourceHash(),new[]{Piece("217","none"),Piece("202","eye",pupil),Piece("216","none")},new[]{Piece("219","none")});
            asset.Validate();EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();PonyNestedEyeFrames.Reset();
            Debug.Log("Мимика пони: исходные состояния eye220 — обычный глаз, тревога, моргание, потеря сознания; регистрация по216. Маска обычного зрачка остаётся неподвижной.");
        }
    }
}
