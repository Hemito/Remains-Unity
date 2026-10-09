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

    // Lit wall edge (lighting L1): a lamp lights a wall only near its sides open to the room, fading
    // into the dark body. One model of wall cells and their open sides/corners feeds both consumers:
    // shadow casters (Build: walls cast and receive their own shadow, open sides moved inward by
    // `edge`, open inner corners cut out, so the edge strip is never inside a caster) and the
    // terrain shader's edge map (Edges: per-cell open sides/corners, the shader fades lamp light by
    // the distance to them). Wall cells of one row are merged while openness and height match, so
    // internal seams get no inset (no light slits). Room borders count as closed (the wall continues
    // in the neighbouring room). Beams cast plain shadows; stairs cast none (thin treads).
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

        public static List<ShadowRect> Build(RoomGeometry geometry, double edge)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (edge < 0 || edge >= geometry.Cell / 2) throw new ArgumentOutOfRangeException(nameof(edge), edge, "Кромка должна быть в пределах [0, половина клетки).");
            double cell = geometry.Cell;
            var result = new List<ShadowRect>();
            foreach (var s in geometry.Surfaces)
                if (s.Slope == 0 && s.OneWay) result.Add(new ShadowRect { X = s.X, Y = s.Y, Width = s.Width, Height = s.Height });

            var edges = Edges(geometry);
            int width = edges.GetLength(0), height = edges.GetLength(1);
            const WallOpen corners = WallOpen.TopLeft | WallOpen.TopRight | WallOpen.BottomRight | WallOpen.BottomLeft;
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    var e = edges[x, y];
                    if (!e.Wall) { x++; continue; }
                    var rowOpen = e.Open & (WallOpen.Top | WallOpen.Bottom);
                    int end = x + 1;
                    if ((e.Open & corners) == 0)
                        while (end < width && edges[end, y].Wall && Math.Abs(edges[end, y].Height - e.Height) < 1e-9
                            && (edges[end, y].Open & corners) == 0 && (edges[end, y].Open & (WallOpen.Top | WallOpen.Bottom)) == rowOpen) end++;
                    var open = rowOpen | (e.Open & (WallOpen.Left | corners)) | (edges[end - 1, y].Open & WallOpen.Right);
                    Cut(result, x * cell, end * cell, y * cell, y * cell + e.Height, open, edge);
                    x = end;
                }
            }
            return result;
        }

        // Rectangle with open sides moved inward and open inner corners cut out (edge × edge), as
        // up to three horizontal strips.
        private static void Cut(List<ShadowRect> result, double x0, double x1, double y0, double y1, WallOpen open, double edge)
        {
            bool Has(WallOpen flag) => (open & flag) != 0;
            if (Has(WallOpen.Left)) x0 += edge;
            if (Has(WallOpen.Right)) x1 -= edge;
            if (Has(WallOpen.Bottom)) y0 += edge;
            if (Has(WallOpen.Top)) y1 -= edge;
            double bottomCut = Has(WallOpen.BottomLeft) || Has(WallOpen.BottomRight) ? edge : 0;
            double topCut = Has(WallOpen.TopLeft) || Has(WallOpen.TopRight) ? edge : 0;
            Add(result, x0 + (Has(WallOpen.BottomLeft) ? edge : 0), x1 - (Has(WallOpen.BottomRight) ? edge : 0), y0, y0 + bottomCut);
            Add(result, x0, x1, y0 + bottomCut, y1 - topCut);
            Add(result, x0 + (Has(WallOpen.TopLeft) ? edge : 0), x1 - (Has(WallOpen.TopRight) ? edge : 0), y1 - topCut, y1);
        }

        private static void Add(List<ShadowRect> result, double x0, double x1, double y0, double y1)
        {
            if (x1 - x0 > 1e-9 && y1 - y0 > 1e-9) result.Add(new ShadowRect { X = x0, Y = y0, Width = x1 - x0, Height = y1 - y0, SelfShadow = true });
        }
    }
}
