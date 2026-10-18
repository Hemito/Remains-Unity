using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Remains.Serialization
{
    // Detached copies of an already validated wire graph. This is not a parser or a validation boundary.
    // Keep the field contract identical to StateJson; unsupported contracts fail rather than sharing objects.
    public static class ValidatedStateCopy
    {
        private sealed class Shape
        {
            public Func<object> Create;
            public JsonProperty[] Properties;
        }
        private static readonly ConcurrentDictionary<Type,Shape> shapes=new();

        public static T Copy<T>(T validated)=> (T)Clone(validated,typeof(T),0);

        private static object Clone(object value,Type declared,int depth)
        {
            if(value==null) return null;
            if(depth>128) throw Unsupported(declared,"превышена глубина 128 (возможен цикл)");
            var type=value.GetType();
            if(value is JToken token) return token.DeepClone();
            if(type!=(Nullable.GetUnderlyingType(declared)??declared))
                throw Unsupported(type,"полиморфное поле "+declared.FullName);
            if(type.IsEnum || type==typeof(string) || type==typeof(bool) || type==typeof(char) ||
                type==typeof(byte) || type==typeof(sbyte) || type==typeof(short) || type==typeof(ushort) ||
                type==typeof(int) || type==typeof(uint) || type==typeof(long) || type==typeof(ulong) ||
                type==typeof(float) || type==typeof(double) || type==typeof(decimal)) return value;
            if(type.IsArray)
            {
                var source=(Array)value;
                if(source.Rank!=1 || source.GetLowerBound(0)!=0) throw Unsupported(type,"многомерный массив");
                var element=type.GetElementType();var result=Array.CreateInstance(element,source.Length);
                for(int i=0;i<source.Length;i++) result.SetValue(Clone(source.GetValue(i),element,depth+1),i);
                return result;
            }
            if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(List<>))
            {
                var element=type.GetGenericArguments()[0];var result=(IList)Activator.CreateInstance(type);
                foreach(var item in (IList)value) result.Add(Clone(item,element,depth+1));
                return result;
            }
            if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(Dictionary<,>))
            {
                var arguments=type.GetGenericArguments();
                if(arguments[0]!=typeof(string)) throw Unsupported(type,"ключ словаря не строка");
                var result=(IDictionary)Activator.CreateInstance(type);
                foreach(DictionaryEntry pair in (IDictionary)value) result.Add(pair.Key,Clone(pair.Value,arguments[1],depth+1));
                return result;
            }
            var shape=shapes.GetOrAdd(type,Build);
            var copy=shape.Create();
            foreach(var property in shape.Properties)
                property.ValueProvider.SetValue(copy,Clone(property.ValueProvider.GetValue(value),property.PropertyType,depth+1));
            if(value is ExtensibleState sourceState)
            {
                var target=(ExtensibleState)copy;
                if(sourceState.extensions!=null)
                {
                    target.extensions=new Dictionary<string,JToken>();
                    foreach(var pair in sourceState.extensions) target.extensions.Add(pair.Key,pair.Value?.DeepClone());
                }
            }
            return copy;
        }

        private static Shape Build(Type type)
        {
            if(!type.IsSerializable || type.IsAbstract || !(type.Namespace=="Remains.Domain" || type.Namespace=="Remains.Content"))
                throw Unsupported(type,"разрешены только сериализуемые DTO Domain/Content");
            for(var current=type;current!=null && current!=typeof(object);current=current.BaseType)
                foreach(var method in current.GetMethods(BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    if(method.GetCustomAttributes(false).Any(attribute=>attribute is OnSerializingAttribute || attribute is OnSerializedAttribute ||
                        attribute is OnDeserializingAttribute || attribute is OnDeserializedAttribute))
                    {
                        if(current==typeof(ExtensibleState) && method.Name=="CheckExtensionNames") continue;
                        throw Unsupported(type,"callback сериализации "+method.Name);
                    }
            if(StateJson.ContractFor(type) is not JsonObjectContract contract || contract.Converter!=null || contract.DefaultCreator==null)
                throw Unsupported(type,"неподдерживаемый контракт или конструктор");
            var properties=contract.Properties.Where(property=>!property.Ignored).ToArray();
            foreach(var property in properties)
                if(!property.Readable || !property.Writable || property.Converter!=null || property.ItemConverter!=null || property.ShouldSerialize!=null ||
                    property.GetIsSpecified!=null || property.SetIsSpecified!=null)
                    throw Unsupported(type,"неподдерживаемое поле "+property.PropertyName);
            return new Shape {Create=contract.DefaultCreator,Properties=properties};
        }
        private static InvalidOperationException Unsupported(Type type,string reason)=>
            new($"Нельзя скопировать проверенное состояние {type.FullName}: {reason}.");
    }
}
