using System;
using System.Collections.Generic;
using System.Globalization;

namespace Remains.RoomFormat
{
    public sealed class RoomLightSource
    {
        public string Type;
        public double X, Y;            // centre, metres from the room's bottom-left
        public double DiskRadius;      // emitter size for the light transport (soft shadows)
        public RoomLamp Lamp;          // table entry (URP radius / intensity)
        public double[] Color;         // lamp colour × room tint
        public double[] Radiance;      // disk radiance (flatland): fluence 1 m away = power × intensity × Color
    }

    // Light sources and occluders of one room, shared by every lighting consumer (URP lights, the
    // reference tracer, radiance cascades): ambient darkness and tint (room "dark" / land "darkness",
    // land/room "color"), lamps (decor with a lighting-table entry; off when the room says lon < 0),
    // and the wall raster (solid cells and beams block light, stairs do not).
    public sealed class RoomLightScene
    {
        public double Darkness;
        public double[] Tint = { 1, 1, 1 };
        public string TintName;
        public bool LampsOn;
        public readonly List<RoomLightSource> Lamps = new List<RoomLightSource>();
        public readonly List<string> Issues = new List<string>();

        public static RoomLightScene Build(RoomFile room, RoomDefinitionCatalog definitions, RoomLandCatalog lands, RoomLightingTable table, double power, double lampSize)
        {
            if (room == null || definitions == null || lands == null || table == null) throw new ArgumentNullException(room == null ? nameof(room) : definitions == null ? nameof(definitions) : lands == null ? nameof(lands) : nameof(table));
            var scene = new RoomLightScene();
            // Original keys differ: the room says "dark", the land says "darkness".
            scene.Darkness = Number(room.Options.TryGetValue("dark", out string dark) ? dark : lands.Option(room, "darkness", scene.Issues));
            scene.TintName = lands.Option(room, "color", scene.Issues);
            if (!string.IsNullOrEmpty(scene.TintName))
            {
                if (table.Tints.TryGetValue(scene.TintName, out var rgb)) Array.Copy(rgb, scene.Tint, 3);
                else scene.Issues.Add($"{room.Template}: цветовой тон \"{scene.TintName}\" не описан в таблице света — без тона.");
            }
            scene.LampsOn = Number(lands.Option(room, "lon", scene.Issues)) >= 0;
            if (!scene.LampsOn) return scene;
            double cell = room.Cell;
            foreach (var decor in room.Decor)
            {
                if (!table.Lamps.TryGetValue(decor.Type, out var lamp)) continue;
                var d = definitions.Require(decor.Type, room.Template + " декор");
                // Placement cell is the decor's top-left; footprint extends right and down.
                double radius = Math.Max(0.05, d.DecorWidth * cell / 2 * lampSize);
                var color = new[] { lamp.Color[0] * scene.Tint[0], lamp.Color[1] * scene.Tint[1], lamp.Color[2] * scene.Tint[2] };
                double l = power * lamp.Intensity / (2 * radius);
                scene.Lamps.Add(new RoomLightSource
                {
                    Type = decor.Type, Lamp = lamp, DiskRadius = radius, Color = color,
                    X = (decor.X + d.DecorWidth / 2.0) * cell, Y = (decor.Y + 1 - d.DecorHeight / 2.0) * cell + lamp.OffsetY,
                    Radiance = new[] { l * color[0], l * color[1], l * color[2] },
                });
            }
            return scene;
        }

        // Wall raster, `resolution` texels per metre, [x, y] with y up: solid cells and beams.
        public static bool[,] Walls(RoomFile room, RoomGeometry geometry, int resolution)
        {
            double texel = 1.0 / resolution;
            int width = (int)Math.Round(room.Width * room.Cell * resolution), height = (int)Math.Round(room.Height * room.Cell * resolution);
            var solid = new bool[width, height];
            foreach (var s in geometry.Surfaces)
            {
                if (s.Slope != 0) continue;
                for (int x = (int)Math.Round(s.X / texel); x < (int)Math.Round((s.X + s.Width) / texel); x++)
                    for (int y = (int)Math.Round(s.Y / texel); y < (int)Math.Round((s.Y + s.Height) / texel); y++)
                        if (x >= 0 && y >= 0 && x < width && y < height) solid[x, y] = true;
            }
            return solid;
        }

        private static double Number(string value)
            => string.IsNullOrEmpty(value) ? 0 : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : throw new FormatException("Не число: " + value);
    }
}
