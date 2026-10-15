using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Remains.Serialization
{
    [Serializable]
    public abstract class ExtensibleState
    {
        private static readonly ConcurrentDictionary<Type,HashSet<string>> reservedNames=new();
        private static HashSet<string> ReadReservedNames(Type type)=>new(
            type.GetFields(BindingFlags.Instance|BindingFlags.Public)
                .Where(field=>!Attribute.IsDefined(field,typeof(JsonExtensionDataAttribute))).Select(field=>field.Name),
            StringComparer.OrdinalIgnoreCase);
        [JsonExtensionData] public IDictionary<string,JToken> extensions;
        [System.Runtime.Serialization.OnSerializing]
        internal void CheckExtensionNames(System.Runtime.Serialization.StreamingContext context)
        {
            if(extensions==null) return;
            // Contracts are immutable by CLR type. Do not repeat reflection and
            // allocate predicates for every item in a large checkpoint.
            var fields=reservedNames.GetOrAdd(GetType(),ReadReservedNames);
            foreach(string key in extensions.Keys)
                if(fields.Contains(key))
                    throw new System.IO.InvalidDataException($"Дополнительное поле {key} конфликтует с полем состояния {GetType().Name}.");
        }
    }
    // Persistent/network state uses public fields, not computed gameplay properties.
    public static class StateJson
    {
        private class FieldContract : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type type)=>
                type.GetFields(BindingFlags.Instance|BindingFlags.Public).Where(f=>!f.IsNotSerialized).Cast<MemberInfo>().ToList();
        }
        private static readonly JsonSerializerSettings settings=new()
        {
            ContractResolver=new FieldContract(),TypeNameHandling=TypeNameHandling.None,MetadataPropertyHandling=MetadataPropertyHandling.Ignore,
            DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Decimal,
            ObjectCreationHandling=ObjectCreationHandling.Replace,MaxDepth=128,
            Culture=System.Globalization.CultureInfo.InvariantCulture
        };
        public static string ToJson(object value,bool prettyPrint=false)=>
            JsonConvert.SerializeObject(value,prettyPrint?Formatting.Indented:Formatting.None,settings);
        // The same contract with extra converters (e.g. composing a large state from cached parts).
        public static string ToJson(object value,IEnumerable<JsonConverter> converters)
        {
            var serializer=JsonSerializer.Create(settings);
            foreach(var converter in converters) serializer.Converters.Add(converter);
            var text=new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            using(var writer=new JsonTextWriter(text) {Formatting=Formatting.None}) serializer.Serialize(writer,value);
            return text.ToString();
        }
        // A value's text with the named fields of its type left as raw markers \u0002<index>\u0002 (never in serializer
        // output), and those fields' own texts: FillHoles(template, holes) is exactly ToJson(value) (TypeNameHandling
        // None and no member converters, so a field serializes the same alone and in place). A null field is written
        // in place (no marker) and its hole is null.
        public static string ToJsonWithHoles(object value,IReadOnlyList<string> fields,out string[] holes)
        {
            if(value==null) throw new ArgumentNullException(nameof(value));
            var type=value.GetType();
            var serializer=JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver=HoleContracts.GetOrAdd((type,string.Join(",",fields)),key=>new HoleContract(key.type,fields)),
                TypeNameHandling=settings.TypeNameHandling,MetadataPropertyHandling=settings.MetadataPropertyHandling,
                DateParseHandling=settings.DateParseHandling,FloatParseHandling=settings.FloatParseHandling,
                ObjectCreationHandling=settings.ObjectCreationHandling,MaxDepth=settings.MaxDepth,Culture=settings.Culture
            });
            var text=new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            using(var writer=new JsonTextWriter(text) {Formatting=Formatting.None}) serializer.Serialize(writer,value);
            holes=new string[fields.Count];
            for(int i=0;i<fields.Count;i++)
            {
                var field=type.GetField(fields[i],BindingFlags.Instance|BindingFlags.Public)??
                    throw new ArgumentException($"Нет поля {fields[i]} у {type.Name}.");
                var member=field.GetValue(value);
                holes[i]=member==null?null:ToJson(member);
            }
            return text.ToString();
        }
        public static string FillHoles(string template,IReadOnlyList<string> holes)
        {
            var result=new System.Text.StringBuilder(template.Length+holes.Sum(hole=>hole?.Length??0));
            int at=0;
            while(at<template.Length)
            {
                int start=template.IndexOf('\u0002',at);
                if(start<0) {result.Append(template,at,template.Length-at);break;}
                int end=template.IndexOf('\u0002',start+1);
                if(end<0 || !int.TryParse(template.AsSpan(start+1,end-start-1),out int index) || index<0 || index>=holes.Count || holes[index]==null)
                    throw new System.IO.InvalidDataException("Повреждённый шаблон текста с пропусками.");
                result.Append(template,at,start-at).Append(holes[index]);
                at=end+1;
            }
            return result.ToString();
        }
        private static readonly ConcurrentDictionary<(Type type,string fields),HoleContract> HoleContracts=new();
        private sealed class HoleContract : FieldContract
        {
            private readonly Type owner;private readonly IReadOnlyList<string> fields;
            public HoleContract(Type owner,IReadOnlyList<string> fields) {this.owner=owner;this.fields=fields;}
            protected override JsonProperty CreateProperty(MemberInfo member,MemberSerialization serialization)
            {
                var property=base.CreateProperty(member,serialization);
                if(member.ReflectedType==owner)
                    for(int i=0;i<fields.Count;i++)
                        if(member.Name==fields[i]) {property.Converter=new HoleConverter(i);break;}
                return property;
            }
        }
        private sealed class HoleConverter : JsonConverter
        {
            private readonly string marker;
            public HoleConverter(int index) {marker="\u0002"+index+"\u0002";}
            public override bool CanRead=>false;
            public override bool CanConvert(Type objectType)=>true;
            public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer)=>throw new NotSupportedException();
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)=>writer.WriteRawValue(marker);
        }
        public static T FromJson<T>(string json)
        {
            try
            {
                using var reader=new JsonNumberPrecision.Reader(json);
                var token=JToken.Load(reader,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                if(reader.Read()) throw new JsonReaderException("После состояния обнаружены дополнительные данные.");
                CheckFieldNames(typeof(T),token);
                return token.ToObject<T>(JsonSerializer.Create(settings));
            }
            catch(JsonException failure) {throw new System.IO.InvalidDataException($"Некорректное состояние {typeof(T).Name}: {failure.Message}",failure);}
        }
        // Same contract and number handling as FromJson, without the strictness pass (no JToken tree, no field/number
        // checks). Only for text this process already accepted through FromJson (e.g. its own validated snapshot).
        public static T FromTrustedJson<T>(string json)
        {
            try
            {
                // Plain reader: JsonNumberPrecision.Reader tracks number spelling through Read() only, and direct
                // deserialization reads some numbers via ReadAsX, which would desynchronise it.
                using var reader=new JsonTextReader(new System.IO.StringReader(json))
                    {DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Decimal,MaxDepth=128};
                return JsonSerializer.Create(settings).Deserialize<T>(reader);
            }
            catch(JsonException failure) {throw new System.IO.InvalidDataException($"Некорректное состояние {typeof(T).Name}: {failure.Message}",failure);}
        }
        private static void CheckFieldNames(Type type,JToken token)
        {
            if(token is JValue number && number.Value is double binary)
            {
                if(type!=typeof(float) && type!=typeof(double) || type==typeof(float) &&
                    (float.IsInfinity((float)binary) || binary!=0 && (float)binary==0))
                    throw new System.IO.InvalidDataException($"Число JSON вне точного диапазона поля {type.Name}: {token.Path}. Неизвестные числовые данные вне Decimal храните строкой.");
            }
            // Scalars have no field names or children to inspect. Numeric range
            // validation above still applies before this fast path.
            if(token is JValue) return;
            var contract=settings.ContractResolver.ResolveContract(type);
            if(contract is JsonArrayContract array && token is JArray items)
                foreach(var item in items) CheckFieldNames(array.CollectionItemType,item);
            if(contract is not JsonObjectContract shape || token is not JObject source) return;
            foreach(var property in source.Properties())
            {
                var field=shape.Properties.GetClosestMatchProperty(property.Name);
                if(field==null || field.Ignored) {CheckUnknownNumbers(property.Value);continue;}
                if(field.PropertyName!=property.Name)
                    throw new System.IO.InvalidDataException($"Неоднозначное имя поля {property.Name}: поле {type.Name}.{field.PropertyName} отличается регистром.");
                CheckFieldNames(field.PropertyType,property.Value);
            }
        }
        private static void CheckUnknownNumbers(JToken token)
        {
            if(token is JValue value && value.Value is double)
                throw new System.IO.InvalidDataException($"Неизвестное число нельзя сохранить без потери точности: {token.Path}. Используйте строку для значений вне Decimal.");
            foreach(var child in token.Children()) CheckUnknownNumbers(child);
        }
        public static T Copy<T>(T value)=>FromJson<T>(ToJson(value));
    }
}
