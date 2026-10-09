using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    public sealed class ShadowRect
    {
        public double X, Y, Width, Height;
        public bool SelfShadow;   // walls: dark inside; thin beams: lit by their own light
    }

    [Flags]
    public enum WallOpen : byte
    {
        None = 0, Top = 1, Right = 2, Bottom = 4, Left = 8,
        // Inner corners: the diagonal cell is open while both sides at that corner are closed.
        TopLeft = 16, TopRight = 32, BottomRight = 64, BottomLeft = 128,
    }

    public struct WallEdge
    {
        public bool Wall;
        public WallOpen Open;
        public double Height;   // wall top above the cell bottom (lowered for partial height)
    }

    // Wall cells and their open sides/inner corners (lighting L1), one model for both consumers:
    // shadow casters (Build: whole wall rectangles merged per row by height, dark inside; beams cast
    // plain shadows; stairs none — thin treads) and the terrain shader's edge map (Edges: the shader
    // reads lamp light just outside the nearest open side/corner and fades it into the wall body).
    // Room borders count as closed (the wall continues in the neighbouring room).
    public static class RoomShadowShapes
    {
        public static WallEdge[,] Edges(RoomGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            double cell = geometry.Cell;
            int width = (int)Math.Round(geometry.Width / cell), height = (int)Math.Round(geometry.Height / cell);
            var heights = new double?[width, height];
            foreach (var s in geometry.Surfaces)
                if (s.Slope == 0 && !s.OneWay)
                    for (int i = 0; i < s.Cells; i++) heights[s.CellX + i, s.CellY] = s.Height;

            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
            bool Wall(int x, int y) => !Inside(x, y) || heights[x, y] != null;
            bool Full(int x, int y) => !Inside(x, y) || heights[x, y] is double h && h >= cell - 1e-9;

            var edges = new WallEdge[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (heights[x, y] is not double h) continue;
                    bool full = h >= cell - 1e-9;
                    var open = WallOpen.None;
                    if (!full || !Wall(x, y + 1)) open |= WallOpen.Top;
                    if (!Wall(x + 1, y)) open |= WallOpen.Right;
                    if (!Full(x, y - 1)) open |= WallOpen.Bottom;
                    if (!Wall(x - 1, y)) open |= WallOpen.Left;
                    bool top = (open & WallOpen.Top) == 0, bottom = (open & WallOpen.Bottom) == 0;
                    bool left = (open & WallOpen.Left) == 0, right = (open & WallOpen.Right) == 0;
                    if (top && left && !Wall(x - 1, y + 1)) open |= WallOpen.TopLeft;
                    if (top && right && !Wall(x + 1, y + 1)) open |= WallOpen.TopRight;
                    if (bottom && right && !Wall(x + 1, y - 1)) open |= WallOpen.BottomRight;
                    if (bottom && left && !Wall(x - 1, y - 1)) open |= WallOpen.BottomLeft;
                    edges[x, y] = new WallEdge { Wall = true, Open = open, Height = h };
                }
            return edges;
        }

        public static List<ShadowRect> Build(RoomGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            double cell = geometry.Cell;
            var result = new List<ShadowRect>();
            foreach (var s in geometry.Surfaces)
                if (s.Slope == 0 && s.OneWay) result.Add(new ShadowRect { X = s.X, Y = s.Y, Width = s.Width, Height = s.Height });

            var edges = Edges(geometry);
            int width = edges.GetLength(0), height = edges.GetLength(1);
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    var e = edges[x, y];
                    if (!e.Wall) { x++; continue; }
                    int end = x + 1;
                    while (end < width && edges[end, y].Wall && Math.Abs(edges[end, y].Height - e.Height) < 1e-9) end++;
                    result.Add(new ShadowRect { X = x * cell, Y = y * cell, Width = (end - x) * cell, Height = e.Height, SelfShadow = true });
                    x = end;
                }
            }
            return result;
        }
    }
}
