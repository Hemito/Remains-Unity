using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    public enum RoomDefinitionKind { Solid, Backwall, Ladder, Platform, Stairs }

    // Properties of one palette ID (spec §5). Visual data (textures, draw order) is added
    // with the tile catalogue; this is what geometry and terrain damage need.
    public sealed class RoomDefinition
    {
        public string Id, Name, Source;
        public RoomDefinitionKind Kind;
        public string MovementSound;          // empty: room default (RoomLayout semantics)
        public int Hp, Threshold;              // terrain durability; 0 = not destructible by damage
        public bool Indestructible;
        public int Slope;                      // stairs: +1 rises to the right, -1 to the left
        public int Facing;                     // ladder: +1 right, -1 left
        public double LadderFraction, MovementOffset, ArtworkOffset;   // ladder placement inside the cell
        // Visual layers (TileManifest IDs / mask names). Draw order relative to actors: Rear.
        public string MainTexture, MainMask, AltTexture, BorderTexture, BorderMask, FloorTexture, FloorMask, Filter, FrontSprite;
        public bool Rear;
    }

    public sealed class RoomDefinitionCatalog
    {
        public const string FormatName = "remains-room-definitions";
        public const int CurrentVersion = 1;
        public readonly SortedDictionary<string, RoomDefinition> Items = new SortedDictionary<string, RoomDefinition>(StringComparer.Ordinal);

        public RoomDefinition Require(string id, string context)
        {
            if (id != null && Items.TryGetValue(id, out var definition)) return definition;
            throw new FormatException($"{context}: нет определения \"{id}\" в каталоге.");
        }

        public void Add(RoomDefinition definition)
        {
            if (Items.ContainsKey(definition.Id)) throw new InvalidOperationException("Повтор определения: " + definition.Id);
            Items.Add(definition.Id, definition);
        }

        public string Write()
        {
            var root = JsonValue.NewObject().Set("format", JsonValue.Of(FormatName)).Set("version", JsonValue.Of(CurrentVersion));
            var items = JsonValue.NewArray();
            foreach (var d in Items.Values)
            {
                var v = JsonValue.NewObject()
                    .Set("id", JsonValue.Of(d.Id)).Set("kind", JsonValue.Of(d.Kind.ToString().ToLowerInvariant()))
                    .Set("name", JsonValue.Of(d.Name)).Set("source", JsonValue.Of(d.Source));
                if (!string.IsNullOrEmpty(d.MovementSound)) v.Set("movementSound", JsonValue.Of(d.MovementSound));
                if (d.Hp != 0) v.Set("hp", JsonValue.Of(d.Hp));
                if (d.Threshold != 0) v.Set("threshold", JsonValue.Of(d.Threshold));
                if (d.Indestructible) v.Set("indestructible", JsonValue.Of(true));
                if (d.Slope != 0) v.Set("slope", JsonValue.Of(d.Slope));
                SetIf(v, "mainTexture", d.MainTexture); SetIf(v, "mainMask", d.MainMask); SetIf(v, "altTexture", d.AltTexture);
                SetIf(v, "borderTexture", d.BorderTexture); SetIf(v, "borderMask", d.BorderMask);
                SetIf(v, "floorTexture", d.FloorTexture); SetIf(v, "floorMask", d.FloorMask);
                SetIf(v, "filter", d.Filter); SetIf(v, "frontSprite", d.FrontSprite);
                if (d.Rear) v.Set("rear", JsonValue.Of(true));
                if (d.Kind == RoomDefinitionKind.Ladder)
                    v.Set("facing", JsonValue.Of(d.Facing)).Set("ladderFraction", JsonValue.Of(d.LadderFraction))
                     .Set("movementOffset", JsonValue.Of(d.MovementOffset)).Set("artworkOffset", JsonValue.Of(d.ArtworkOffset));
                items.Add(v);
            }
            root.Set("definitions", items);
            return JsonText.Write(root, 3, new[] { "definitions" });
        }

        private static void SetIf(JsonValue v, string key, string value) { if (!string.IsNullOrEmpty(value)) v.Set(key, JsonValue.Of(value)); }
        private static string Str(JsonValue v, string key, string ctx) => v.Get(key)?.AsString(ctx + "." + key);

        public static RoomDefinitionCatalog Read(string text, string source)
        {
            var root = JsonText.Parse(text, source);
            if (root.Get("format")?.AsString(source) != FormatName || root.Get("version")?.AsLong(source) != CurrentVersion)
                throw new FormatException($"{source}: ожидался {FormatName} версии {CurrentVersion}.");
            var catalog = new RoomDefinitionCatalog();
            foreach (var v in root.Get("definitions").Items)
            {
                string id = v.Get("id").AsString(source + ".id");
                string ctx = source + ":" + id;
                if (!Enum.TryParse(v.Get("kind").AsString(ctx + ".kind"), true, out RoomDefinitionKind kind))
                    throw new FormatException(ctx + ": неизвестный kind.");
                catalog.Add(new RoomDefinition
                {
                    Id = id, Kind = kind,
                    Name = v.Get("name")?.AsString(ctx + ".name"),
                    Source = v.Get("source")?.AsString(ctx + ".source"),
                    MovementSound = v.Get("movementSound")?.AsString(ctx + ".movementSound"),
                    Hp = (int)(v.Get("hp")?.AsLong(ctx + ".hp") ?? 0),
                    Threshold = (int)(v.Get("threshold")?.AsLong(ctx + ".threshold") ?? 0),
                    Indestructible = v.Get("indestructible")?.Bool ?? false,
                    Slope = (int)(v.Get("slope")?.AsLong(ctx + ".slope") ?? 0),
                    Facing = (int)(v.Get("facing")?.AsLong(ctx + ".facing") ?? 0),
                    LadderFraction = v.Get("ladderFraction")?.AsDouble(ctx + ".ladderFraction") ?? 0,
                    MovementOffset = v.Get("movementOffset")?.AsDouble(ctx + ".movementOffset") ?? 0,
                    ArtworkOffset = v.Get("artworkOffset")?.AsDouble(ctx + ".artworkOffset") ?? 0,
                    MainTexture = Str(v, "mainTexture", ctx), MainMask = Str(v, "mainMask", ctx), AltTexture = Str(v, "altTexture", ctx),
                    BorderTexture = Str(v, "borderTexture", ctx), BorderMask = Str(v, "borderMask", ctx),
                    FloorTexture = Str(v, "floorTexture", ctx), FloorMask = Str(v, "floorMask", ctx),
                    Filter = Str(v, "filter", ctx), FrontSprite = Str(v, "frontSprite", ctx),
                    Rear = v.Get("rear")?.Bool ?? false,
                });
            }
            return catalog;
        }
    }
}
