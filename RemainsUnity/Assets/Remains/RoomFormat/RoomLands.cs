using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    // Land-level defaults for rooms of one template set (original GameData <land>): the
    // room's own options override them. Keyed by the template land slug (remains:<land>:...).
    public sealed class RoomLand
    {
        public string Id, Land;
        public readonly SortedDictionary<string, string> Options = new SortedDictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed class RoomLandCatalog
    {
        public const string FormatName = "remains-room-lands";
        public const int CurrentVersion = 1;
        public readonly List<RoomLand> Lands = new List<RoomLand>();

        public static string LandOf(string template)
        {
            var parts = template?.Split(':');
            if (parts == null || parts.Length != 3) throw new FormatException($"Шаблон \"{template}\": ожидалось remains:<земля>:<комната>.");
            return parts[1];
        }

        // Effective option: the room's own value, else the land default for its template set.
        // Several lands may share one template set; they must then agree on the option.
        public string Option(RoomFile room, string key, List<string> issues)
        {
            if (room.Options.TryGetValue(key, out string own)) return own;
            string land = LandOf(room.Template);
            string found = null;
            foreach (var entry in Lands)
            {
                if (entry.Land != land || !entry.Options.TryGetValue(key, out string value)) continue;
                if (found != null && found != value) issues?.Add($"{room.Template}: земли с одним набором комнат расходятся в {key}: {found} / {value} ({entry.Id}); взято первое.");
                else found = value;
            }
            return found;
        }

        public string Write()
        {
            var root = JsonValue.NewObject().Set("format", JsonValue.Of(FormatName)).Set("version", JsonValue.Of(CurrentVersion));
            var lands = JsonValue.NewArray();
            foreach (var land in Lands)
            {
                var options = JsonValue.NewObject();
                foreach (var o in land.Options) options.Set(o.Key, JsonValue.Of(o.Value));
                lands.Add(JsonValue.NewObject().Set("id", JsonValue.Of(land.Id)).Set("land", JsonValue.Of(land.Land)).Set("options", options));
            }
            root.Set("lands", lands);
            return JsonText.Write(root, 3, new[] { "lands" });
        }

        public static RoomLandCatalog Read(string text, string source)
        {
            var root = JsonText.Parse(text, source);
            if (root.Get("format")?.AsString(source) != FormatName || root.Get("version")?.AsLong(source) != CurrentVersion)
                throw new FormatException($"{source}: ожидался {FormatName} версии {CurrentVersion}.");
            var catalog = new RoomLandCatalog();
            foreach (var v in root.Get("lands").Items)
            {
                var land = new RoomLand { Id = v.Get("id").AsString(source + ".id"), Land = v.Get("land").AsString(source + ".land") };
                foreach (var o in v.Get("options").Fields) land.Options.Add(o.Key, o.Value.AsString(source + ".options." + o.Key));
                catalog.Lands.Add(land);
            }
            return catalog;
        }
    }
}
