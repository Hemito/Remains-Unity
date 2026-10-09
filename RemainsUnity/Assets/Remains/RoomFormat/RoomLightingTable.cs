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
    // room/land darkness, tint from the room/land color, lamps keyed by decor definition, shadow
    // shape (lit wall edge) and softness.
    public sealed class RoomLightingTable
    {
        public const string FormatName = "remains-room-lighting";
        public const int CurrentVersion = 1;
        public double AmbientBase, DarknessScale, AmbientMin, AmbientMax;
        public double ShadowEdge, ShadowSoftness;
        public int RadianceResolution;              // flatland light transport grid, texels per metre
        public double RadiancePower, RadianceLampSize, RadianceAlbedo;   // lamp fluence 1 m away = power × intensity; disk = lampSize × half decor width   // lit wall edge width (m) and lamp shadow softness 0..1
        public readonly SortedDictionary<string, double[]> Tints = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, RoomLamp> Lamps = new SortedDictionary<string, RoomLamp>(StringComparer.Ordinal);
        // Light absorbed by free-standing objects by size class, and per-definition overrides.
        public readonly SortedDictionary<int, double> AbsorptionBySize = new SortedDictionary<int, double>();
        public readonly SortedDictionary<string, double> AbsorptionByObject = new SortedDictionary<string, double>(StringComparer.Ordinal);

        // Share of light an object stops (0..1). Original placement depth: wall = 1 hangs on the back
        // wall (not in the light's plane) → 0; wall = 2 stands across the corridor (doors, grates,
        // windows) → its original "opac", else 1; free-standing → by size class (table), unless overridden.
        public double ObjectAbsorption(RoomDefinition definition)
        {
            if (AbsorptionByObject.TryGetValue(definition.Id, out double value)) return value;
            if (definition.Wall == 1) return 0;
            if (definition.Wall >= 2) return definition.Opacity > 0 ? definition.Opacity : 1;
            if (AbsorptionBySize.TryGetValue(Math.Max(1, definition.ObjectSize), out value)) return value;
            throw new InvalidOperationException($"Таблица света: нет поглощения для размера {definition.ObjectSize} ({definition.Id}).");
        }

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
            var shadows = root.Get("shadows") ?? throw new FormatException(source + ": нет раздела shadows.");
            table.ShadowEdge = shadows.Get("edge").AsDouble(source + ".shadows.edge");
            table.ShadowSoftness = shadows.Get("softness").AsDouble(source + ".shadows.softness");
            var radiance = root.Get("radiance") ?? throw new FormatException(source + ": нет раздела radiance.");
            table.RadianceResolution = (int)radiance.Get("resolution").AsLong(source + ".radiance.resolution");
            table.RadiancePower = radiance.Get("power").AsDouble(source + ".radiance.power");
            table.RadianceLampSize = radiance.Get("lampSize").AsDouble(source + ".radiance.lampSize");
            table.RadianceAlbedo = radiance.Get("albedo").AsDouble(source + ".radiance.albedo");
            if (table.RadianceAlbedo < 0 || table.RadianceAlbedo >= 1) throw new FormatException(source + ".radiance.albedo: доля отражённого света в [0, 1).");
            if (table.RadianceResolution < 1 || table.RadiancePower < 0 || table.RadianceLampSize <= 0) throw new FormatException(source + ".radiance: resolution ≥ 1, power ≥ 0, lampSize > 0.");
            var absorption = root.Get("absorption") ?? throw new FormatException(source + ": нет раздела absorption.");
            foreach (var e in absorption.Get("bySize").Fields) table.AbsorptionBySize.Add(int.Parse(e.Key, System.Globalization.CultureInfo.InvariantCulture), e.Value.AsDouble(source + ".absorption.bySize." + e.Key));
            foreach (var e in absorption.Get("objects").Fields) table.AbsorptionByObject.Add(e.Key, e.Value.AsDouble(source + ".absorption.objects." + e.Key));
            if (table.ShadowEdge < 0) throw new FormatException(source + ".shadows.edge: ширина кромки не может быть отрицательной.");
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
