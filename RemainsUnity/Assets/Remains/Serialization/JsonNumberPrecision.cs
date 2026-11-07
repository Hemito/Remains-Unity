using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace Remains.Serialization
{
    // Decimal parsing must fail explicitly instead of rounding unknown mod values.
    internal static class JsonNumberPrecision
    {
        private static readonly char[] exponentMarkers={'e','E'};
        private sealed class ReaderBufferPool : IArrayPool<char>
        {
            public static readonly ReaderBufferPool Shared=new();
            public char[] Rent(int minimumLength)=>ArrayPool<char>.Shared.Rent(minimumLength);
            public void Return(char[] array)=>ArrayPool<char>.Shared.Return(array);
        }
        public sealed class Reader : JsonTextReader
        {
            private readonly string source;
            private int scan;
            public Reader(string json):base(new StringReader(json))
            {
                source=json;
                DateParseHandling=DateParseHandling.None;FloatParseHandling=FloatParseHandling.Double;MaxDepth=128;
                ArrayPool=ReaderBufferPool.Shared;
            }
            public override bool Read()
            {
                bool read=base.Read();
                if(read && (TokenType==JsonToken.Comment ||
                    (TokenType==JsonToken.String || TokenType==JsonToken.PropertyName) && QuoteChar!='"'))
                    throw new InvalidDataException($"Состояние требует JSON без комментариев и с двойными кавычками: {Path}.");
                if(!read || TokenType!=JsonToken.Float && TokenType!=JsonToken.Integer) return read;
                var raw=NextNumber(TokenType==JsonToken.Float);
                if(TokenType==JsonToken.Float)
                {
                    double binary=Convert.ToDouble(Value,CultureInfo.InvariantCulture);
                    if(double.IsNaN(binary) || double.IsInfinity(binary)) throw new InvalidDataException($"Неконечное число JSON: {Path}.");
                    if(binary==0 && !HasZeroMantissa(raw)) throw new InvalidDataException($"Число JSON меньше диапазона Double: {Path}.");
                    if(decimal.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var exact) &&
                        IsExactDecimal(raw,exact)) SetToken(JsonToken.Float,exact);
                    // Binary numbers are accepted later only for declared float/double fields.
                }
                return read;
            }
            // The JSON reader remains responsible for syntax, duplicates and depth.
            // This cursor only recovers the original numeric spelling. Long XML/mod
            // strings are skipped once, without Regex Match/backtracking allocations.
            private ReadOnlySpan<char> NextNumber(bool spelling)
            {
                while(scan<source.Length)
                {
                    char value=source[scan++];
                    if(value=='"')
                    {
                        while(scan<source.Length)
                        {
                            char part=source[scan++];
                            if(part=='\\') {if(scan<source.Length) scan++;}
                            else if(part=='"') break;
                        }
                        continue;
                    }
                    if(!Digit(value) && !(value=='-' && scan<source.Length && Digit(source[scan]))) continue;
                    int start=scan-1;
                    while(scan<source.Length && Digit(source[scan])) scan++;
                    if(scan+1<source.Length && source[scan]=='.' && Digit(source[scan+1]))
                    {scan+=2;while(scan<source.Length && Digit(source[scan])) scan++;}
                    if(scan<source.Length && (source[scan]=='e' || source[scan]=='E'))
                    {
                        int marker=scan++;if(scan<source.Length && (source[scan]=='+' || source[scan]=='-')) scan++;
                        if(scan<source.Length && Digit(source[scan]))
                        {while(scan<source.Length && Digit(source[scan])) scan++;}
                        else scan=marker;
                    }
                    return spelling?source.AsSpan(start,scan-start):default;
                }
                throw new InvalidDataException($"Некорректное число JSON: {Path}.");
            }
            private static bool Digit(char value)=>value>='0' && value<='9';
        }
        private static bool HasZeroMantissa(ReadOnlySpan<char> raw)
        {
            foreach(char c in raw)
            {
                if(c is 'e' or 'E')break;
                if(c is >= '1' and <= '9')return false;
            }
            return true;
        }
        private static bool IsExactDecimal(ReadOnlySpan<char> raw,decimal parsed)
        {
            // Without an exponent, at most 28 input characters fit both Decimal's
            // significant-digit and fractional-scale limits. TryParse cannot round them.
            if(raw.Length<=28 && raw.IndexOfAny('e','E')<0)return true;
            return Normalize(raw.ToString())==Normalize(parsed.ToString("G29",CultureInfo.InvariantCulture));
        }
        private static string Normalize(string value)
        {
            int marker=value.IndexOfAny(exponentMarkers);
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
