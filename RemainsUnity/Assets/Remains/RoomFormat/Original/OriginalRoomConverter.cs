using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Remains.RoomFormat.Original
{
    // Explicit mapping of an original structural form to a semantic layer, ID and palette key.
    public sealed class OriginalStructuralForm
    {
        public string Id, Layer;
        public char Key;
    }

    public sealed class OriginalFormMap
    {
        public string SolidPrefix, BackPrefix;
        public readonly Dictionary<string, OriginalStructuralForm> Structural = new Dictionary<string, OriginalStructuralForm>(StringComparer.Ordinal);

        public static OriginalFormMap Parse(string json, string source)
        {
            var root = JsonText.Parse(json, source);
            if (root.Get("format")?.AsString(source) != "remains-original-forms" || root.Get("version")?.AsLong(source) != 1)
                throw new FormatException(source + ": ожидался формат remains-original-forms версии 1.");
            var map = new OriginalFormMap
            {
                SolidPrefix = root.Get("solidPrefix").AsString(source + ".solidPrefix"),
                BackPrefix = root.Get("backPrefix").AsString(source + ".backPrefix"),
            };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var structural = root.Get("structural") ?? throw new FormatException(source + ": нет раздела structural.");
            foreach (var entry in structural.Fields)
            {
                string ctx = source + ".structural." + entry.Key;
                var form = new OriginalStructuralForm
                {
                    Id = entry.Value.Get("id").AsString(ctx + ".id"),
                    Layer = entry.Value.Get("layer").AsString(ctx + ".layer"),
                };
                string key = entry.Value.Get("key").AsString(ctx + ".key");
                if (Array.IndexOf(RoomLayers.Structural, form.Layer) < 0)
                    throw new FormatException($"{ctx}: слой \"{form.Layer}\" не структурный ({string.Join(", ", RoomLayers.Structural)}).");
                if (key.Length != 1 || key[0] == RoomFile.Empty || !keys.Add(form.Layer + key))
                    throw new FormatException($"{ctx}: ключ \"{key}\" не одиночный, зарезервирован или повторён в слое {form.Layer}.");
                form.Key = key[0];
                map.Structural.Add(entry.Key, form);
            }
            return map;
        }
    }

    public sealed class ConversionReport
    {
        public int Files, Rooms, RoomsFailed, Cells;
        public readonly List<string> Failures = new List<string>();
        public readonly SortedDictionary<string, int> Tokens = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> UnknownChars = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, string> UnknownCharExample = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> IgnoredFirst = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> OtherEd = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> UnmappedOver = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly int[] OverlayCount = new int[4];          // cells with 0,1,2,3+ overlays
        public readonly List<string> OverlayExamples = new List<string>();
        public int ZadApplied, ZadUnknown, PartialHeight, PartialWithoutSolid, Water, MaterialOrderExceptions;
        public readonly SortedDictionary<string, int> SameLayerTwice = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> LayerCombinations = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> SolidUse = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> BackUse = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> OverUse = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> DecorTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> ObjectTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> ObjectParams = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> ObjectChildren = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> RoomChildren = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> RoomAttributes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> OptionKeys = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> FileChildren = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> ConnectorValues = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int ObjectsTotal, ObjectsWithoutUid, NonIntegerCoordinates, OutOfRoomCoordinates, ConnectorRows, ConnectorBadLength, UnresolvedLinks;
        public readonly List<string> ValidationErrors = new List<string>();

        internal static void Count(SortedDictionary<string, int> map, string key, int amount = 1)
        {
            map.TryGetValue(key, out int value);
            map[key] = value + amount;
        }
    }

    public sealed class OriginalRoomConverter
    {
        private readonly OriginalForms forms;
        private readonly OriginalFormMap map;
        public readonly ConversionReport Report = new ConversionReport();
        private readonly HashSet<string> templates = new HashSet<string>(StringComparer.Ordinal);

        public OriginalRoomConverter(OriginalForms forms, OriginalFormMap map) { this.forms = forms; this.map = map; }

        // Converts one Rooms/*.xml file. Rooms that cannot be converted are reported, not guessed.
        public List<RoomFile> ConvertFile(string fileName, string xmlText)
        {
            Report.Files++;
            var result = new List<RoomFile>();
            XDocument document;
            try { document = XDocument.Parse(xmlText); }
            catch (Exception e) { Report.Failures.Add($"{fileName}: XML не разобран: {e.Message}"); return result; }
            string land = LandOf(fileName);
            foreach (var child in document.Root.Elements())
            {
                if (child.Name.LocalName != "room") { ConversionReport.Count(Report.FileChildren, child.Name.LocalName); continue; }
                Report.Rooms++;
                try
                {
                    var room = ConvertRoom(fileName, land, child);
                    var errors = RoomValidator.Validate(room, room.Template);
                    Report.ValidationErrors.AddRange(errors);
                    Report.UnresolvedLinks += RoomValidator.UnresolvedLinks(room).Count;
                    result.Add(room);
                }
                catch (FormatException e) { Report.RoomsFailed++; Report.Failures.Add($"{fileName}/{child.Attribute("name")?.Value}: {e.Message}"); }
            }
            return result;
        }

        public static string LandOf(string fileName)
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            return stem.StartsWith("rooms_", StringComparison.Ordinal) ? stem.Substring(6) : stem;
        }

        private RoomFile ConvertRoom(string fileName, string land, XElement element)
        {
            string name = element.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) throw new FormatException("комната без name");
            var room = new RoomFile { Template = $"remains:{land}:{name}" };
            if (!templates.Add(room.Template)) throw new FormatException($"повтор шаблона {room.Template}");
            room.Source = new RoomSource
            {
                File = fileName, Room = name,
                MapX = IntAttr(element, "x", 0), MapY = IntAttr(element, "y", 0),
                Sha256 = Sha256(element.ToString(SaveOptions.DisableFormatting)),
            };
            var extra = JsonValue.NewObject();
            foreach (var attr in element.Attributes())
                if (attr.Name.LocalName != "name" && attr.Name.LocalName != "x" && attr.Name.LocalName != "y")
                {
                    ConversionReport.Count(Report.RoomAttributes, attr.Name.LocalName);
                    extra.Set("@" + attr.Name.LocalName, JsonValue.Of(attr.Value));
                }

            var rows = element.Elements("a").Select(a => a.Value.Split('.')).ToList();
            if (rows.Count == 0) throw new FormatException("нет строк <a>");
            room.Height = rows.Count;
            room.Width = rows[0].Length;
            if (rows.Any(r => r.Length != room.Width)) throw new FormatException("строки <a> разной длины");
            var lossyCells = ConvertCells(room, rows);
            if (lossyCells.Items.Count > 0) extra.Set("cells", lossyCells);

            var unknownChildren = JsonValue.NewArray();
            foreach (var child in element.Elements())
            {
                string tag = child.Name.LocalName;
                ConversionReport.Count(Report.RoomChildren, tag);
                switch (tag)
                {
                    case "a": break;
                    case "back": room.Decor.Add(ConvertDecor(room, child)); break;
                    case "obj": room.Objects.Add(ConvertObject(room, child)); break;
                    case "doors": room.Connectors = ConvertConnectors(child.Value); break;
                    case "options":
                        foreach (var attr in child.Attributes())
                        {
                            ConversionReport.Count(Report.OptionKeys, attr.Name.LocalName);
                            if (room.Options.ContainsKey(attr.Name.LocalName)) throw new FormatException("повтор опции " + attr.Name.LocalName);
                            room.Options.Add(attr.Name.LocalName, attr.Value);
                        }
                        if (child.HasElements) unknownChildren.Add(JsonValue.Of(child.ToString(SaveOptions.DisableFormatting)));
                        break;
                    default: unknownChildren.Add(JsonValue.Of(child.ToString(SaveOptions.DisableFormatting))); break;
                }
            }
            if (unknownChildren.Items.Count > 0) extra.Set("elements", unknownChildren);
            room.Extra = extra.Fields.Count > 0 ? extra : null;
            return room;
        }

        // Returns cells whose source token was not fully representable (kept verbatim).
        private JsonValue ConvertCells(RoomFile room, List<string[]> rows)
        {
            var solid = RoomLayers.NewGrid(room.Width, room.Height);
            var back = RoomLayers.NewGrid(room.Width, room.Height);
            var structural = new Dictionary<string, char[][]>(StringComparer.Ordinal);
            foreach (string layer in RoomLayers.Structural) structural.Add(layer, RoomLayers.NewGrid(room.Width, room.Height));
            var height = RoomLayers.NewGrid(room.Width, room.Height);
            var water = RoomLayers.NewGrid(room.Width, room.Height);
            var cellExtra = JsonValue.NewArray();

            for (int r = 0; r < rows.Count; r++)
                for (int x = 0; x < room.Width; x++)
                {
                    string token = rows[r][x];
                    Report.Cells++;
                    ConversionReport.Count(Report.Tokens, token);
                    var cell = OriginalCellDecoder.Decode(token, forms);
                    bool lossy = false;

                    if (cell.Solid != null)
                    {
                        solid[r][x] = cell.Solid[0];
                        room.Palette(RoomLayers.Solid)[cell.Solid[0]] = map.SolidPrefix + cell.Solid;
                        ConversionReport.Count(Report.SolidUse, cell.Solid);
                    }
                    if (cell.Back != null)
                    {
                        if (forms.Other.TryGetValue(cell.Back, out var backForm) && backForm.Ed == 2 && cell.Back.Length == 1)
                        {
                            back[r][x] = cell.Back[0];
                            room.Palette(RoomLayers.Back)[cell.Back[0]] = map.BackPrefix + cell.Back;
                            ConversionReport.Count(Report.BackUse, cell.Back);
                        }
                        else { Report.ZadUnknown++; lossy = true; }
                    }
                    if (cell.ZadApplied != null) Report.ZadApplied++;
                    if (cell.Height > 0)
                    {
                        height[r][x] = (char)('0' + cell.Height);
                        Report.PartialHeight++;
                        if (cell.Solid == null) Report.PartialWithoutSolid++;
                    }
                    if (cell.Water) { water[r][x] = '~'; Report.Water++; }

                    Report.OverlayCount[Math.Min(cell.Overlays.Count, 3)]++;
                    if (cell.Overlays.Count >= 2 && Report.OverlayExamples.Count < 12) Report.OverlayExamples.Add(token);
                    string previousLayer = null;
                    foreach (string id in cell.Overlays)
                    {
                        if (!map.Structural.TryGetValue(id, out var target)) { ConversionReport.Count(Report.UnmappedOver, id); lossy = true; continue; }
                        var grid = structural[target.Layer];
                        if (grid[r][x] != RoomFile.Empty) { ConversionReport.Count(Report.SameLayerTwice, target.Layer); lossy = true; continue; }
                        // Original: the last applied form with a material decides the cell material.
                        // Our rule: platform/stairs material over ladder; a ladder applied after them would break it.
                        if (target.Layer == RoomLayers.Ladder && previousLayer != null && previousLayer != RoomLayers.Ladder) { Report.MaterialOrderExceptions++; lossy = true; }
                        grid[r][x] = target.Key;
                        room.Palette(target.Layer)[target.Key] = target.Id;
                        ConversionReport.Count(Report.OverUse, id);
                        if (previousLayer != null) ConversionReport.Count(Report.LayerCombinations, previousLayer + "+" + target.Layer);
                        previousLayer = target.Layer;
                    }

                    if (cell.IgnoredFirst != '\0') { ConversionReport.Count(Report.IgnoredFirst, cell.IgnoredFirst.ToString()); lossy = true; }
                    foreach (char c in cell.Unknown)
                    {
                        ConversionReport.Count(Report.UnknownChars, c.ToString());
                        if (!Report.UnknownCharExample.ContainsKey(c.ToString())) Report.UnknownCharExample[c.ToString()] = $"{room.Template} x={x} row={r} token={token}";
                        lossy = true;
                    }
                    foreach (string id in cell.OtherEd) { ConversionReport.Count(Report.OtherEd, id); lossy = true; }
                    if (lossy)
                        cellExtra.Add(JsonValue.NewObject().Set("x", JsonValue.Of(x)).Set("y", JsonValue.Of(room.Height - 1 - r)).Set("token", JsonValue.Of(token)));
                }

            Store(room, RoomLayers.Solid, solid);
            Store(room, RoomLayers.Back, back);
            foreach (var layer in structural) Store(room, layer.Key, layer.Value);
            Store(room, RoomLayers.Height, height);
            Store(room, RoomLayers.Water, water);
            return cellExtra;
        }

        private static void Store(RoomFile room, string layer, char[][] grid)
        {
            bool any = false;
            foreach (var row in grid) foreach (char c in row) if (c != RoomFile.Empty) { any = true; break; }
            if (!any) return;
            room.Layers[layer] = grid.Select(row => new string(row)).ToArray();
        }

        private RoomDecor ConvertDecor(RoomFile room, XElement element)
        {
            var decor = new RoomDecor { Type = "remains:decor:" + Required(element, "id") };
            ConversionReport.Count(Report.DecorTypes, Required(element, "id"));
            decor.X = Coordinate(element, "x", room, false);
            decor.Y = Coordinate(element, "y", room, true);
            foreach (var attr in element.Attributes())
                if (attr.Name.LocalName != "id" && attr.Name.LocalName != "x" && attr.Name.LocalName != "y") decor.Params.Add(attr.Name.LocalName, attr.Value);
            if (element.HasElements) decor.Params.Add("@children", string.Concat(element.Elements().Select(e => e.ToString(SaveOptions.DisableFormatting))));
            return decor;
        }

        private RoomObject ConvertObject(RoomFile room, XElement element)
        {
            Report.ObjectsTotal++;
            string type = Required(element, "id");
            ConversionReport.Count(Report.ObjectTypes, type);
            var obj = new RoomObject
            {
                Type = "remains:obj:" + type,
                Id = element.Attribute("uid")?.Value,
                Code = element.Attribute("code")?.Value,
                X = Coordinate(element, "x", room, false),
                Y = Coordinate(element, "y", room, true),
            };
            if (obj.Id == null) Report.ObjectsWithoutUid++;
            foreach (var attr in element.Attributes())
            {
                string key = attr.Name.LocalName;
                if (key == "id" || key == "x" || key == "y" || key == "uid" || key == "code") continue;
                ConversionReport.Count(Report.ObjectParams, key);
                obj.Params.Add(key, attr.Value);
            }
            var extra = JsonValue.NewArray();
            foreach (var child in element.Elements())
            {
                string tag = child.Name.LocalName;
                ConversionReport.Count(Report.ObjectChildren, tag);
                if (tag == "item") obj.Items.Add(Attributes(child));
                else if (tag == "scr" && !child.HasElements) obj.Links.Add(Attributes(child));
                else extra.Add(JsonValue.Of(child.ToString(SaveOptions.DisableFormatting)));
            }
            if (extra.Items.Count > 0) obj.Extra = extra;
            return obj;
        }

        private int[] ConvertConnectors(string text)
        {
            Report.ConnectorRows++;
            var parts = text.Trim().Split('.');
            if (parts.Length != 22) Report.ConnectorBadLength++;
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
                    throw new FormatException("разъём не число: " + parts[i]);
                ConversionReport.Count(Report.ConnectorValues, parts[i]);
            }
            return result;
        }

        // Original rows count from the top; ours from the bottom (spec §4.1).
        // Object anchors are not yet formalised: only the cell row is converted.
        private int Coordinate(XElement element, string name, RoomFile room, bool flipY)
        {
            string raw = Required(element, name);
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                Report.NonIntegerCoordinates++;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw new FormatException($"координата {name}=\"{raw}\" не число");
                value = (int)Math.Floor(d);
            }
            int result = flipY ? room.Height - 1 - value : value;
            int limit = flipY ? room.Height : room.Width;
            if (result < 0 || result >= limit) Report.OutOfRoomCoordinates++;
            return result;
        }

        private static SortedDictionary<string, string> Attributes(XElement element)
        {
            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var attr in element.Attributes()) map[attr.Name.LocalName] = attr.Value;
            return map;
        }

        private static string Required(XElement element, string name)
            => element.Attribute(name)?.Value ?? throw new FormatException($"<{element.Name.LocalName}> без атрибута {name}");

        private static int IntAttr(XElement element, string name, int fallback)
        {
            var attr = element.Attribute(name);
            if (attr == null) return fallback;
            if (!int.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) throw new FormatException($"атрибут {name}=\"{attr.Value}\" не целое");
            return value;
        }

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var b = new StringBuilder(hash.Length * 2);
                foreach (byte x in hash) b.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return b.ToString();
            }
        }
    }
}
