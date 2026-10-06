using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    // Pins reviewed resource/data input, not the source generator or its rolls.
    internal static class FactoryWarehouseSource
    {
        internal const string SourceId="original:plant:40",SourceHash="3a33d6e9f9781454197e5cb9540f4d83c1647a83d526e19ef040af245c764268";
        [Serializable] internal sealed class FileData {public int schema;public SourceRoom room;public Dictionary<string,ObjectDefinition> objectDefinitions;}
        [Serializable] internal sealed class ObjectDefinition {public Dictionary<string,string> attributes;public string contentXml;}
        [Serializable] internal sealed class SourceRoom {public string id,contentSha256;public int sourceIndex,width,height;public string[][] grid;public SourceRecord[] records;}
        [Serializable] internal sealed class SourceRecord {public string recordId,tag,contentXml;public Dictionary<string,string> attributes;}
        [Serializable] internal sealed class Manifest {public FactoryModuleResource[] resources;public Decoration[] decorPlacements;}
        [Serializable] internal sealed class Decoration {public string recordId,blend,disposition;public int artworkIndex;public Dictionary<string,string> sourceAttributes,sourceDefinition;}
        [Serializable] private sealed class GeometryBindings {public FactoryModuleSurfaceBinding[] sourceBindings;}
        internal sealed class Data
        {public FileData source;public RoomLayout geometry,decor;public Manifest manifest;public FactoryModuleSurfaceBinding[] bindings;public FactoryLadderLandingRecord[] landings;}
        internal static string Research(string file)=>System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Tools/Research/FactoryWarehouse40",file));
        internal static Data Load(string researchFolder=null)
        {
            researchFolder=researchFolder??System.IO.Path.GetDirectoryName(Research("source-room.json"));
            var source=Read<FileData>("source-room.json","cc294fa24a62da5f618fdd6ac995fbf44d2e5f012cf52fd71e6615fb44a78333",researchFolder);var room=source.room;
            Require(source.schema==1 && room?.id==SourceId && room.contentSha256==SourceHash && room.sourceIndex==40 && room.width==48 && room.height==25 &&
                room.grid?.Length==25 && room.grid.All(row=>row?.Length==48) && room.records?.Count(r=>r.tag=="obj")==57 && room.records.Count(r=>r.tag=="back")==19 &&
                source.objectDefinitions!=null,"Изменён проверенный исходный шаблон/grid/records.");
            foreach(var record in room.records.Where(r=>r.tag=="obj" || r.tag=="back"))
            {
                var xml=XElement.Parse(record.contentXml);
                Require(xml.Name.LocalName==record.tag && record.attributes!=null && xml.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value).OrderBy(a=>a.Key).SequenceEqual(record.attributes.OrderBy(a=>a.Key)),"XML/атрибуты расходятся: "+record.recordId);
            }
            var geometry=Read<RoomLayout>("geometry.json","7d34bbc64859709966b53f00cfcb296c98e6342a63c56d6f711462eabbe1bc5a",researchFolder);
            var decor=Read<RoomLayout>("decor.json","b53c903f276839fe3c6d6571a87c4b02477d5f9d232083e8d1b1d814970563b3",researchFolder);
            var manifest=Read<Manifest>("manifest.json","d0213dfa90b07637c844e71ef40fb44549455a419e97012bf9a6fd529a861d13",researchFolder);
            var capSource=Read<FactoryLadderLandingSource>("landing-source.json","7801813e11d8cf0b8b78aae9a446a20a6d3c47dc36f1e91b8acc5658ef800473",researchFolder);capSource.Validate();
            Require(geometry.platforms?.Length==52 && geometry.ladders?.Length==3 && geometry.artwork?.Length==78 && decor.artwork?.Length==19 && manifest.resources?.Length==22 && capSource.records.Length==3,"Неполная геометрия/ресурсы/посадки.");
            foreach(var record in capSource.records)
                Require(record.sourceRoom==40 && room.grid[record.row][record.column]==record.expectedToken && geometry.platforms.Count(p=>FactoryLadderLandingChecks.SameSurface(p,record.surface))==1,
                    "Изменена source-клетка/опора явной посадки: "+record.id);
            var bindings=StateJson.FromJson<GeometryBindings>(System.IO.File.ReadAllText(System.IO.Path.Combine(researchFolder,"geometry.json"))).sourceBindings;
            return new Data {source=source,geometry=geometry,decor=decor,manifest=manifest,bindings=bindings,landings=capSource.records};
        }
        private static T Read<T>(string file,string expected,string researchFolder)
        {
            string text=System.IO.File.ReadAllText(System.IO.Path.Combine(researchFolder,file)).TrimStart('\uFEFF').Replace("\r\n","\n");
            using var sha=SHA256.Create();string actual=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
            Require(actual==expected,"Изменены проверенные данные; file="+file+", actual="+actual+", expected="+expected);
            return StateJson.FromJson<T>(text);
        }
        internal static string Value(SourceRecord record,string key)=>record.attributes!=null && record.attributes.TryGetValue(key,out string result)?result:
            throw new InvalidOperationException("Склад40: нет source атрибута "+key+", record="+record.recordId);
        internal static float Number(SourceRecord record,string key)=>float.Parse(Value(record,key),NumberStyles.Float,CultureInfo.InvariantCulture);
        internal static Vector2 Anchor(Data data,SourceRecord record)
        {
            string kind=Value(record,"id");var definition=data.source.objectDefinitions[kind];var xml=XElement.Parse(definition.contentXml);
            Require(definition.attributes.OrderBy(a=>a.Key).SequenceEqual(xml.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value).OrderBy(a=>a.Key)),"Изменена XML/size декларация типа "+kind);
            float size=definition.attributes.TryGetValue("size",out string value)?float.Parse(value,CultureInfo.InvariantCulture):throw new InvalidOperationException("Склад40: не указан source size типа "+kind);
            return FactorySourceCoordinates.Placement(Number(record,"x"),Number(record,"y"),size);
        }
        internal static T Asset<T>(string path)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Склад40: отсутствует подготовленный ассет "+path);
        internal static void Require(bool condition,string reason) {if(!condition)throw new InvalidOperationException("Склад40: "+reason);}
    }
}
