using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationSource
    {
        public const string SourceId="original:plant:27",SourceHash="2300f72c02bf58273641760f36a8847ecf5e071257a47af631d2df8f468d04ec";
        [Serializable] public sealed class SourceFile {public int schema;public SourceRoom room;public Dictionary<string,ObjectDefinition> objectDefinitions;}
        [Serializable] public sealed class ObjectDefinition {public Dictionary<string,string> attributes;public string contentXml;}
        [Serializable] public sealed class SourceRoom {public string id,contentSha256;public int sourceIndex,width,height;public string[][] grid;public SourceRecord[] records;}
        [Serializable] public sealed class SourceRecord {public string recordId,tag,contentXml;public Dictionary<string,string> attributes;}
        [Serializable] public sealed class Decoration {public string recordId,disposition;public int artworkIndex;public float sourceAlpha;public Dictionary<string,string> sourceAttributes,sourceDefinition;}
        [Serializable] public sealed class Window {public string recordId,coverageKey;public int diffuseArtworkIndex;public float[] localBounds;}
        [Serializable] public sealed class Manifest
        {public int schema;public string sourceId,roomContentSha256;public Dictionary<string,string> historicalInputs,derivedFiles;public FactoryModuleResource[] resources;public Decoration[] decorPlacements;public Window[] windows;public RoomArtwork[] approvedUnityArtwork;}
        [Serializable] private sealed class Bindings {public FactoryModuleSurfaceBinding[] sourceBindings;}
        public sealed class Data
        {public SourceFile source;public RoomLayout geometry,decor;public Manifest manifest;public FactoryModuleSurfaceBinding[] bindings;public FactoryLadderLandingRecord[] landings;}
        public static string Research(string file)=>System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Tools/Research/FactoryContinuation27R60",file));
        public static string Historical(string file)=>System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Tools/Research/FactoryContinuation27",file));
        public static Data Load()
        {
            var manifest=StateJson.FromJson<Manifest>(File.ReadAllText(Research("manifest.json")));
            Require(manifest?.schema==1 && manifest.sourceId==SourceId && manifest.roomContentSha256==SourceHash && manifest.historicalInputs!=null && manifest.derivedFiles!=null,
                "Неполная resource wave/schema/source identity.");
            foreach(var file in manifest.historicalInputs) RequireFile(Historical(file.Key),file.Value);
            foreach(var file in manifest.derivedFiles) RequireFile(Research(file.Key),file.Value);
            var source=StateJson.FromJson<SourceFile>(File.ReadAllText(Historical("source-room.json")));var room=source.room;
            Require(source.schema==1 && room?.id==SourceId && room.contentSha256==SourceHash && room.sourceIndex==27 && room.width==48 && room.height==25 &&
                room.grid?.Length==25 && room.grid.All(r=>r?.Length==48) && room.records?.Count(r=>r.tag=="obj")==44 && room.records.Count(r=>r.tag=="back")==35 && source.objectDefinitions!=null,
                "Изменён source grid/44objects/35decor.");
            foreach(var record in room.records.Where(r=>r.tag=="obj"||r.tag=="back"))
            {
                var xml=XElement.Parse(record.contentXml);
                Require(xml.Name.LocalName==record.tag && record.attributes!=null && xml.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value).OrderBy(a=>a.Key).SequenceEqual(record.attributes.OrderBy(a=>a.Key)),
                    "Source XML/attrs расходятся, record="+record.recordId);
            }
            var geometry=StateJson.FromJson<RoomLayout>(File.ReadAllText(Research("geometry.json")));
            var decor=StateJson.FromJson<RoomLayout>(File.ReadAllText(Research("decor.json")));
            var caps=StateJson.FromJson<FactoryLadderLandingSource>(File.ReadAllText(Historical("landing-source.json")));caps.Validate();
            Require(geometry.platforms?.Length==29 && geometry.ladders?.Length==8 && geometry.artwork?.Length==43 && decor.artwork?.Length==35 && caps.records.Length==3 &&
                manifest.resources?.Length==23 && manifest.decorPlacements?.Length==35 && manifest.windows?.Length==5 && manifest.approvedUnityArtwork?.Length==1,
                "Изменён точный набор29surfaces/8ladders/43+35+1art/23resources/5windows/3caps.");
            // Explicit Unity shader policy, separate from the immutable source rectangles.
            foreach(var artwork in geometry.artwork.Concat(decor.artwork).Where(a=>a.order<-10)) artwork.backgroundLighting=true;
            foreach(var cap in caps.records)
                Require(cap.sourceRoom==27 && room.grid[cap.row][cap.column]==cap.expectedToken && geometry.platforms.Count(p=>FactoryLadderLandingChecks.SameSurface(p,cap.surface))==1,
                    "Изменена явная source ladder landing, id="+cap.id);
            Require(manifest.decorPlacements.Select(p=>p.recordId).Distinct().Count()==35 && manifest.decorPlacements.Select(p=>p.artworkIndex).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,35)),
                "Неоднозначная decor/source binding.");
            foreach(var p in manifest.decorPlacements)
            {
                string kind=p.sourceAttributes["id"];
                Require(p.sourceAlpha==(kind=="rgraff"?.9f:1f) && p.disposition==(kind=="bwindow"?"window-diffuse-with-native-coverage":kind=="rgraff"?"diffuse-material-alpha":"diffuse-sprite"),
                    "Необработанный alpha/erasure/disposition, record="+p.recordId);
            }
            var binding=StateJson.FromJson<Bindings>(File.ReadAllText(Research("geometry.json"))).sourceBindings;
            Require(binding?.Length==29,"Потеряны точные source surface bindings.");
            return new Data {source=source,geometry=geometry,decor=decor,manifest=manifest,bindings=binding,landings=caps.records};
        }
        public static string Value(SourceRecord record,string key)=>record.attributes!=null && record.attributes.TryGetValue(key,out string value)?value:
            throw new InvalidOperationException("Продолжение27: нет атрибута "+key+", record="+record.recordId);
        public static float Number(SourceRecord record,string key)
        {float value=float.Parse(Value(record,key),NumberStyles.Float,CultureInfo.InvariantCulture);Require(float.IsFinite(value),"Неконечная source coordinate, record="+record.recordId);return value;}
        public static Vector2 Anchor(Data data,SourceRecord record)
        {
            string kind=Value(record,"id");var definition=data.source.objectDefinitions[kind];var xml=XElement.Parse(definition.contentXml);
            Require(definition.attributes.OrderBy(a=>a.Key).SequenceEqual(xml.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value).OrderBy(a=>a.Key)),"Изменён source object definition, kind="+kind);
            Require(definition.attributes.TryGetValue("size",out string text),"Нет source placement size, kind="+kind);
            float size=float.Parse(text,CultureInfo.InvariantCulture);Require(float.IsFinite(size)&&size>0,"Неверный source placement size, kind="+kind);
            return FactorySourceCoordinates.Placement(Number(record,"x"),Number(record,"y"),size);
        }
        public static T Asset<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Продолжение27: нет подготовленного named asset, path="+path);
        public static void Require(bool condition,string detail) {if(!condition)throw new InvalidOperationException("Продолжение27: "+detail);}
        public static void RequireFile(string path,string hash)
        {
            using var sha=SHA256.Create();Require(File.Exists(path),"Отсутствует source file="+path);
            string actual=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
            Require(actual==hash,"Изменён source file="+path+", expected="+hash+", actual="+actual);
        }
    }
}
