using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    public sealed class RoomLamp
    {
        public double Radius, Intensity, OffsetY;
        public double[] Color;
    }

    // Lighting parameters per room (Rooms/Original/room-lighting.json): ambient level from the
    // room/land darkness, tint from the room/land color, lamps keyed by decor definition.
    public sealed class RoomLightingTable
    {
        public const string FormatName = "remains-room-lighting";
        public const int CurrentVersion = 1;
        public double AmbientBase, DarknessScale, AmbientMin, AmbientMax;
        public readonly SortedDictionary<string, double[]> Tints = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, RoomLamp> Lamps = new SortedDictionary<string, RoomLamp>(StringComparer.Ordinal);

        public double Ambient(double darkness) => Math.Max(AmbientMin, Math.Min(AmbientMax, AmbientBase - darkness * DarknessScale));

        public static RoomLightingTable Read(string text, string source)
        {
            var root = JsonText.Parse(text, source);
            if (root.Get("format")?.AsString(source) != FormatName || root.Get("version")?.AsLong(source) != CurrentVersion)
                throw new FormatException($"{source}: ожидался {FormatName} версии {CurrentVersion}.");
            var ambient = root.Get("ambient");
            var table = new RoomLightingTable
            {
                AmbientBase = ambient.Get("base").AsDouble(source + ".ambient.base"),
                DarknessScale = ambient.Get("darknessScale").AsDouble(source + ".ambient.darknessScale"),
                AmbientMin = ambient.Get("min").AsDouble(source + ".ambient.min"),
                AmbientMax = ambient.Get("max").AsDouble(source + ".ambient.max"),
            };
            foreach (var t in root.Get("tints").Fields) table.Tints.Add(t.Key, Rgb(t.Value, source + ".tints." + t.Key));
            foreach (var l in root.Get("lamps").Fields)
            {
                string ctx = source + ".lamps." + l.Key;
                table.Lamps.Add(l.Key, new RoomLamp
                {
                    Radius = l.Value.Get("radius").AsDouble(ctx + ".radius"),
                    Intensity = l.Value.Get("intensity").AsDouble(ctx + ".intensity"),
                    OffsetY = l.Value.Get("offsetY")?.AsDouble(ctx + ".offsetY") ?? 0,
                    Color = Rgb(l.Value.Get("color"), ctx + ".color"),
                });
            }
            return table;
        }

        private static double[] Rgb(JsonValue v, string ctx)
        {
            if (v == null || v.Kind != JsonKind.Array || v.Items.Count != 3) throw new FormatException(ctx + ": ожидался массив [r, g, b].");
            return new[] { v.Items[0].AsDouble(ctx), v.Items[1].AsDouble(ctx), v.Items[2].AsDouble(ctx) };
        }
    }
}
