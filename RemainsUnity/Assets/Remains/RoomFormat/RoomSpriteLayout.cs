using System;
using System.Collections.Generic;
using System.Globalization;

namespace Remains.RoomFormat
{
    public enum RoomSpriteKind { Decor, Object }

    // One decor or object picture of a room as placed, in room metres (origin bottom-left, y up).
    public sealed class RoomPlacedSprite
    {
        public RoomSpriteKind Kind;
        public int Index;                      // in RoomFile.Decor / RoomFile.Objects
        public RoomDefinition Definition;
        public int Frame;
        public string ArtId, EraseArtId;       // TileManifest frame IDs; EraseArtId null = no background erase
        public double X, Y, ScaleX = 1, ScaleY = 1, Alpha = 1;
    }

    // Placement rules of room pictures (one implementation for previews, room prefabs and level
    // preparation), after the original BackObj / Location constructor:
    //  - decor: clip origin at the placement cell's top-left; frame = placement "fr", else the
    //    definition's fixed frame, else the lights-on/off frame by the room's "lon", else random;
    //    "mirr" 2 flips at random; placement "w"/"h" scale (w keeps a flip: the original's w replaced the
    //    flip sign — no plant room has both); "a" opacity;
    //  - mirrored room: decor with mirr 1 (or 2 when its random flip comes up) flips around the room's
    //    right edge, otherwise only its position mirrors; objects move to width - x - size;
    //  - objects: clip origin at the bottom centre of "size" cells, 1 source pixel above the cell bottom.
    // Random choices are seeded by template and placement index, so both variants of a room and every
    // player get the same frames. Decor without a picture (light zones) and objects without a vis clip
    // (drawn by their own systems) are counted in the given dictionaries, not placed.
    public static class RoomSpriteLayout
    {
        public const double SourcePixelsPerMeter = 50;

        public static List<RoomPlacedSprite> Place(RoomFile room, bool mirror, RoomDefinitionCatalog definitions, TileManifest manifest, double lightsOn,
            List<string> issues, SortedDictionary<string, int> lightZones, SortedDictionary<string, int> otherObjects)
        {
            var frames = FrameCounts(manifest);
            var result = new List<RoomPlacedSprite>();
            double cell = room.Cell, roomWidth = room.Width * cell;
            for (int i = 0; i < room.Decor.Count; i++)
            {
                var placement = room.Decor[i];
                var d = definitions.Require(placement.Type, $"{room.Template} декор {i}");
                if (!frames.TryGetValue("decor:" + d.Art, out int count)) { Count(lightZones, d.Art); continue; }
                var random = Seeded(room.Template, i);
                int frame = d.DecorFrame;
                if (frame == 0)
                {
                    if (lightsOn > 0 && d.DecorFrameOn > 0) frame = d.DecorFrameOn;
                    else if (lightsOn < 0 && d.DecorFrameOff > 0) frame = d.DecorFrameOff;
                    else frame = random.Next(count) + 1;
                }
                if (Param(placement.Params, "fr") is double fr) frame = (int)fr;
                if (Param(placement.Params, "lon") is double lon)
                {
                    if (lon > 1 && d.DecorFrameOn > 0) frame = d.DecorFrameOn;
                    if (lon < 1 && d.DecorFrameOff > 0) frame = d.DecorFrameOff;
                }
                if (frame < 1 || frame > count) { issues.Add($"{room.Template}: декор {d.Id} кадр {frame} вне 1..{count} — взят кадр 1."); frame = 1; }

                double x = placement.X * cell, y = (placement.Y + 1) * cell, flip = 1;
                double? w = Param(placement.Params, "w"), h = Param(placement.Params, "h");
                double width = (w ?? (d.DecorWidth > 0 ? d.DecorWidth : 1)) * cell;
                bool randomFlip = d.Mirror == 2 && random.NextDouble() < 0.5;
                if (mirror)
                {
                    if (d.Mirror == 1 || randomFlip) { x = roomWidth - x; flip = -1; }
                    else x = roomWidth - x - width;
                }
                else if (randomFlip) { x += width; flip = -1; }
                string erase = TileManifest.DecorEraseFrameId(d.Art, frame);
                result.Add(new RoomPlacedSprite
                {
                    Kind = RoomSpriteKind.Decor, Index = i, Definition = d, Frame = frame,
                    ArtId = TileManifest.DecorFrameId(d.Art, frame), EraseArtId = manifest.Art.ContainsKey(erase) ? erase : null,
                    X = x, Y = y, ScaleX = flip * (w ?? 1), ScaleY = h ?? 1, Alpha = Param(placement.Params, "a") ?? d.Alpha,
                });
            }
            for (int i = 0; i < room.Objects.Count; i++)
            {
                var placement = room.Objects[i];
                var d = definitions.Require(placement.Type, $"{room.Template} объект {placement.Code}");
                if (!frames.ContainsKey("object:" + d.Art)) { Count(otherObjects, d.ObjectType ?? "?"); continue; }
                int size = Math.Max(1, d.ObjectSize);
                int cellX = mirror ? room.Width - placement.X - size : placement.X;
                result.Add(new RoomPlacedSprite
                {
                    Kind = RoomSpriteKind.Object, Index = i, Definition = d, Frame = 1, ArtId = TileManifest.ObjectFrameId(d.Art, 1),
                    X = (cellX + size / 2.0) * cell, Y = placement.Y * cell + 1 / SourcePixelsPerMeter,
                });
            }
            return result;
        }

        // Object cell after mirroring (Location constructor: nx = spaceX - nx - size).
        public static int ObjectCellX(RoomFile room, RoomObject placement, RoomDefinition definition, bool mirror) =>
            mirror ? room.Width - placement.X - Math.Max(1, definition.ObjectSize) : placement.X;

        // Frame count per art clip: "decor:<art>" / "object:<art>" -> frames 1..N present in the manifest.
        private static Dictionary<string, int> FrameCounts(TileManifest manifest)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string id in manifest.Art.Keys)
            {
                if (!id.StartsWith("decor:", StringComparison.Ordinal) && !id.StartsWith("object:", StringComparison.Ordinal)) continue;
                int split = id.LastIndexOf(':');
                string key = id.Substring(0, split);
                int frame = int.Parse(id.Substring(split + 1), CultureInfo.InvariantCulture);
                counts[key] = Math.Max(frame, counts.TryGetValue(key, out int n) ? n : 0);
            }
            return counts;
        }

        private static void Count(SortedDictionary<string, int> counts, string key) { if (counts != null) counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1; }

        private static Random Seeded(string template, int index)
        {
            uint hash = 2166136261;
            foreach (char c in template + "#" + index.ToString(CultureInfo.InvariantCulture)) { hash ^= c; hash *= 16777619; }
            return new Random((int)hash);
        }

        private static double? Param(IReadOnlyDictionary<string, string> parameters, string key)
            => parameters != null && parameters.TryGetValue(key, out string v) && v.Length > 0 ? double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture) : (double?)null;
    }
}
