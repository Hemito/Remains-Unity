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
        public double RadiancePower, RadianceLampSize, RadianceAlbedo;
        public double LampOtherFrame;   // lamp frames other than "on"/"off" (dimmer variants): share of the power

        // Share of a lamp's power by its drawn frame: "on" 1, "off" 0, other frames LampOtherFrame;
        // lamps without on/off frames in the data shine in every frame.
        public double LampFrameShare(RoomDefinition definition, int frame)
        {
            if (definition.DecorFrameOn == 0 && definition.DecorFrameOff == 0) return 1;
            if (frame == definition.DecorFrameOff) return 0;
            if (frame == definition.DecorFrameOn) return 1;
            return LampOtherFrame;
        }   // lamp fluence 1 m away = power × intensity; disk = lampSize × half decor width   // lit wall edge width (m) and lamp shadow softness 0..1
        public readonly SortedDictionary<string, double[]> Tints = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, RoomLamp> Lamps = new SortedDictionary<string, RoomLamp>(StringComparer.Ordinal);
        // Objects: share of light passing through (transmittance), the visible range is the last
        // percents. By size class for furniture, actors (units) separately; anything with a silhouette
        // larger than LargeArea m² (bigger than a pony: large furniture, alicorns, raiders) at most Large.
        public readonly SortedDictionary<int, double> TransmittanceBySize = new SortedDictionary<int, double>();
        public readonly SortedDictionary<string, double> TransmittanceById = new SortedDictionary<string, double>(StringComparer.Ordinal);
        public double ActorTransmittance, LargeTransmittance, LargeArea;
        public string ObjectLightMode;   // "rim": lit fully, dims only near the dark edge; "gradient": through the whole body
        public double ObjectRimWidth;    // m, rim mode
        public double BeamTransmittance; // share of light a beam (one-way platform) lets through across its height
        public double BeamHeight=1;      // share of the cell (from its top) the beam's drawing covers: its extent for light
        public double ActorShadowDepth,ActorShadowMaxOffset,ActorShadowMaxOpacity,ActorShadowMoveThreshold;
        public int ActorShadowMaxSources;

        // Light an object lets through (0..1). Closed doors: none. Original placement depth: wall = 1 hangs
        // on the back wall (not in the light's plane) → all; wall = 2 stands across the corridor (grates,
        // windows) → 1 − its original "opac"; actors and free-standing furniture by the table.
        public double ObjectTransmittance(RoomDefinition definition, double silhouetteArea)
        {
            if (TransmittanceById.TryGetValue(definition.Id, out double value)) return value;
            if (definition.ObjectType == "door") return 0;   // closed (open state: later, by object state)
            if (definition.Wall == 1) return 1;
            if (definition.Wall >= 2) return definition.Opacity > 0 ? 1 - definition.Opacity : 0;
            if (definition.ObjectType == "unit") value = ActorTransmittance;
            else if (!TransmittanceBySize.TryGetValue(Math.Max(1, definition.ObjectSize), out value))
                throw new InvalidOperationException($"Таблица света: нет пропускания для размера {definition.ObjectSize} ({definition.Id}).");
            return silhouetteArea > LargeArea ? Math.Min(value, LargeTransmittance) : value;
        }

        // Screens and panels glowing by their bright pixels: definition -> (gain, luminance threshold).
        // Light through windows (decor erase clips): radiance of the far background ("fon" of the land) seen in them.
        public readonly SortedDictionary<string, double[]> WindowRadiance = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, (double Gain, double Threshold)> Glow = new SortedDictionary<string, (double, double)>(StringComparer.Ordinal);

        // Share of light an object stops (0..1). Closed doors: all. Original placement depth: wall = 1 hangs on the back
        // wall (not in the light's plane) → 0; wall = 2 stands across the corridor (doors, grates,
        // windows) → its original "opac", else 1; free-standing → by size class (table), unless overridden.

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
            var actorShadows = root.Get("actorShadows") ?? throw new FormatException(source + ": нет раздела actorShadows.");
            table.ActorShadowDepth=actorShadows.Get("depth").AsDouble(source+".actorShadows.depth");
            table.ActorShadowMaxOffset=actorShadows.Get("maxOffset").AsDouble(source+".actorShadows.maxOffset");
            table.ActorShadowMaxOpacity=actorShadows.Get("maxOpacity").AsDouble(source+".actorShadows.maxOpacity");
            table.ActorShadowMoveThreshold=actorShadows.Get("moveThreshold").AsDouble(source+".actorShadows.moveThreshold");
            long shadowSourceCount=actorShadows.Get("maxSources").AsLong(source+".actorShadows.maxSources");
            if(shadowSourceCount<1 || shadowSourceCount>3)
                throw new FormatException(source+".actorShadows.maxSources: ожидается от 1 до 3.");
            table.ActorShadowMaxSources=(int)shadowSourceCount;
            if(!double.IsFinite(table.ActorShadowDepth) || table.ActorShadowDepth<=0 ||
                !double.IsFinite(table.ActorShadowMaxOffset) || table.ActorShadowMaxOffset<=0 ||
                !double.IsFinite(table.ActorShadowMaxOpacity) || table.ActorShadowMaxOpacity<0 || table.ActorShadowMaxOpacity>1 ||
                !double.IsFinite(table.ActorShadowMoveThreshold) || table.ActorShadowMoveThreshold<=0 ||
                table.ActorShadowMaxSources<1 || table.ActorShadowMaxSources>3)
                throw new FormatException(source+".actorShadows: depth/maxOffset/moveThreshold > 0, maxOpacity в [0,1], maxSources от 1 до 3.");
            table.ShadowEdge = shadows.Get("edge").AsDouble(source + ".shadows.edge");
            table.ShadowSoftness = shadows.Get("softness").AsDouble(source + ".shadows.softness");
            var radiance = root.Get("radiance") ?? throw new FormatException(source + ": нет раздела radiance.");
            table.RadianceResolution = (int)radiance.Get("resolution").AsLong(source + ".radiance.resolution");
            table.RadiancePower = radiance.Get("power").AsDouble(source + ".radiance.power");
            table.RadianceLampSize = radiance.Get("lampSize").AsDouble(source + ".radiance.lampSize");
            table.RadianceAlbedo = radiance.Get("albedo").AsDouble(source + ".radiance.albedo");
            table.LampOtherFrame = radiance.Get("lampOtherFrame").AsDouble(source + ".radiance.lampOtherFrame");
            if (table.RadianceAlbedo < 0 || table.RadianceAlbedo >= 1) throw new FormatException(source + ".radiance.albedo: доля отражённого света в [0, 1).");
            if (table.RadianceResolution < 1 || table.RadiancePower < 0 || table.RadianceLampSize <= 0) throw new FormatException(source + ".radiance: resolution ≥ 1, power ≥ 0, lampSize > 0.");
            var windows = root.Get("windows") ?? throw new FormatException(source + ": нет раздела windows.");
            foreach (var e in windows.Fields)
            {
                var c = e.Value.Get("color").Items; double i = e.Value.Get("intensity").AsDouble(source + ".windows." + e.Key + ".intensity");
                if (c.Count != 3 || i < 0) throw new FormatException(source + ".windows." + e.Key + ": color [r,g,b], intensity ≥ 0.");
                table.WindowRadiance.Add(e.Key, new[] { c[0].AsDouble(source) * i, c[1].AsDouble(source) * i, c[2].AsDouble(source) * i });
            }
            var beams = root.Get("beams") ?? throw new FormatException(source + ": нет раздела beams.");
            table.BeamTransmittance = beams.Get("transmittance").AsDouble(source + ".beams.transmittance");
            table.BeamHeight = beams.Get("height").AsDouble(source + ".beams.height");
            if (table.BeamHeight <= 0 || table.BeamHeight > 1) throw new FormatException(source + ".beams.height: доля клетки в (0, 1].");
            if (table.BeamTransmittance < 0 || table.BeamTransmittance >= 1) throw new FormatException(source + ".beams.transmittance: доля в [0, 1).");
            var objects = root.Get("objects") ?? throw new FormatException(source + ": нет раздела objects.");
            var transmittance = objects.Get("transmittance");
            foreach (var e in transmittance.Get("bySize").Fields) table.TransmittanceBySize.Add(int.Parse(e.Key, System.Globalization.CultureInfo.InvariantCulture), e.Value.AsDouble(source + ".objects.transmittance.bySize." + e.Key));
            foreach (var e in transmittance.Get("byId").Fields) table.TransmittanceById.Add(e.Key, e.Value.AsDouble(source + ".objects.transmittance.byId." + e.Key));
            table.ActorTransmittance = transmittance.Get("actor").AsDouble(source + ".objects.transmittance.actor");
            table.LargeTransmittance = transmittance.Get("large").AsDouble(source + ".objects.transmittance.large");
            table.LargeArea = transmittance.Get("largeArea").AsDouble(source + ".objects.transmittance.largeArea");
            var objectLight = objects.Get("lighting");
            table.ObjectLightMode = objectLight.Get("mode").AsString(source + ".objects.lighting.mode");
            if (table.ObjectLightMode != "rim" && table.ObjectLightMode != "gradient") throw new FormatException(source + ".objects.lighting.mode: rim или gradient.");
            table.ObjectRimWidth = objectLight.Get("rimWidth").AsDouble(source + ".objects.lighting.rimWidth");
            foreach (var e in (root.Get("glow") ?? throw new FormatException(source + ": нет раздела glow.")).Fields)
                table.Glow.Add(e.Key, (e.Value.Get("gain").AsDouble(source + ".glow." + e.Key + ".gain"), e.Value.Get("threshold").AsDouble(source + ".glow." + e.Key + ".threshold")));
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
