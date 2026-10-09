using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Remains.RoomFormat.Original
{
    // Reads land defaults from the original GameData (<land file='rooms_x'><options .../>).
    public static class OriginalLands
    {
        private static readonly Regex Land = new Regex(@"<land\s([^>]*)>(.*?)</land>", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex Options = new Regex(@"<options\s([^>]*?)/?>", RegexOptions.CultureInvariant);
        private static readonly Regex Attribute = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:'([^']*)'|""([^""]*)"")", RegexOptions.CultureInvariant);

        public static RoomLandCatalog Parse(string gameDataText, string source)
        {
            var catalog = new RoomLandCatalog();
            foreach (Match land in Land.Matches(gameDataText))
            {
                var attrs = Attributes(land.Groups[1].Value);
                if (!attrs.TryGetValue("id", out string id)) throw new FormatException($"{source}: <land> без id.");
                if (!attrs.TryGetValue("file", out string file)) continue;   // lands without a room file have no templates
                var entry = new RoomLand { Id = id, Land = OriginalRoomConverter.Slug(OriginalRoomConverter.LandOf(file + ".xml")) };
                var options = Options.Match(land.Groups[2].Value);
                if (options.Success) foreach (var o in Attributes(options.Groups[1].Value)) entry.Options[o.Key] = o.Value;
                catalog.Lands.Add(entry);
            }
            if (catalog.Lands.Count == 0) throw new FormatException(source + ": не найдено ни одной земли с file.");
            return catalog;
        }

        private static Dictionary<string, string> Attributes(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match a in Attribute.Matches(text)) result[a.Groups[1].Value] = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value;
            return result;
        }
    }
}
