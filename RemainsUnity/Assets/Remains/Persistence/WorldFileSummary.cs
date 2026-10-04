using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // Browsing reads lineage only. Gameplay, mods and unknown state are validated
    // from the complete immutable payload when it is actually selected/loaded.
    public static class WorldFileSummary
    {
        public static WorldSummary Read(string path)
        {
            string json=File.ReadAllText(path);
            var summary=new WorldSummary {ancestors=new List<WorldAncestor>()};int schema=0;
            using var source=new StringReader(json);
            using var reader=new JsonTextReader(source) {DateParseHandling=DateParseHandling.None,MaxDepth=128};
            if(!reader.Read() || reader.TokenType!=JsonToken.StartObject) throw Invalid(path,"ожидался объект JSON");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while(reader.Read() && reader.TokenType!=JsonToken.EndObject)
            {
                if(reader.TokenType!=JsonToken.PropertyName) throw Invalid(path,"ожидалось имя поля");
                string name=(string)reader.Value;
                if(!names.Add(name)) throw Invalid(path,"повторное/неоднозначное имя поля "+name);
                if(!reader.Read()) throw Invalid(path,"нет значения поля "+name);
                switch(name)
                {
                    case "schema": schema=Integer<int>(reader,path,name);break;
                    case "worldId": summary.worldId=Text(reader,path,name);break;
                    case "branchId": summary.branchId=Text(reader,path,name);break;
                    case "historyVersion": summary.historyVersion=Integer<int>(reader,path,name);break;
                    case "checkpointSequence": summary.sequence=Integer<long>(reader,path,name);break;
                    case "revision": summary.revision=Integer<long>(reader,path,name);break;
                    case "ancestors": summary.ancestors=StateJson.FromJson<List<WorldAncestor>>(JToken.ReadFrom(reader).ToString(Formatting.None));break;
                    default: reader.Skip();break;
                }
            }
            if(reader.TokenType!=JsonToken.EndObject || reader.Read()) throw Invalid(path,"неполный JSON или дополнительные данные");
            if(schema!=1) throw Invalid(path,"неподдерживаемый формат "+schema);
            summary.snapshot=RecoverySnapshot.Fingerprint(json);Validate(summary,path);return summary;
        }
        public static void Validate(WorldSummary summary,string context)
        {
            if(summary==null || !Guid.TryParseExact(summary.worldId,"N",out _) ||
                summary.snapshot==null || summary.snapshot.Length!=64 || summary.revision<0 ||
                summary.historyVersion<0 || summary.historyVersion>1 || summary.sequence<0)
                throw Invalid(context,"некорректная идентичность или версия истории");
            foreach(char c in summary.snapshot) if(!Uri.IsHexDigit(c)) throw Invalid(context,"некорректный ID снимка");
            if(summary.historyVersion==1 && !Guid.TryParseExact(summary.branchId,"N",out _)) throw Invalid(context,"некорректная ветвь");
            if(summary.ancestors!=null) foreach(var ancestor in summary.ancestors)
                if(ancestor==null || !Guid.TryParseExact(ancestor.branchId,"N",out _) || ancestor.sequence<0)
                    throw Invalid(context,"некорректный предок ветви");
        }
        private static T Integer<T>(JsonTextReader reader,string path,string name)
        {
            if(reader.TokenType!=JsonToken.Integer) throw Invalid(path,"поле "+name+" должно быть целым");
            try {return (T)Convert.ChangeType(reader.Value,typeof(T),System.Globalization.CultureInfo.InvariantCulture);}
            catch(Exception error) when(error is OverflowException || error is FormatException || error is InvalidCastException)
            {throw Invalid(path,"число вне диапазона в поле "+name);}
        }
        private static string Text(JsonTextReader reader,string path,string name)
        {
            if(reader.TokenType==JsonToken.Null) return null;
            if(reader.TokenType!=JsonToken.String) throw Invalid(path,"поле "+name+" должно быть строкой");
            return (string)reader.Value;
        }
        private static InvalidDataException Invalid(string path,string reason)=>new($"Описание мира {path}: {reason}.");
    }
}
