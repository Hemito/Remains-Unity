using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Remains.RoomFormat;

namespace Remains.RoomConvert
{
    // Reference 2D ("flatland") light transport for one room: how the lighting should look in
    // theory, to compare cheap real-time methods against. Not a game system, no speed goal.
    //  - light travels in the room plane; walls (solid cells, beams) block it, stairs do not;
    //  - lamps are disks (decor with a lamp entry in the lighting table), soft shadows from their size;
    //  - walls reflect diffusely (albedo), bounces solved on wall boundary elements (radiosity);
    //  - each open pixel gathers incoming radiance over the full circle (fluence);
    //  - wall bodies show the light just outside their open sides, fading over shadows.edge
    //    (same rule as the WorldTerrain shader);
    //  - optional composite: albedo render (unlit preview) × (ambient + light).
    // Rays leaving the room are lost (neighbouring rooms are not loaded); room borders are walls in
    // practice (solid cells), door openings let light out.
    public static class LightReference
    {
        public static int Run(Dictionary<string, string> o, Func<Dictionary<string, string>, string, string> required)
        {
            var culture = CultureInfo.InvariantCulture;
            double Num(string key, double fallback) => o.TryGetValue(key, out string v) ? double.Parse(v, culture) : fallback;
            string roomFile = required(o, "room"), outFile = required(o, "out");
            var room = RoomSerializer.Read(File.ReadAllText(roomFile, Encoding.UTF8), roomFile);
            var definitions = RoomDefinitionCatalog.Read(File.ReadAllText(required(o, "definitions"), Encoding.UTF8), "definitions");
            var lands = RoomLandCatalog.Read(File.ReadAllText(required(o, "lands"), Encoding.UTF8), "lands");
            var table = RoomLightingTable.Read(File.ReadAllText(required(o, "lighting"), Encoding.UTF8), "lighting");
            var settings = new Settings
            {
                Resolution = (int)Num("resolution", table.RadianceResolution), Rays = (int)Num("rays", 256), Bounces = (int)Num("bounces", 2),
                ElementRays = (int)Num("element-rays", 512), Albedo = Num("albedo", table.RadianceAlbedo), Power = Num("power", table.RadiancePower),
                LampSize = Num("lamp-size", table.RadianceLampSize), DiskSamples = (int)Num("disk-samples", 8), Edge = table.ShadowEdge,
            };
            var watch = Stopwatch.StartNew();
            var tracer = new Tracer(room, definitions, lands, table, settings);
            var light = tracer.Solve();
            Console.WriteLine($"{room.Template}: сетка {tracer.Width}×{tracer.Height}, ламп {tracer.LampCount}, элементов стен {tracer.ElementCount}, {watch.Elapsed.TotalSeconds:0.0} с.");

            double ambient = o.ContainsKey("ambient") ? Num("ambient", 0) : table.Ambient(tracer.Darkness);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));
            WriteLightMap(tracer, light, Path.ChangeExtension(outFile, null) + "-light.png", Num("exposure", 1));
            if (o.TryGetValue("albedo-image", out string albedoFile))
            {
                var albedo = PngImage.Read(albedoFile);
                Composite(tracer, light, albedo, ambient, tracer.Tint, Num("exposure", 1));
                albedo.Write(outFile);
                Console.WriteLine($"Итог: {outFile} (общий свет {ambient:0.##}).");
            }
            return 0;
        }

        public sealed class Settings
        {
            public int Resolution, Rays, Bounces, ElementRays, DiskSamples;
            public double Albedo, Power, LampSize, Edge;
        }

        private static void WriteLightMap(Tracer t, double[,][] light, string file, double exposure)
        {
            var image = new PngImage { Width = t.Width, Height = t.Height, Rgba = new byte[t.Width * t.Height * 4] };
            for (int y = 0; y < t.Height; y++)
                for (int x = 0; x < t.Width; x++)
                {
                    int o = ((t.Height - 1 - y) * t.Width + x) * 4;
                    var c = light[x, y];
                    for (int k = 0; k < 3; k++) image.Rgba[o + k] = ToSrgb(1 - Math.Exp(-c[k] * exposure));
                    image.Rgba[o + 3] = 255;
                }
            image.Write(file);
        }

        private static void Composite(Tracer t, double[,][] light, PngImage albedo, double ambient, double[] tint, double exposure)
        {
            for (int py = 0; py < albedo.Height; py++)
                for (int px = 0; px < albedo.Width; px++)
                {
                    // Pixel centre in light-map texels (y up), bilinear.
                    double fx = (px + 0.5) / albedo.Width * t.Width - 0.5, fy = (albedo.Height - py - 0.5) / albedo.Height * t.Height - 0.5;
                    int x0 = Math.Clamp((int)Math.Floor(fx), 0, t.Width - 1), y0 = Math.Clamp((int)Math.Floor(fy), 0, t.Height - 1);
                    int x1 = Math.Min(x0 + 1, t.Width - 1), y1 = Math.Min(y0 + 1, t.Height - 1);
                    double ax = Math.Clamp(fx - x0, 0, 1), ay = Math.Clamp(fy - y0, 0, 1);
                    int o = (py * albedo.Width + px) * 4;
                    for (int k = 0; k < 3; k++)
                    {
                        double l = (light[x0, y0][k] * (1 - ax) + light[x1, y0][k] * ax) * (1 - ay) + (light[x0, y1][k] * (1 - ax) + light[x1, y1][k] * ax) * ay;
                        double v = FromSrgb(albedo.Rgba[o + k]) * (ambient * tint[k] + l * exposure);
                        albedo.Rgba[o + k] = ToSrgb(v);
                    }
                }
        }

        private static double FromSrgb(byte b) { double c = b / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        private static byte ToSrgb(double c)
        {
            c = Math.Clamp(c, 0, 1);
            double s = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
            return (byte)Math.Round(s * 255);
        }

        private sealed class Lamp { public double X, Y, Radius; public double[] Radiance; }

        private sealed class Tracer
        {
            public readonly int Width, Height, LampCount;
            public readonly double Darkness;
            public readonly double[] Tint = { 1, 1, 1 };
            public int ElementCount => elements.Count;
            private readonly double texel;
            private readonly bool[,] solid;
            private readonly Settings s;
            private readonly List<Lamp> lamps = new List<Lamp>();
            // Wall boundary elements: a solid texel side facing an open texel. side: 0 up, 1 right, 2 down, 3 left.
            private readonly Dictionary<long, int> elementIndex = new Dictionary<long, int>();
            private readonly List<(double x, double y, double nx, double ny)> elements = new List<(double, double, double, double)>();
            private double[][] elementRadiance;
            private static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

            public Tracer(RoomFile room, RoomDefinitionCatalog definitions, RoomLandCatalog lands, RoomLightingTable table, Settings settings)
            {
                s = settings;
                texel = 1.0 / settings.Resolution;
                solid = RoomLightScene.Walls(room, RoomGeometryBuilder.Build(room, definitions), settings.Resolution);
                Width = solid.GetLength(0); Height = solid.GetLength(1);
                var scene = RoomLightScene.Build(room, definitions, lands, table, settings.Power, settings.LampSize);
                Darkness = scene.Darkness;
                Array.Copy(scene.Tint, Tint, 3);
                foreach (var source in scene.Lamps)
                    lamps.Add(new Lamp { X = source.X, Y = source.Y, Radius = Math.Max(texel, source.DiskRadius), Radiance = source.Radiance });
                LampCount = lamps.Count;
                foreach (string issue in scene.Issues) Console.Error.WriteLine("Предупреждение: " + issue);

                for (int x = 0; x < Width; x++)
                    for (int y = 0; y < Height; y++)
                    {
                        if (!solid[x, y]) continue;
                        for (int side = 0; side < 4; side++)
                        {
                            int nx = x + Dx[side], ny = y + Dy[side];
                            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height || solid[nx, ny]) continue;
                            elementIndex[Key(x, y, side)] = elements.Count;
                            elements.Add(((x + 0.5 + Dx[side] * 0.5) * texel, (y + 0.5 + Dy[side] * 0.5) * texel, Dx[side], Dy[side]));
                        }
                    }
            }

            private static long Key(int x, int y, int side) => ((long)x * 100000 + y) * 4 + side;

            public double[,][] Solve()
            {
                // Wall elements: direct light, then bounces (each pass gathers the previous pass).
                var direct = new double[elements.Count][];
                Parallel.For(0, elements.Count, i =>
                {
                    var e = elements[i];
                    direct[i] = Direct(e.x + e.nx * texel * 0.01, e.y + e.ny * texel * 0.01, e.nx, e.ny, new Random(i));
                });
                elementRadiance = new double[elements.Count][];
                for (int i = 0; i < elements.Count; i++) elementRadiance[i] = Scale(direct[i], s.Albedo / 2);
                for (int bounce = 1; bounce < s.Bounces; bounce++)
                {
                    var next = new double[elements.Count][];
                    Parallel.For(0, elements.Count, i =>
                    {
                        var e = elements[i];
                        var random = new Random(i * 7919 + bounce);
                        var gathered = new double[3];
                        for (int r = 0; r < s.ElementRays; r++)
                        {
                            // Cosine-weighted half circle: angle from the normal = asin(2u - 1); estimator E = 2 / N × ΣL.
                            double a = Math.Asin(2 * ((r + random.NextDouble()) / s.ElementRays) - 1) + Math.Atan2(e.ny, e.nx);
                            Add(gathered, Trace(e.x + e.nx * texel * 0.01, e.y + e.ny * texel * 0.01, Math.Cos(a), Math.Sin(a)), 2.0 / s.ElementRays);
                        }
                        Add(gathered, direct[i], 1);
                        next[i] = Scale(gathered, s.Albedo / 2);
                    });
                    elementRadiance = next;
                }

                // Open pixels: direct (all directions) + gathered bounce light over the full circle.
                var light = new double[Width, Height][];
                Parallel.For(0, Height, y =>
                {
                    var random = new Random(y * 104729 + 1);
                    for (int x = 0; x < Width; x++)
                    {
                        if (solid[x, y]) continue;
                        double px = (x + 0.5) * texel, py = (y + 0.5) * texel;
                        var sum = Direct(px, py, 0, 0, random);
                        double offset = random.NextDouble();
                        for (int r = 0; r < s.Rays; r++)
                        {
                            double a = 2 * Math.PI * (r + offset) / s.Rays;
                            Add(sum, Trace(px, py, Math.Cos(a), Math.Sin(a)), 2 * Math.PI / s.Rays);
                        }
                        light[x, y] = sum;
                    }
                });

                // Wall bodies: light just outside the nearest open texel, faded over the edge width.
                int reach = (int)Math.Ceiling(s.Edge / texel);
                Parallel.For(0, Height, y =>
                {
                    for (int x = 0; x < Width; x++)
                    {
                        if (!solid[x, y]) continue;
                        var best = new double[3];
                        for (int dy = -reach; dy <= reach; dy++)
                            for (int dx = -reach; dx <= reach; dx++)
                            {
                                int ox = x + dx, oy = y + dy;
                                if (ox < 0 || oy < 0 || ox >= Width || oy >= Height || solid[ox, oy]) continue;
                                double d = (Math.Sqrt(dx * dx + dy * dy) - 0.5) * texel;
                                double t = Math.Clamp(d / s.Edge, 0, 1), fade = 1 - t * t * (3 - 2 * t);
                                for (int k = 0; k < 3; k++) best[k] = Math.Max(best[k], light[ox, oy][k] * fade);
                            }
                        light[x, y] = best;
                    }
                });
                return light;
            }

            // Direct light from lamp disks: fluence at a point (nx = ny = 0) or irradiance on a surface (cosine).
            private double[] Direct(double px, double py, double nx, double ny, Random random)
            {
                var sum = new double[3];
                foreach (var lamp in lamps)
                {
                    double dx = lamp.X - px, dy = lamp.Y - py, d = Math.Max(Math.Sqrt(dx * dx + dy * dy), lamp.Radius);
                    double ux = dx / d, uy = dy / d;
                    double cos = nx == 0 && ny == 0 ? 1 : ux * nx + uy * ny;
                    if (cos <= 0) continue;
                    // Samples across the disk diameter perpendicular to the view direction (soft shadow).
                    double angle = 2 * Math.Asin(Math.Min(1, lamp.Radius / d)), visible = 0;
                    for (int k = 0; k < s.DiskSamples; k++)
                    {
                        double t = ((k + random.NextDouble()) / s.DiskSamples * 2 - 1) * lamp.Radius;
                        double qx = lamp.X - uy * t, qy = lamp.Y + ux * t;
                        if (Visible(px, py, qx, qy)) visible++;
                    }
                    Add(sum, lamp.Radiance, angle * cos * visible / s.DiskSamples);
                }
                return sum;
            }

            private bool Visible(double ax, double ay, double bx, double by)
            {
                double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) return true;
                return !Cast(ax, ay, dx / len, dy / len, len, out _, out _, out _);
            }

            // Radiance arriving along a ray: the wall element it hits (lamps are counted in Direct).
            private double[] Trace(double px, double py, double dx, double dy)
            {
                if (elementRadiance == null || !Cast(px, py, dx, dy, double.MaxValue, out int hx, out int hy, out int side)) return Zero;
                return elementIndex.TryGetValue(Key(hx, hy, side), out int i) ? elementRadiance[i] : Zero;
            }

            private static readonly double[] Zero = new double[3];

            // Grid DDA: first solid texel (or the room border, treated as a wall) along the ray.
            private bool Cast(double px, double py, double dx, double dy, double maxDistance, out int hx, out int hy, out int side)
            {
                int x = (int)Math.Floor(px / texel), y = (int)Math.Floor(py / texel);
                int stepX = dx > 0 ? 1 : -1, stepY = dy > 0 ? 1 : -1;
                double tDeltaX = dx == 0 ? double.MaxValue : texel / Math.Abs(dx), tDeltaY = dy == 0 ? double.MaxValue : texel / Math.Abs(dy);
                double tMaxX = dx == 0 ? double.MaxValue : ((dx > 0 ? x + 1 : x) * texel - px) / dx;
                double tMaxY = dy == 0 ? double.MaxValue : ((dy > 0 ? y + 1 : y) * texel - py) / dy;
                hx = hy = side = 0;
                while (true)
                {
                    double t;
                    if (tMaxX < tMaxY) { t = tMaxX; x += stepX; tMaxX += tDeltaX; side = stepX > 0 ? 3 : 1; }
                    else { t = tMaxY; y += stepY; tMaxY += tDeltaY; side = stepY > 0 ? 2 : 0; }
                    if (t > maxDistance) return false;
                    if (x < 0 || y < 0 || x >= Width || y >= Height) return false;   // border: no element there
                    if (solid[x, y]) { hx = x; hy = y; return true; }
                }
            }

            private static void Add(double[] sum, double[] v, double w) { for (int k = 0; k < 3; k++) sum[k] += v[k] * w; }
            private static double[] Scale(double[] v, double w) => new[] { v[0] * w, v[1] * w, v[2] * w };
        }
    }
}
