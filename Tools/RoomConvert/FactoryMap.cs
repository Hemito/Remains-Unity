using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Remains.RoomFormat;

namespace Remains.RoomConvert
{
    // layout: generate a factory layout from room templates, compose its level grid and draw a schematic map.
    //   layout --rooms-dir <plant rooms> --definitions <definitions.json> --lands <lands.json> --seed <world seed>
    //          [--land plant --index 0 --width 4 --depth 21] --out <layout.json> [--grid-out <level.room.json>] [--png <map.png> --scale 3]
    // The map draws the composed level grid (FactoryGridBuilder: shared edges, cut passages, land border),
    // so it shows exactly what level preparation builds from.
    public static class FactoryMap
    {
        public static int Run(Dictionary<string, string> o, Func<Dictionary<string, string>, string, string> required)
        {
            string dir = required(o, "rooms-dir");
            var rooms = Directory.GetFiles(dir, "*.room.json").OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => RoomSerializer.Read(File.ReadAllText(f, Encoding.UTF8), f)).ToList();
            var definitions = RoomDefinitionCatalog.Read(File.ReadAllText(required(o, "definitions"), Encoding.UTF8), required(o, "definitions"));
            var lands = RoomLandCatalog.Read(File.ReadAllText(required(o, "lands"), Encoding.UTF8), required(o, "lands"));
            ulong seed = ulong.Parse(required(o, "seed"), CultureInfo.InvariantCulture);
            string land = o.TryGetValue("land", out string l) ? l : "plant";
            int Int(string key, int fallback) => o.TryGetValue(key, out string v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
            var layout = FactoryLayoutGenerator.Generate(rooms, land, seed, Int("index", 0), Int("width", 4), Int("depth", 21));
            File.WriteAllText(required(o, "out"), layout.Write(), new UTF8Encoding(false));
            int distinct = layout.Cells.Select(c => c.Template).Distinct().Count();
            Console.WriteLine($"Завод {land}:{layout.Index}, сид {seed}: {layout.Width}×{layout.Depth}, комнат разных {distinct}, повторов сверх бюджета {layout.Cells.Count(c => c.Repeat)}, " +
                $"зеркальных {layout.Cells.Count(c => c.Mirror)}, проходов {layout.Seams.Count}, чекпоинтов {layout.Cells.Count(c => c.Role == FactoryCellRole.Checkpoint)}, " +
                $"выходов {layout.Cells.Count(c => c.Role == FactoryCellRole.Exit)}, испытаний {layout.Cells.Count(c => c.Role == FactoryCellRole.Trial)}.");
            foreach (string issue in layout.Issues) Console.WriteLine("  " + issue);
            var grid = FactoryGridBuilder.Build(layout, rooms.ToDictionary(r => r.Template, StringComparer.Ordinal), definitions, FactoryGridBuilder.BorderSolid(lands, land, definitions), closeOuter: true);
            var geometry = RoomGeometryBuilder.Build(grid.Level, definitions);
            int border = 0; foreach (bool b in grid.Border) if (b) border++;
            Console.WriteLine($"Сетка уровня {grid.Level.Width}×{grid.Level.Height}: граница {border} клеток, проходов {grid.Passages.Count} " +
                $"(стену ломали {grid.Passages.Count(p => p.FirstBrokeWall || p.SecondBrokeWall)}), поверхностей {geometry.Surfaces.Count}, лестниц {geometry.Ladders.Count}, " +
                $"SHA256 {Sha(RoomSerializer.Write(grid.Level))}.");
            foreach (string issue in grid.Issues.Concat(geometry.Issues)) Console.WriteLine("  " + issue);
            if (o.TryGetValue("grid-out", out string gridOut)) File.WriteAllText(gridOut, RoomSerializer.Write(grid.Level), new UTF8Encoding(false));
            if (o.TryGetValue("png", out string png)) Draw(grid, layout, png, Int("scale", 3));
            return 0;
        }

