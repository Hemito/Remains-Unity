using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    public enum RoomDefinitionKind { Solid, Backwall, Ladder, Platform, Stairs, Decor, Object }

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
        public string MirrorForm;              // ladder/stairs: definition drawn in a mirrored room (original form attribute m)
        public string BackForm;                // solid: back wall it brings when it becomes a room border (Location.mainFrame)
        // Visual layers (TileManifest IDs / mask names). Draw order relative to actors: Rear.
        public string MainTexture, MainMask, AltTexture, BorderTexture, BorderMask, FloorTexture, FloorMask, Filter, FrontSprite;
        public bool Rear;
        // Decor: footprint in cells from the placement cell (x right, rows down), drawing layer.
        public int DecorWidth, DecorHeight, DecorLayer;
        // Decor/object art: clip name in the art manifest (decor:<Art>:<frame>, object:<Art>:<frame>).
        public string Art;
        // Decor frames (original BackObj): fixed frame, frame with room lights on / off; 0 = not set
        // (then a frame is chosen per placement). Blend mode ("normal" when null), opacity, mirroring
        // (0 mirrors position only, 1 flips with a mirrored room, 2 random flip), drawn before others.
        public int DecorFrame, DecorFrameOn, DecorFrameOff, Mirror;
        public string Blend;
        public double Alpha = 1;
        public bool EraseFirst;
        // Objects (original <obj>): type (box, door, ...), footprint width in cells (placement: bottom
        // centre of it), size class, opacity of a door to light (0 = not set), wall mounting.
        public string ObjectType;
        public int ObjectWidth, ObjectSize, Wall;
        public double Opacity;
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
                SetIf(v, "mirrorForm", d.MirrorForm);
                SetIf(v, "backForm", d.BackForm);
                SetIf(v, "mainTexture", d.MainTexture); SetIf(v, "mainMask", d.MainMask); SetIf(v, "altTexture", d.AltTexture);
                SetIf(v, "borderTexture", d.BorderTexture); SetIf(v, "borderMask", d.BorderMask);
                SetIf(v, "floorTexture", d.FloorTexture); SetIf(v, "floorMask", d.FloorMask);
                SetIf(v, "filter", d.Filter); SetIf(v, "frontSprite", d.FrontSprite);
                if (d.Rear) v.Set("rear", JsonValue.Of(true));
                if (d.Kind == RoomDefinitionKind.Decor)
                    v.Set("decorWidth", JsonValue.Of(d.DecorWidth)).Set("decorHeight", JsonValue.Of(d.DecorHeight)).Set("decorLayer", JsonValue.Of(d.DecorLayer));
                SetIf(v, "art", d.Art);
                if (d.Kind == RoomDefinitionKind.Decor)
                {
                    if (d.DecorFrame != 0) v.Set("frame", JsonValue.Of(d.DecorFrame));
                    if (d.DecorFrameOn != 0) v.Set("frameOn", JsonValue.Of(d.DecorFrameOn));
                    if (d.DecorFrameOff != 0) v.Set("frameOff", JsonValue.Of(d.DecorFrameOff));
                    if (d.Mirror != 0) v.Set("mirror", JsonValue.Of(d.Mirror));
                    SetIf(v, "blend", d.Blend);
                    if (d.Alpha != 1) v.Set("alpha", JsonValue.Of(d.Alpha));
                    if (d.EraseFirst) v.Set("eraseFirst", JsonValue.Of(true));
                }
                if (d.Kind == RoomDefinitionKind.Object)
                {
                    v.Set("objectType", JsonValue.Of(d.ObjectType)).Set("objectWidth", JsonValue.Of(d.ObjectWidth)).Set("objectSize", JsonValue.Of(d.ObjectSize));
                    if (d.Wall != 0) v.Set("wall", JsonValue.Of(d.Wall));
                    if (d.Opacity != 0) v.Set("opacity", JsonValue.Of(d.Opacity));
                }
                if (d.Kind == RoomDefinitionKind.Ladder)
                    v.Set("facing", JsonValue.Of(d.Facing)).Set("ladderFraction", JsonValue.Of(d.LadderFraction))
                     .Set("movementOffset", JsonValue.Of(d.MovementOffset)).Set("artworkOffset", JsonValue.Of(d.ArtworkOffset));
                items.Add(v);
            }
            root.Set("definitions", items);
            return JsonText.Write(root, 3, new[] { "definitions" });
        }

        private static void SetIf(JsonValue v, string key, string value) { if (!string.IsNullOrEmpty(value)) v.Set(key, JsonValue.Of(value)); }
        // Absent and JSON null both mean "not set" (e.g. original decor without a name).
        private static string Str(JsonValue v, string key, string ctx)
        {
            var value = v.Get(key);
            return value == null || value.Kind == JsonKind.Null ? null : value.AsString(ctx + "." + key);
        }

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
                    Name = Str(v, "name", ctx),
                    Source = Str(v, "source", ctx),
                    MovementSound = Str(v, "movementSound", ctx),
                    Hp = (int)(v.Get("hp")?.AsLong(ctx + ".hp") ?? 0),
                    Threshold = (int)(v.Get("threshold")?.AsLong(ctx + ".threshold") ?? 0),
                    Indestructible = v.Get("indestructible")?.Bool ?? false,
                    Slope = (int)(v.Get("slope")?.AsLong(ctx + ".slope") ?? 0),
                    Facing = (int)(v.Get("facing")?.AsLong(ctx + ".facing") ?? 0),
                    MirrorForm = Str(v, "mirrorForm", ctx),
                    BackForm = Str(v, "backForm", ctx),
                    LadderFraction = v.Get("ladderFraction")?.AsDouble(ctx + ".ladderFraction") ?? 0,
                    MovementOffset = v.Get("movementOffset")?.AsDouble(ctx + ".movementOffset") ?? 0,
                    ArtworkOffset = v.Get("artworkOffset")?.AsDouble(ctx + ".artworkOffset") ?? 0,
                    MainTexture = Str(v, "mainTexture", ctx), MainMask = Str(v, "mainMask", ctx), AltTexture = Str(v, "altTexture", ctx),
                    BorderTexture = Str(v, "borderTexture", ctx), BorderMask = Str(v, "borderMask", ctx),
                    FloorTexture = Str(v, "floorTexture", ctx), FloorMask = Str(v, "floorMask", ctx),
                    Filter = Str(v, "filter", ctx), FrontSprite = Str(v, "frontSprite", ctx),
                    Rear = v.Get("rear")?.Bool ?? false,
                    DecorWidth = (int)(v.Get("decorWidth")?.AsLong(ctx + ".decorWidth") ?? 0),
                    DecorHeight = (int)(v.Get("decorHeight")?.AsLong(ctx + ".decorHeight") ?? 0),
                    DecorLayer = (int)(v.Get("decorLayer")?.AsLong(ctx + ".decorLayer") ?? 0),
                    Art = Str(v, "art", ctx),
                    DecorFrame = (int)(v.Get("frame")?.AsLong(ctx + ".frame") ?? 0),
                    DecorFrameOn = (int)(v.Get("frameOn")?.AsLong(ctx + ".frameOn") ?? 0),
                    DecorFrameOff = (int)(v.Get("frameOff")?.AsLong(ctx + ".frameOff") ?? 0),
                    Mirror = (int)(v.Get("mirror")?.AsLong(ctx + ".mirror") ?? 0),
                    Blend = Str(v, "blend", ctx),
                    Alpha = v.Get("alpha")?.AsDouble(ctx + ".alpha") ?? 1,
                    EraseFirst = v.Get("eraseFirst")?.Bool ?? false,
                    ObjectType = Str(v, "objectType", ctx),
                    ObjectWidth = (int)(v.Get("objectWidth")?.AsLong(ctx + ".objectWidth") ?? 0),
                    ObjectSize = (int)(v.Get("objectSize")?.AsLong(ctx + ".objectSize") ?? 0),
                    Wall = (int)(v.Get("wall")?.AsLong(ctx + ".wall") ?? 0),
                    Opacity = v.Get("opacity")?.AsDouble(ctx + ".opacity") ?? 0,
                });
            }
            return catalog;
        }
    }
}
