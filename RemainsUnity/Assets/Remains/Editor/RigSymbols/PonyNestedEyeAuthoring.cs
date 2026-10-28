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
    // Source is our 1.04 eye sprite, not donor artwork. GitLab confirms the state name/driver contract only.
    public static class PonyNestedEyeAuthoring
    {
        private const string AssetPath="Assets/Remains/Resources/Original/Pony/Nested/eye-closed.asset";
        private const string TexturePath="Assets/Remains/Resources/Original/Pony/218.png";
        private static readonly Vector4 ReferenceBounds=new(-257,-109,64,320),ClosedBounds=new(-443,-82,740,314);
        private static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        [MenuItem("Remains/Пони/Подготовить закрытый глаз")]
        public static void Build()
        {
            string xml=Path.Combine(Root,".reference/original/pfeUI.xml"),png=Path.Combine(Root,".reference/original/pony-appearance-source/218.png");
            string contract=ReadContract(xml);
            string player=File.ReadAllText(Path.Combine(Root,".reference/original/base/scripts/fe/unit/UnitPlayer.as"));
            foreach(string required in new[]{"internal var klip:int = 300;","this.klip = Math.random() * 200 + 50;","if(this.klip == 5 && vis.osn.body.head.morda.eye.currentFrame == 1)","vis.osn.body.head.morda.eye.gotoAndStop(3);"})
                if(!player.Contains(required,StringComparison.Ordinal))throw new InvalidDataException("Не подтверждён исходный driver моргания1.04: "+required);
            byte[] bytes=File.ReadAllBytes(png);string pngHash=Hash(bytes);
            if(File.Exists(TexturePath))
            {
                if(Hash(File.ReadAllBytes(TexturePath))!=pngHash)throw new InvalidDataException("Текстура моргания изменена вручную: "+TexturePath);
            }
            else File.WriteAllBytes(TexturePath,bytes);
            AssetDatabase.ImportAsset(TexturePath,ImportAssetOptions.ForceUpdate);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath)??throw new InvalidDataException("Не импортирован исходный closed-eye218.");
            var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if(!PonyTextureImportPolicy.Complies(importer,Math.Max(texture.width,texture.height)))throw new InvalidDataException("Текстура closed-eye218 не соответствует общему правилу пони.");
            var baseline=AssetDatabase.LoadAssetAtPath<PonyRenderAsset>("Assets/Remains/Resources/Original/Pony/render.asset")??throw new InvalidDataException("Нет банка символов пони.");
            var anchor=baseline.Clips.Single(c=>c.id=="idle").frames[0].parts.Single(p=>p.sourceShape=="216");
            if(string.IsNullOrWhiteSpace(anchor.symbolId))throw new InvalidDataException("Closed-eye строится после компиляции локальных символов пони.");
            var points=CanonicalClosed(anchor);
            var mesh=new Mesh {name="symbol:nested-eye-closed",vertices=points,uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},
                triangles=new[]{0,2,1,2,3,1},colors=new[]{Color.white,Color.white,Color.white,Color.white}};
            mesh.RecalculateBounds();var material=WorldArtworkMaterial.Create(texture);material.name="material:nested-eye-closed";
            string folder=Path.GetDirectoryName(AssetPath).Replace('\\','/');
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Remains/Resources/Original/Pony","Nested");
            var asset=AssetDatabase.LoadAssetAtPath<PonyNestedEyeAsset>(AssetPath);
            if(asset==null){asset=ScriptableObject.CreateInstance<PonyNestedEyeAsset>();AssetDatabase.CreateAsset(asset,AssetPath);}
            var old=AssetDatabase.LoadAllAssetsAtPath(AssetPath).Where(o=>o is Mesh || o is Material).ToDictionary(o=>o.name,StringComparer.Ordinal);
            T Save<T>(T value)where T:Object
            {
                if(old.TryGetValue(value.name,out var previous))
                {
                    if(previous is not T)throw new InvalidDataException("Конфликт вложенного ресурса глаза: "+value.name);
                    EditorUtility.CopySerialized(value,previous);Object.DestroyImmediate(value);EditorUtility.SetDirty(previous);return (T)previous;
                }
                AssetDatabase.AddObjectToAsset(value,asset);return value;
            }
            asset.Configure(Hash(Encoding.UTF8.GetBytes(contract+"\n"+pngHash+"\nklip:300;interval:50..249;closed:5..2")),PonyRenderAsset.SourceHash(),Save(mesh),Save(material),points);
            asset.Validate();EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();PonyNestedEyeFrames.Reset();
            Check();
            Debug.Log("Вложенный глаз подготовлен: исходный1.04 shape218, чёрная линия без масок/перекраски, один head-local mesh; driver300/50..249/4 ticks. Результат source-derived; косметический PRNG Unity отличается от Flash Math.random.");
        }
        private static Vector3[] CanonicalClosed(PonyRenderPart anchor)
        {
            if(anchor.points?.Length!=4 || anchor.sourceShape!="216")throw new InvalidDataException("Нет опорного quad216 для вложенного глаза.");
            var p=anchor.points;var inverse=anchor.symbolPose.Matrix.inverse;
            Vector3 Point(float x,float y)=>inverse.MultiplyPoint3x4(p[0]+(p[1]-p[0])*((x-ReferenceBounds.x)/(ReferenceBounds.z-ReferenceBounds.x))+
                (p[2]-p[0])*((y-ReferenceBounds.w)/(ReferenceBounds.y-ReferenceBounds.w)));
            return new[]{Point(ClosedBounds.x,ClosedBounds.w),Point(ClosedBounds.z,ClosedBounds.w),Point(ClosedBounds.x,ClosedBounds.y),Point(ClosedBounds.z,ClosedBounds.y)};
        }
        public static void Check()
        {
            var asset=AssetDatabase.LoadAssetAtPath<PonyNestedEyeAsset>(AssetPath)??throw new InvalidDataException("Не подготовлен closed-eye asset.");asset.Validate();
            int anchors=0;float error=0;
            void Include(PonyRenderPart[] parts,string context)
            {
                foreach(var anchor in parts.Where(p=>p.sourceShape=="216" && PonyNestedEyeFrames.IsEyePart(p)))
                {
                    var canonical=CanonicalClosed(anchor);
                    for(int p=0;p<4;p++)error=Math.Max(error,(canonical[p]-asset.Points[p]).magnitude);
                    if(error>.00001f)throw new InvalidDataException($"Закрытый глаз расходится с локальным head symbol: {context}/{anchor.sourcePath}, error={error:G6}.");
                    if(anchor.baseColor!=Color.white)throw new InvalidDataException("Для моргания нужен явный authored color adapter: "+context+"/"+anchor.sourcePath);
                    anchors++;
                }
            }
            var baseline=AssetDatabase.LoadAssetAtPath<PonyRenderAsset>("Assets/Remains/Resources/Original/Pony/render.asset");
            foreach(var clip in baseline.Clips)for(int f=0;f<clip.frames.Length;f++)Include(clip.frames[f].parts,clip.id+"/"+f);
            string wearRoot="Assets/Remains/Resources/Original/Pony/Wearables";
            foreach(string guid in AssetDatabase.FindAssets("t:WearableRenderAsset",new[]{wearRoot}))
            {
                var wear=AssetDatabase.LoadAssetAtPath<WearableRenderAsset>(AssetDatabase.GUIDToAssetPath(guid));
                foreach(var clip in wear.Clips)for(int f=0;f<clip.frames.Length;f++)Include(clip.frames[f].parts,wear.VisualId+"/"+clip.id+"/"+f);
            }
            CheckClock();Debug.Log($"Вложенный глаз: PASS — {anchors} опор216, canonical error={error:G6} м; точный source interval/closed ticks, стабильный seed, смена pose не участвует.");
        }
        public static void CheckClock()
        {
            void Require(bool value,string reason){if(!value)throw new InvalidOperationException("Часы моргания: "+reason);}
            var clock=new PonyNestedBlinkClock();clock.Bind("nested-clock-check");
            for(int tick=1;tick<295;tick++){clock.Advance(1f/30,true);Require(!clock.Closed,"Раннее первое моргание");}
            for(int tick=295;tick<=298;tick++){clock.Advance(1f/30,true);Require(clock.Closed,"Пропущены четыре закрытых source ticks");}
            clock.Advance(1f/30,true);Require(!clock.Closed,"Глаз не открылся приklip1");
            clock.Advance(1f/30,true);clock.Advance(1f/30,true);Require(clock.RemainingTicks>=50 && clock.RemainingTicks<250,"Интервал вне source50..249");
            int before=clock.RemainingTicks;clock.Advance(2,false);Require(!clock.Closed && clock.RemainingTicks==before,"Запрещённое состояние продолжает косметический clock");
            clock.Bind("nested-clock-check");Require(clock.RemainingTicks==before,"Snapshot rebind сбросил phase");
            var same=new PonyNestedBlinkClock();same.Bind("nested-clock-check");
            var other=new PonyNestedBlinkClock();other.Bind("nested-clock-check");
            for(int i=0;i<1500;i++){same.Advance(1f/60,true);if(i%2==1)other.Advance(1f/30,true);if(i%2==1)Require(same.RemainingTicks==other.RemainingTicks && same.Closed==other.Closed,"Фаза зависит от render FPS");}
        }
        private static string ReadContract(string file)
        {
            var wanted=new Dictionary<string,XElement>(StringComparer.Ordinal);
            using var reader=XmlReader.Create(file,new XmlReaderSettings {IgnoreWhitespace=true,IgnoreComments=true,DtdProcessing=DtdProcessing.Prohibit});
            while(reader.Read())
            {
                if(reader.NodeType!=XmlNodeType.Element || reader.LocalName!="item")continue;
                string type=reader.GetAttribute("type"),shape=reader.GetAttribute("shapeId"),sprite=reader.GetAttribute("spriteId");
                string key=shape is "216" or "218"?shape:sprite=="220"?sprite:null;
                if(key==null || type==null || !type.StartsWith("Define",StringComparison.Ordinal))continue;
                using(var subtree=reader.ReadSubtree())wanted.Add(key,XElement.Load(subtree));
                if(wanted.Count==3)break;
            }
            if(wanted.Count!=3)throw new InvalidDataException("Нет исходных eye216/218/sprite220 в "+file);
            Vector4 Bounds(XElement shape)
            {
                var b=shape.Element("shapeBounds")??throw new InvalidDataException("Нет bounds закрытого глаза.");
                float Read(string name)=>float.Parse((string)b.Attribute(name),CultureInfo.InvariantCulture);
                return new Vector4(Read("Xmin"),Read("Ymin"),Read("Xmax"),Read("Ymax"));
            }
            if(Bounds(wanted["216"])!=ReferenceBounds || Bounds(wanted["218"])!=ClosedBounds)throw new InvalidDataException("Изменились source bounds глаза216/218.");
            var fills=wanted["218"].Descendants("color").ToArray();
            if(fills.Length!=1 || (int)fills[0].Attribute("red")!=0 || (int)fills[0].Attribute("green")!=0 || (int)fills[0].Attribute("blue")!=0)
                throw new InvalidDataException("Shape218 должен быть исходной чёрной линией, slot none.");
            var display=new Dictionary<int,int>();var frames=new List<Dictionary<int,int>>();
            foreach(var tag in wanted["220"].Element("subTags").Elements())
            {
                string type=(string)tag.Attribute("type");
                if(type.StartsWith("PlaceObject",StringComparison.Ordinal))
                {
                    int depth=(int)tag.Attribute("depth");var id=(int?)tag.Attribute("characterId");
                    if(id.HasValue)display[depth]=id.Value;
                }
                else if(type.StartsWith("RemoveObject",StringComparison.Ordinal))display.Remove((int)tag.Attribute("depth"));
                else if(type=="ShowFrameTag")frames.Add(new Dictionary<int,int>(display));
            }
            if(frames.Count!=4 || frames[2].Count!=1 || !frames[2].TryGetValue(1,out int closed) || closed!=218 || !frames[0].ContainsValue(216))
                throw new InvalidDataException("Eye frame3 не совпадает с исходным закрытым состоянием218.");
            return string.Join("\n",wanted.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Value.ToString(SaveOptions.DisableFormatting)));
        }
        private static string Hash(byte[] bytes){using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    }
}
