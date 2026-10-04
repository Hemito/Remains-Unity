using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Remains.Editor
{
    internal static class OriginalTrainingRoomSource
    {
        [Serializable] private sealed class Report {public Source[] sources;}
        [Serializable] private sealed class Source {public Room[] rooms;}
        [Serializable] private sealed class Room {public string contentXml;}
        public static string Research=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/original"));
        public static XElement Read()
        {
            var report=JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Research,"room-content-20261004.json")));
            return report.sources.SelectMany(s=>s.rooms).Select(r=>XElement.Parse(r.contentXml))
                .Single(r=>(string)r.Attribute("name")=="room_1_0");
        }
        public static Vector3 Position(XElement room,XElement item)=>new(
            ((int)item.Attribute("x")+.5f)*.8f,(room.Elements("a").Count()-(int)item.Attribute("y")-1)*.8f,0);
    }
}
