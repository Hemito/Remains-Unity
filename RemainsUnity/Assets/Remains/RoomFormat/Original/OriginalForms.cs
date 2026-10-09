using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Remains.RoomFormat.Original
{
    // A tile form from the original AllData <mat> list (code-derived from fe/loc/Form.as).
    public sealed class OriginalForm
    {
        public string Id, Name, Back, Mirror;
        public int Ed, Vid, Diagon, Stair;
        public bool Shelf, Rear, Solid;
        // Form.as: a form without vid draws itself as the tile front.
        public string Front => Vid > 0 ? null : Id;
    }

    public sealed class OriginalForms
    {
        private static readonly Regex MatTag = new Regex(@"<mat\s([^>]*)>", RegexOptions.CultureInvariant);
        private static readonly Regex Attribute = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:'([^']*)'|""([^""]*)"")", RegexOptions.CultureInvariant);

        // fForms: ed=1 (first character); oForms: every other ed (following characters).
        public readonly Dictionary<string, OriginalForm> Front = new Dictionary<string, OriginalForm>(StringComparer.Ordinal);
        public readonly Dictionary<string, OriginalForm> Other = new Dictionary<string, OriginalForm>(StringComparer.Ordinal);

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
                };
                var table = form.Ed == 1 ? result.Front : result.Other;
                if (table.ContainsKey(id)) throw new FormatException($"{source}: повтор формы ed={form.Ed} id={id}.");
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
