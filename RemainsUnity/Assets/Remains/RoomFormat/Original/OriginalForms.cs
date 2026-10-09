using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Remains.RoomFormat.Original
{
    // A tile form from the original AllData <mat> list (code-derived from fe/loc/Form.as).
    public sealed class OriginalForm
    {
        public string Id, Name, Back, Mirror;
        public int Ed, Vid, Diagon, Stair, Hp, Threshold, Mat;
        // Drawing layers (fe/graph/Material): texture name and its shape mask per layer.
        public string MainTexture, MainMask, AltTexture, BorderTexture, BorderMask, FloorTexture, FloorMask, Filter;
        public bool Shelf, Rear, Solid, Indestructible;
        // Form.as: a form without vid draws itself as the tile front.
        public string Front => Vid > 0 ? null : Id;
    }

    public sealed class OriginalForms
    {
        private static readonly Regex MatTag = new Regex(@"<mat\s([^>]*?)(/>|>(.*?)</mat>)", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex ChildTag = new Regex(@"<(main|border|floor|filter)\s([^>]*?)/?>", RegexOptions.CultureInvariant);
        private static readonly Regex Attribute = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:'([^']*)'|""([^""]*)"")", RegexOptions.CultureInvariant);

        // fForms: ed=1 (first character); oForms: every other ed (following characters).
        public readonly Dictionary<string, OriginalForm> Front = new Dictionary<string, OriginalForm>(StringComparer.Ordinal);
        public readonly Dictionary<string, OriginalForm> Other = new Dictionary<string, OriginalForm>(StringComparer.Ordinal);

        private static readonly Regex BackTag = new Regex(@"<back\s([^>]*?)/?>", RegexOptions.CultureInvariant);

        // Decor definitions (<back id s x2 y2 .../>): size in cells (default 1) and drawing layer.
        public static List<RoomDefinition> ParseDecor(string allDataText, string source)
        {
            var result = new List<RoomDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match tag in BackTag.Matches(allDataText))
            {
                var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match a in Attribute.Matches(tag.Groups[1].Value)) attrs[a.Groups[1].Value] = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value;
                if (!attrs.TryGetValue("id", out string id) || id.Length == 0 || !seen.Add(id)) continue;   // empty id: a category header
                int width = Int(attrs, "x2", source, id), height = Int(attrs, "y2", source, id);
                result.Add(new RoomDefinition
                {
                    Id = "remains:decor:" + id, Kind = RoomDefinitionKind.Decor, Name = Get(attrs, "n"), Source = "AllData back id=" + id,
                    DecorWidth = width > 0 ? width : 1, DecorHeight = height > 0 ? height : 1, DecorLayer = Int(attrs, "s", source, id),
                    Art = Get(attrs, "tid") ?? id,
                    DecorFrame = Int(attrs, "fr", source, id), DecorFrameOn = Int(attrs, "lon", source, id), DecorFrameOff = Int(attrs, "loff", source, id),
                    Mirror = Int(attrs, "mirr", source, id), Blend = Get(attrs, "blend"),
                    Alpha = attrs.ContainsKey("alpha") ? Number(attrs, "alpha", source, id) : 1,
                    EraseFirst = attrs.ContainsKey("er"),
                });
            }
            return result;
        }

        private static readonly Regex ObjTag = new Regex(@"<obj\s([^>]*?)/?>", RegexOptions.CultureInvariant);

        // Original <obj> as room object definitions: visual and placement data only (type, footprint,
        // art); gameplay (locks, contents, durability) belongs to the object systems, not the room format.
        public static List<RoomDefinition> ParseObjects(string allDataText, string source)
        {
            var result = new List<RoomDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match tag in ObjTag.Matches(allDataText))
            {
                var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match a in Attribute.Matches(tag.Groups[1].Value)) attrs[a.Groups[1].Value] = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value;
                if (!attrs.TryGetValue("id", out string id) || id.Length == 0 || !seen.Add(id)) continue;   // empty id: a category header
                result.Add(new RoomDefinition
                {
                    Id = "remains:obj:" + id, Kind = RoomDefinitionKind.Object, Name = Get(attrs, "n"), Source = "AllData obj id=" + id,
                    Art = id, ObjectType = Get(attrs, "tip"),
                    ObjectWidth = Int(attrs, "wid", source, id), ObjectSize = Int(attrs, "size", source, id), Wall = Int(attrs, "wall", source, id),
                    Opacity = attrs.ContainsKey("opac") ? Number(attrs, "opac", source, id) : 0,
                });
            }
            return result;
        }

        public static OriginalForms Parse(string allDataText, string source)
        {
            var result = new OriginalForms();
            foreach (Match tag in MatTag.Matches(allDataText))
            {
                var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match a in Attribute.Matches(tag.Groups[1].Value))
                    attrs[a.Groups[1].Value] = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value;
                if (!attrs.TryGetValue("id", out string id) || !attrs.TryGetValue("ed", out string ed))
                    throw new FormatException($"{source}: <mat> без id или ed: {tag.Value}");
                var form = new OriginalForm
                {
                    Id = id,
                    Ed = Int(attrs, "ed", source, id),
                    Name = Get(attrs, "n"),
                    Back = Get(attrs, "back"),
                    Mirror = Get(attrs, "m"),
                    Vid = Int(attrs, "vid", source, id),
                    Diagon = Int(attrs, "diagon", source, id),
                    Stair = Int(attrs, "stair", source, id),
                    Shelf = attrs.ContainsKey("shelf"),
                    Rear = attrs.ContainsKey("rear"),
                    Solid = Int(attrs, "phis", source, id) > 0,
                    Hp = Int(attrs, "hp", source, id),
                    Threshold = Int(attrs, "thre", source, id),
                    Mat = Int(attrs, "mat", source, id),
                    Indestructible = Int(attrs, "indestruct", source, id) > 0,
                };
                var table = form.Ed == 1 ? result.Front : result.Other;
                if (table.ContainsKey(id)) throw new FormatException($"{source}: повтор формы ed={form.Ed} id={id}.");
                foreach (Match child in ChildTag.Matches(tag.Groups[3].Value))
                {
                    var c = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (Match a in Attribute.Matches(child.Groups[2].Value)) c[a.Groups[1].Value] = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value;
                    switch (child.Groups[1].Value)
                    {
                        case "main": form.MainTexture = Get(c, "tex"); form.MainMask = Get(c, "mask"); form.AltTexture = Get(c, "alt"); break;
                        case "border": form.BorderTexture = Get(c, "tex"); form.BorderMask = Get(c, "mask"); break;
                        case "floor": form.FloorTexture = Get(c, "tex"); form.FloorMask = Get(c, "mask"); break;
                        case "filter": form.Filter = Get(c, "f"); break;
                    }
                }
                table.Add(id, form);
            }
            if (result.Front.Count == 0) throw new FormatException(source + ": не найдено ни одной формы <mat ed='1'>.");
            return result;
        }

        private static string Get(Dictionary<string, string> attrs, string key) => attrs.TryGetValue(key, out string v) && v.Length > 0 ? v : null;

        private static int Int(Dictionary<string, string> attrs, string key, string source, string id)
        {
            if (!attrs.TryGetValue(key, out string v) || v.Length == 0) return 0;
            if (!int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
                throw new FormatException($"{source}: форма {id}: атрибут {key}=\"{v}\" не целое число.");
            return result;
        }

        private static double Number(Dictionary<string, string> attrs, string key, string source, string id)
        {
            string v = attrs[key];
            if (!double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
                throw new FormatException($"{source}: {id}: атрибут {key}=\"{v}\" не число.");
            return result;
        }
    }

    public sealed class DecodedCell
    {
        public string Solid;                       // ed=1 id or null
        public readonly List<string> Overlays = new List<string>(); // ed=3/4 ids, in application order
        public string Back;                        // final ed=2 id after the zad rule, or null
        public string ZadApplied;                  // zad that replaced back (for the report), or null
        public int Height;                         // zForm 0..3
        public bool Water;
        public char IgnoredFirst;                  // first char the original skips (no fForm), or '\0'
        public readonly List<char> Unknown = new List<char>();     // following chars without oForm
        public readonly List<string> OtherEd = new List<string>(); // oForms with ed outside 2/3/4
    }

    // Mirrors the application order of Tile.dec/inForm (spec §3.1); our own implementation.
    public static class OriginalCellDecoder
    {
        public static DecodedCell Decode(string token, OriginalForms forms)
        {
            if (string.IsNullOrEmpty(token)) throw new FormatException("Пустой токен клетки.");
            var cell = new DecodedCell();
            string zad = null;
            char first = token[0];
            if (first > 64 && first != '_')
            {
                if (forms.Front.TryGetValue(first.ToString(), out var front)) Apply(front, cell, ref zad);
                else cell.IgnoredFirst = first;
            }
            for (int i = 1; i < token.Length; i++)
            {
                char c = token[i];
                if (c == '*') cell.Water = true;
                else if (c == ',') cell.Height = 1;
                else if (c == ';') cell.Height = 2;
                else if (c == ':') cell.Height = 3;
                else if (forms.Other.TryGetValue(c.ToString(), out var form)) Apply(form, cell, ref zad);
                else cell.Unknown.Add(c);
            }
            if (cell.Height == 0 && zad != null) { cell.Back = zad; cell.ZadApplied = zad; }
            return cell;
        }

        private static void Apply(OriginalForm form, DecodedCell cell, ref string zad)
        {
            switch (form.Ed)
            {
                case 1: cell.Solid = form.Id; break;
                case 2: cell.Back = form.Id; return;  // inForm: tip 2 writes only back
                case 3: case 4: cell.Overlays.Add(form.Id); break;
                default: cell.OtherEd.Add(form.Id); break;
            }
            if (form.Back != null) zad = form.Back;
        }
    }
}
