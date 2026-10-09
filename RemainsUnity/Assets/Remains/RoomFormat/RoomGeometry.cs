using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    // Physical support rectangle in room metres (origin bottom-left). Derived data: the
    // grid stays the source of truth; rectangles only answer "where is the contact".
    public sealed class GeometrySurface
    {
        public double X, Y, Width, Height;
        public string Definition, MovementSound;
        public bool OneWay, ShotsPassThrough;
        public int Slope;
        public int CellX, CellY, Cells;       // source run: first cell and length along x
    }

    public sealed class GeometryLadder
    {
        public double X, Bottom, Top, MovementOffset, ArtworkOffset;
        public int Facing;
        public string Definition;
    }

    public sealed class RoomGeometry
    {
        public double Width, Height, Cell;
        public readonly List<GeometrySurface> Surfaces = new List<GeometrySurface>();
        public readonly List<GeometryLadder> Ladders = new List<GeometryLadder>();
        public readonly List<string> Issues = new List<string>();

        // Grid lookup: the cell that contains a point (spec: hit point -> cell by division).
        public static bool TryCell(double x, double y, double cell, int width, int height, out int cellX, out int cellY)
        {
            cellX = (int)Math.Floor(x / cell);
            cellY = (int)Math.Floor(y / cell);
            return cellX >= 0 && cellY >= 0 && cellX < width && cellY < height;
        }
    }

    // Builds rectangles in the current RoomLayout semantics (docs/pipelines/room-format.md §5):
    // solids and platforms merge along a row while the definition and height match; stairs stay
    // per cell; ladders are vertical runs per column. Partial height lowers the cell top.
    public static class RoomGeometryBuilder
    {
        public static RoomGeometry Build(RoomFile room, RoomDefinitionCatalog definitions)
        {
            var geometry = new RoomGeometry { Width = room.Width * room.Cell, Height = room.Height * room.Cell, Cell = room.Cell };
            string ctx = room.Template;
            char[][] Grid(string layer) => room.Layers.TryGetValue(layer, out var rows) ? Array.ConvertAll(rows, s => s.ToCharArray()) : null;
            var heights = Grid(RoomLayers.Height);
            int HeightAt(int row, int x) => heights == null || heights[row][x] == RoomFile.Empty ? 0 : heights[row][x] - '0';

            Runs(room, definitions, geometry, RoomLayers.Solid, Grid(RoomLayers.Solid), HeightAt, false, false, true);
            Runs(room, definitions, geometry, RoomLayers.Platform, Grid(RoomLayers.Platform), HeightAt, true, true, true);
            Runs(room, definitions, geometry, RoomLayers.Stairs, Grid(RoomLayers.Stairs), HeightAt, true, true, false);
            Ladders(room, definitions, geometry, Grid(RoomLayers.Ladder));

            if (heights != null)
            {
                var solid = Grid(RoomLayers.Solid);
                var platform = Grid(RoomLayers.Platform);
                var stairs = Grid(RoomLayers.Stairs);
                int orphan = 0;
                for (int r = 0; r < room.Height; r++)
                    for (int x = 0; x < room.Width; x++)
                        if (HeightAt(r, x) > 0 && Empty(solid, r, x) && Empty(platform, r, x) && Empty(stairs, r, x)) orphan++;
                if (orphan > 0) geometry.Issues.Add($"{ctx}: {orphan} клеток с частичной высотой без физической формы — физики не дают.");
            }
            var distinct = new List<string>(new HashSet<string>(geometry.Issues));
            distinct.Sort(StringComparer.Ordinal);
            geometry.Issues.Clear();
            geometry.Issues.AddRange(distinct);
            return geometry;
        }

        private static bool Empty(char[][] grid, int r, int x) => grid == null || grid[r][x] == RoomFile.Empty;

        private static void Runs(RoomFile room, RoomDefinitionCatalog definitions, RoomGeometry geometry, string layer, char[][] grid,
            Func<int, int, int> heightAt, bool oneWay, bool shotsPass, bool merge)
        {
            if (grid == null) return;
            var palette = room.Palettes[layer];
            for (int r = 0; r < room.Height; r++)
            {
                int x = 0;
                while (x < room.Width)
                {
                    char key = grid[r][x];
                    if (key == RoomFile.Empty) { x++; continue; }
                    int height = heightAt(r, x), end = x + 1;
                    if (merge)
                        while (end < room.Width && grid[r][end] == key && heightAt(r, end) == height) end++;
                    var definition = definitions.Require(palette[key], $"{room.Template} слой {layer} клетка {x},{room.Height - 1 - r}");
                    if (string.IsNullOrEmpty(definition.MovementSound))
                        geometry.Issues.Add($"{room.Template}: {definition.Id} без набора звуков шагов — используется настройка комнаты.");
                    int cellY = room.Height - 1 - r;
                    geometry.Surfaces.Add(new GeometrySurface
                    {
                        X = x * room.Cell, Y = cellY * room.Cell,
                        Width = (end - x) * room.Cell, Height = room.Cell * (4 - height) / 4.0,
                        Definition = definition.Id, MovementSound = definition.MovementSound,
                        OneWay = oneWay, ShotsPassThrough = shotsPass, Slope = definition.Slope,
                        CellX = x, CellY = cellY, Cells = end - x,
                    });
                    x = end;
                }
            }
        }

        private static void Ladders(RoomFile room, RoomDefinitionCatalog definitions, RoomGeometry geometry, char[][] grid)
        {
            if (grid == null) return;
            var palette = room.Palettes[RoomLayers.Ladder];
            for (int x = 0; x < room.Width; x++)
            {
                int r = 0;
                while (r < room.Height)
                {
                    char key = grid[r][x];
                    if (key == RoomFile.Empty) { r++; continue; }
                    int start = r;
                    while (r < room.Height && grid[r][x] == key) r++;
                    var definition = definitions.Require(palette[key], $"{room.Template} лестница {x},{room.Height - 1 - start}");
                    geometry.Ladders.Add(new GeometryLadder
                    {
                        X = (x + definition.LadderFraction) * room.Cell,
                        Bottom = (room.Height - r) * room.Cell, Top = (room.Height - start) * room.Cell,
                        Facing = definition.Facing, MovementOffset = definition.MovementOffset, ArtworkOffset = definition.ArtworkOffset,
                        Definition = definition.Id,
                    });
                }
            }
        }
    }
}