        private static string Sha(string text)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private static void Draw(FactoryGrid grid, FactoryLayout layout, string file, int scale)
        {
            var level = grid.Level;
            int cellsX = level.Width, cellsY = level.Height;
            var kind = new byte[cellsX, cellsY];   // 0 empty, 1 structure, 2 solid, 3 passage, 4 border
            foreach (var p in grid.Passages) foreach (var (x, row) in p.Cells) kind[x, row] = 3;
            for (int row = 0; row < cellsY; row++)
                for (int x = 0; x < cellsX; x++)
                {
                    bool solid = Char(level, RoomLayers.Solid, x, row);
                    bool structure = RoomLayers.Structural.Any(layer => Char(level, layer, x, row));
                    if (grid.Border[x, row]) kind[x, row] = 4;
                    else if (solid) kind[x, row] = 2;
                    else if (structure) kind[x, row] = 1;
                }
            var band = new int[cellsX, cellsY];
            for (int row = 0; row < cellsY; row++) for (int x = 0; x < cellsX; x++) band[x, row] = grid.Owner(x, row).Cell.Band;
            int w = grid.StepX + 1, h = grid.StepY + 1, stepX = grid.StepX, stepY = grid.StepY;
            var bands = new[] { (230, 236, 242), (214, 228, 214), (238, 226, 206), (238, 212, 212) };
            var image = new PngImage { Width = cellsX * scale, Height = cellsY * scale, Rgba = new byte[cellsX * scale * cellsY * scale * 4] };
            void Put(int px, int py, (int r, int g, int b) color)
            {
                if (px < 0 || py < 0 || px >= image.Width || py >= image.Height) return;
                int o = (py * image.Width + px) * 4;
                image.Rgba[o] = (byte)color.r; image.Rgba[o + 1] = (byte)color.g; image.Rgba[o + 2] = (byte)color.b; image.Rgba[o + 3] = 255;
            }
            for (int gy = 0; gy < cellsY; gy++)
                for (int gx = 0; gx < cellsX; gx++)
                {
                    var b = bands[Math.Min(band[gx, gy], bands.Length - 1)];
                    var color = kind[gx, gy] switch { 4 => (24, 26, 30), 2 => (52, 54, 60), 1 => (130, 132, 140), 3 => (60, 170, 230), _ => b };
                    for (int py = 0; py < scale; py++) for (int px = 0; px < scale; px++) Put(gx * scale + px, gy * scale + py, color);
                }
            for (int gy = 0; gy < cellsY; gy++)          // room borders (the shared strip of neighbours)
                for (int gx = 0; gx < cellsX; gx++)
                    if ((gx % stepX == 0 || gy % stepY == 0) && kind[gx, gy] == 0)
                        for (int p = 0; p < scale; p++) { Put(gx * scale + p, gy * scale + scale / 2, (170, 172, 180)); Put(gx * scale + scale / 2, gy * scale + p, (170, 172, 180)); }
            foreach (var c in layout.Cells)
            {
                (int, int, int)? mark = c.Role switch
                {
                    FactoryCellRole.Checkpoint => (40, 190, 70), FactoryCellRole.Exit => (220, 40, 40),
                    FactoryCellRole.Trial => c.RoleRequired ? (240, 130, 20) : (240, 210, 30), _ => null,
                };
                int cx = (c.X * stepX + w / 2) * scale, cy = (c.Y * stepY + h / 2) * scale, size = 3 * scale;
                if (mark != null) for (int py = -size; py <= size; py++) for (int px = -size; px <= size; px++) Put(cx + px, cy + py, mark.Value);
                if (c.Start) Frame(c, (40, 90, 220)); else if (c.Repeat) Frame(c, (200, 40, 200));
                void Frame(FactoryCell cell, (int, int, int) color)
                {
                    int x0 = cell.X * stepX * scale, y0 = cell.Y * stepY * scale, x1 = x0 + w * scale - 1, y1 = y0 + h * scale - 1;
                    for (int px = x0; px <= x1; px++) { Put(px, y0, color); Put(px, y0 + 1, color); Put(px, y1, color); Put(px, y1 - 1, color); }
                    for (int py = y0; py <= y1; py++) { Put(x0, py, color); Put(x0 + 1, py, color); Put(x1, py, color); Put(x1 - 1, py, color); }
                }
            }
            image.Write(file);
            Console.WriteLine($"Схема: {file} ({image.Width}×{image.Height}).");
        }

        private static bool Char(RoomFile room, string layer, int x, int y) =>
            room.Layers.TryGetValue(layer, out var rows) && y < rows.Length && x < rows[y].Length && rows[y][x] != RoomFile.Empty;
    }
}
