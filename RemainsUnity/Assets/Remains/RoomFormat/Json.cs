using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Remains.RoomFormat
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    // Minimal ordered JSON tree. No external dependency: the same code runs in Unity
    // and in the .NET converter, and the writer output is byte-deterministic.
    public sealed class JsonValue
    {
        public JsonKind Kind { get; private set; }
        public bool Bool { get; private set; }
        public string Text { get; private set; }               // string value or raw number
        public List<JsonValue> Items { get; private set; }
        public List<KeyValuePair<string, JsonValue>> Fields { get; private set; }

        public static readonly JsonValue Null = new JsonValue { Kind = JsonKind.Null };
        public static JsonValue Of(bool value) => new JsonValue { Kind = JsonKind.Bool, Bool = value };
        public static JsonValue Of(string value) => value == null ? Null : new JsonValue { Kind = JsonKind.String, Text = value };
        public static JsonValue Of(long value) => new JsonValue { Kind = JsonKind.Number, Text = value.ToString(CultureInfo.InvariantCulture) };
        public static JsonValue Of(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("JSON не поддерживает нечисловое значение: " + value);
            return new JsonValue { Kind = JsonKind.Number, Text = value.ToString("R", CultureInfo.InvariantCulture) };
        }
        public static JsonValue NewArray() => new JsonValue { Kind = JsonKind.Array, Items = new List<JsonValue>() };
        public static JsonValue NewObject() => new JsonValue { Kind = JsonKind.Object, Fields = new List<KeyValuePair<string, JsonValue>>() };
        internal static JsonValue Raw(JsonKind kind, string text) => new JsonValue { Kind = kind, Text = text };

        public JsonValue Add(JsonValue item) { Require(JsonKind.Array); Items.Add(item); return this; }
        public JsonValue Set(string key, JsonValue value)
        {
            Require(JsonKind.Object);
            if (key == null) throw new ArgumentNullException(nameof(key));
            for (int i = 0; i < Fields.Count; i++)
                if (Fields[i].Key == key) throw new InvalidOperationException("Повтор ключа JSON: " + key);
            Fields.Add(new KeyValuePair<string, JsonValue>(key, value ?? Null));
            return this;
        }
        public JsonValue Get(string key)
        {
            Require(JsonKind.Object);
            foreach (var field in Fields) if (field.Key == key) return field.Value;
            return null;
        }
        public string AsString(string context)
        {
            if (Kind != JsonKind.String) throw new FormatException(context + ": ожидалась строка, получено " + Kind);
            return Text;
        }
        public long AsLong(string context)
        {
            if (Kind != JsonKind.Number || !long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                throw new FormatException(context + ": ожидалось целое число, получено " + Kind + " " + Text);
            return value;
        }
        public double AsDouble(string context)
        {
            if (Kind != JsonKind.Number || !double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException(context + ": ожидалось число, получено " + Kind + " " + Text);
            return value;
        }
        private void Require(JsonKind kind)
        {
            if (Kind != kind) throw new InvalidOperationException("Операция требует JSON " + kind + ", а значение " + Kind);
        }
    }

    public static class JsonText
    {
        public static JsonValue Parse(string text, string source)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var parser = new Parser(text, source);
            var value = parser.ReadValue();
            parser.SkipSpace();
            if (!parser.AtEnd) throw parser.Error("лишние данные после значения");
            return value;
        }

        // Layout: objects and arrays are indented. On one line: anything nested at depth >=
        // compactDepth, arrays of non-string scalars, small all-scalar objects, and every item
        // of an array stored under a key from compactItemsOf (one record per line).
        // Arrays of strings (layer rows) stay one string per line.
        public static string Write(JsonValue value, int compactDepth = 3, ICollection<string> compactItemsOf = null)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, 0, compactDepth, compactItemsOf, false);
            builder.Append('\n');
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder b, JsonValue v, int depth, int compactDepth, ICollection<string> compactItemsOf, bool forceCompact)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: b.Append("null"); return;
                case JsonKind.Bool: b.Append(v.Bool ? "true" : "false"); return;
                case JsonKind.Number: b.Append(v.Text); return;
                case JsonKind.String: WriteString(b, v.Text); return;
            }
            bool compact = forceCompact || depth >= compactDepth || IsFlat(v);
            bool isArray = v.Kind == JsonKind.Array;
            int count = isArray ? v.Items.Count : v.Fields.Count;
            b.Append(isArray ? '[' : '{');
            if (count == 0) { b.Append(isArray ? ']' : '}'); return; }
            for (int i = 0; i < count; i++)
            {
                if (i > 0) b.Append(compact ? ", " : ",");
                if (!compact) { b.Append('\n'); Indent(b, depth + 1); }
                if (!isArray)
                {
                    string key = v.Fields[i].Key;
                    var child = v.Fields[i].Value;
                    WriteString(b, key);
                    b.Append(": ");
                    if (!compact && child.Kind == JsonKind.Array && compactItemsOf != null && compactItemsOf.Contains(key))
                        WriteRecords(b, child, depth + 1, compactDepth, compactItemsOf);
                    else WriteValue(b, child, depth + 1, compactDepth, compactItemsOf, compact);
                }
                else WriteValue(b, v.Items[i], depth + 1, compactDepth, compactItemsOf, compact);
            }
            if (!compact) { b.Append('\n'); Indent(b, depth); }
            b.Append(isArray ? ']' : '}');
        }

        // Array whose items are each written on their own line, compact.
        private static void WriteRecords(StringBuilder b, JsonValue array, int depth, int compactDepth, ICollection<string> compactItemsOf)
        {
            if (array.Items.Count == 0) { b.Append("[]"); return; }
            b.Append('[');
            for (int i = 0; i < array.Items.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append('\n');
                Indent(b, depth + 1);
                WriteValue(b, array.Items[i], depth + 1, compactDepth, compactItemsOf, true);
            }
            b.Append('\n');
            Indent(b, depth);
            b.Append(']');
        }

        private const int SmallObjectFields = 4;

        private static bool IsFlat(JsonValue v)
        {
            if (v.Kind == JsonKind.Array)
            {
                foreach (var item in v.Items) if (item.Kind == JsonKind.Array || item.Kind == JsonKind.Object || item.Kind == JsonKind.String) return false;
                return true;
            }
            if (v.Fields.Count > SmallObjectFields) return false;
            foreach (var field in v.Fields) if (field.Value.Kind == JsonKind.Array || field.Value.Kind == JsonKind.Object) return false;
            return true;
        }

        private static void Indent(StringBuilder b, int depth) => b.Append(' ', depth * 2);

        private static void WriteString(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }

        private sealed class Parser
        {
            private readonly string text, source;
            private int position;
            public Parser(string text, string source) { this.text = text; this.source = source ?? "<json>"; }
            public bool AtEnd => position >= text.Length;

            public FormatException Error(string reason)
            {
                int line = 1, column = 1;
                for (int i = 0; i < position && i < text.Length; i++) { if (text[i] == '\n') { line++; column = 1; } else column++; }
                return new FormatException($"Ошибка JSON в {source}:{line}:{column}: {reason}.");
            }

            public void SkipSpace() { while (!AtEnd && (text[position] == ' ' || text[position] == '\t' || text[position] == '\n' || text[position] == '\r')) position++; }

            public JsonValue ReadValue()
            {
                SkipSpace();
                if (AtEnd) throw Error("неожиданный конец");
                char c = text[position];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return JsonValue.Of(ReadString());
                if (Match("true")) return JsonValue.Of(true);
                if (Match("false")) return JsonValue.Of(false);
                if (Match("null")) return JsonValue.Null;
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw Error("неожиданный символ '" + c + "'");
            }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0) return false;
                position += word.Length;
                return true;
            }

            private JsonValue ReadObject()
            {
                var result = JsonValue.NewObject();
                position++;
                SkipSpace();
                if (!AtEnd && text[position] == '}') { position++; return result; }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || text[position] != '"') throw Error("ожидался ключ-строка");
                    string key = ReadString();
                    SkipSpace();
                    if (AtEnd || text[position] != ':') throw Error("ожидалось ':'");
                    position++;
                    var value = ReadValue();
                    if (result.Get(key) != null) throw Error("повтор ключа \"" + key + "\"");
                    result.Set(key, value);
                    SkipSpace();
                    if (AtEnd) throw Error("незакрытый объект");
                    if (text[position] == ',') { position++; continue; }
                    if (text[position] == '}') { position++; return result; }
                    throw Error("ожидалось ',' или '}'");
                }
            }

            private JsonValue ReadArray()
            {
                var result = JsonValue.NewArray();
                position++;
                SkipSpace();
                if (!AtEnd && text[position] == ']') { position++; return result; }
                while (true)
                {
                    result.Add(ReadValue());
                    SkipSpace();
                    if (AtEnd) throw Error("незакрытый массив");
                    if (text[position] == ',') { position++; continue; }
                    if (text[position] == ']') { position++; return result; }
                    throw Error("ожидалось ',' или ']'");
                }
            }

            private string ReadString()
            {
                var b = new StringBuilder();
                position++;
                while (true)
                {
                    if (AtEnd) throw Error("незакрытая строка");
                    char c = text[position++];
                    if (c == '"') return b.ToString();
                    if (c < 0x20) throw Error("управляющий символ внутри строки");
                    if (c != '\\') { b.Append(c); continue; }
                    if (AtEnd) throw Error("обрыв escape-последовательности");
                    char e = text[position++];
                    switch (e)
                    {
                        case '"': b.Append('"'); break;
                        case '\\': b.Append('\\'); break;
                        case '/': b.Append('/'); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break;
                        case 't': b.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length || !int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("неверная \\u-последовательность");
                            b.Append((char)code);
                            position += 4;
                            break;
                        default: throw Error("неизвестная escape-последовательность \\" + e);
                    }
                }
            }

            private JsonValue ReadNumber()
            {
                int start = position;
                if (text[position] == '-') position++;
                while (!AtEnd && "0123456789.eE+-".IndexOf(text[position]) >= 0) position++;
                string raw = text.Substring(start, position - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw Error("неверное число " + raw);
                return JsonValue.Raw(JsonKind.Number, raw);
            }
        }
    }
}
