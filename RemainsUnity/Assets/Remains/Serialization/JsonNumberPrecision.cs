using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Remains.Serialization
{
    // Decimal parsing must fail explicitly instead of rounding unknown mod values.
    internal static class JsonNumberPrecision
    {
        private static readonly Regex tokens=new("\"(?:\\\\.|[^\"\\\\])*\"|-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?",RegexOptions.Compiled|RegexOptions.CultureInvariant);
        public static void Validate(string json)
        {
            foreach(Match match in tokens.Matches(json))
            {
                string value=match.Value;
                if(value[0]=='"' || value.IndexOfAny(new[]{'.','e','E'})<0) continue;
                if(!decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) ||
                    Normalize(value)!=Normalize(number.ToString("G29",CultureInfo.InvariantCulture)))
                    throw new InvalidDataException($"Число JSON нельзя сохранить без потери точности, позиция {match.Index}. Используйте строку для значений вне диапазона Decimal.");
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
