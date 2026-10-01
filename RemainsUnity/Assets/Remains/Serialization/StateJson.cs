using System;
using System.Collections.Generic;
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
        [JsonExtensionData] public IDictionary<string,JToken> extensions;
        [System.Runtime.Serialization.OnSerializing]
        internal void CheckExtensionNames(System.Runtime.Serialization.StreamingContext context)
        {
            if(extensions==null) return;
            var fields=GetType().GetFields(BindingFlags.Instance|BindingFlags.Public);
            foreach(string key in extensions.Keys)
                if(fields.Any(f=>!Attribute.IsDefined(f,typeof(JsonExtensionDataAttribute)) && string.Equals(f.Name,key,StringComparison.OrdinalIgnoreCase)))
                    throw new System.IO.InvalidDataException($"Дополнительное поле {key} конфликтует с полем состояния {GetType().Name}.");
        }
    }
    // Persistent/network state uses public fields, not computed gameplay properties.
    public static class StateJson
    {
        private sealed class FieldContract : DefaultContractResolver
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
        private static void CheckFieldNames(Type type,JToken token)
        {
            if(token is JValue number && number.Value is double binary)
            {
                if(type!=typeof(float) && type!=typeof(double) || type==typeof(float) &&
                    (float.IsInfinity((float)binary) || binary!=0 && (float)binary==0))
                    throw new System.IO.InvalidDataException($"Число JSON вне точного диапазона поля {type.Name}: {token.Path}. Неизвестные числовые данные вне Decimal храните строкой.");
            }
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
