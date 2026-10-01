using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Remains.Serialization
{
    // Decimal parsing must fail explicitly instead of rounding unknown mod values.
    internal static class JsonNumberPrecision
    {
        private static readonly Regex tokens=new("\"(?:\\\\.|[^\"\\\\])*\"|-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?",RegexOptions.Compiled|RegexOptions.CultureInvariant);
        public sealed class Reader : JsonTextReader
        {
            private readonly Queue<string> numbers=new();
            public Reader(string json):base(new StringReader(json))
            {
                DateParseHandling=DateParseHandling.None;FloatParseHandling=FloatParseHandling.Double;MaxDepth=128;
                foreach(Match match in tokens.Matches(json)) if(match.Value[0]!='"') numbers.Enqueue(match.Value);
            }
            public override bool Read()
            {
                bool read=base.Read();
                if(read && (TokenType==JsonToken.Comment ||
                    (TokenType==JsonToken.String || TokenType==JsonToken.PropertyName) && QuoteChar!='"'))
                    throw new InvalidDataException($"Состояние требует JSON без комментариев и с двойными кавычками: {Path}.");
                if(!read || TokenType!=JsonToken.Float && TokenType!=JsonToken.Integer) return read;
                if(numbers.Count==0) throw new InvalidDataException($"Некорректное число JSON: {Path}.");
                string raw=numbers.Dequeue();
                if(TokenType==JsonToken.Float)
                {
                    double binary=Convert.ToDouble(Value,CultureInfo.InvariantCulture);
                    if(double.IsNaN(binary) || double.IsInfinity(binary)) throw new InvalidDataException($"Неконечное число JSON: {Path}.");
                    if(binary==0 && Normalize(raw)!="0") throw new InvalidDataException($"Число JSON меньше диапазона Double: {Path}.");
                    if(decimal.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var exact) &&
                        Normalize(raw)==Normalize(exact.ToString("G29",CultureInfo.InvariantCulture))) SetToken(JsonToken.Float,exact);
                    // Binary numbers are accepted later only for declared float/double fields.
                }
                return read;
            }
        }
        private static string Normalize(string value)
        {
            int marker=value.IndexOfAny(new[]{'e','E'});
            long exponent=marker<0?0:long.Parse(value.Substring(marker+1),CultureInfo.InvariantCulture);
            string mantissa=marker<0?value:value.Substring(0,marker);
            bool negative=mantissa.StartsWith("-",StringComparison.Ordinal);
            if(negative) mantissa=mantissa.Substring(1);
            int dot=mantissa.IndexOf('.');
            long scale=exponent-(dot<0?0:mantissa.Length-dot-1);
            string digits=mantissa.Replace(".","").TrimStart('0');
            if(digits.Length==0) return "0";
            int length=digits.Length;digits=digits.TrimEnd('0');scale+=length-digits.Length;
            return (negative?"-":"")+digits+"e"+scale.ToString(CultureInfo.InvariantCulture);
        }
    }
}
