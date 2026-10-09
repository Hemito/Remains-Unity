using System;

namespace Remains.RoomFormat
{
    // Corner shape states for drawing terrain (docs/pipelines/room-format.md §5, stage B3).
    // A cell is drawn as four quarters; each quarter takes one of five states from its two
    // orthogonal neighbours and the diagonal between them (rule observed in the original
    // renderer, implemented here from the grid):
    //   0 inside (both + diagonal filled), 1 inner corner (both filled, diagonal empty),
    //   2 vertical neighbour empty, 3 horizontal neighbour empty, 4 outer corner (both empty).
    // Mask frame = state + 1. Outside the room counts as filled (room-local until seams).
    public enum Quarter { TopLeft = 0, TopRight = 1, BottomLeft = 2, BottomRight = 3 }

    public sealed class RoomTerrainShapes
    {
        public readonly int Width, Height;
        private readonly sbyte[,,] front, back;   // [x, y, quarter]; -1 = not drawn

        private RoomTerrainShapes(int w, int h)
        {
            Width = w; Height = h;
            front = new sbyte[w, h, 4]; back = new sbyte[w, h, 4];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) for (int q = 0; q < 4; q++) { front[x, y, q] = -1; back[x, y, q] = -1; }
        }

        // Cells use y up (y = 0 bottom row), as everywhere in the room format.
        public int Front(int x, int y, Quarter q) => front[x, y, (int)q];
        public int Back(int x, int y, Quarter q) => back[x, y, (int)q];

        public static int State(bool vertical, bool horizontal, bool diagonal)
        {
            if (vertical && horizontal) return diagonal ? 0 : 1;
            if (horizontal) return 2;
            if (vertical) return 3;
            return 4;
        }

        public static RoomTerrainShapes Compute(RoomFile room)
        {
            var shapes = new RoomTerrainShapes(room.Width, room.Height);
            char At(string layer, int x, int y) => room.Layers.TryGetValue(layer, out var rows) ? rows[room.Height - 1 - y][x] : RoomFile.Empty;
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < room.Width && y < room.Height;
            bool Solid(int x, int y) => !Inside(x, y) || At(RoomLayers.Solid, x, y) != RoomFile.Empty;
            bool HasBack(int x, int y) => Inside(x, y) && At(RoomLayers.Back, x, y) != RoomFile.Empty;
            bool Shelf(int x, int y) => Inside(x, y) && At(RoomLayers.Platform, x, y) != RoomFile.Empty;
            bool skyBackdrop = room.Options.TryGetValue("backwall", out string backwall) && backwall == "sky";

            for (int x = 0; x < room.Width; x++)
                for (int y = 0; y < room.Height; y++)
                {
                    bool solid = At(RoomLayers.Solid, x, y) != RoomFile.Empty;
                    char ownBack = At(RoomLayers.Back, x, y);
                    if (solid)
                    {
                        Fill(shapes.front, x, y, (dx, dy) => Solid(x + dx, y + dy), (dx, dy) => Solid(x + dx, y + dy));
                        // Back behind a solid cell: orthogonals also accept a back wall or a beam; diagonals do not.
                        if (ownBack != RoomFile.Empty)
                            Fill(shapes.back, x, y, (dx, dy) => Solid(x + dx, y + dy) || HasBack(x + dx, y + dy) || Shelf(x + dx, y + dy), (dx, dy) => Solid(x + dx, y + dy));
                    }
                    else if (ownBack != RoomFile.Empty)
                    {
                        bool Same(int dx, int dy)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (!Inside(nx, ny)) return true;
                            char other = At(RoomLayers.Back, nx, ny);
                            return other == ownBack || skyBackdrop && other != RoomFile.Empty || Solid(nx, ny) || Shelf(nx, ny);
                        }
                        Fill(shapes.back, x, y, Same, Same);
                    }
                }
            return shapes;
        }

        // Neighbour offsets in y-up cells: TopLeft looks up (0,+1), left (-1,0), up-left (-1,+1), etc.
        private static void Fill(sbyte[,,] target, int x, int y, Func<int, int, bool> orthogonal, Func<int, int, bool> diagonal)
        {
            target[x, y, (int)Quarter.TopLeft] = (sbyte)State(orthogonal(0, 1), orthogonal(-1, 0), diagonal(-1, 1));
            target[x, y, (int)Quarter.TopRight] = (sbyte)State(orthogonal(0, 1), orthogonal(1, 0), diagonal(1, 1));
            target[x, y, (int)Quarter.BottomLeft] = (sbyte)State(orthogonal(0, -1), orthogonal(-1, 0), diagonal(-1, -1));
            target[x, y, (int)Quarter.BottomRight] = (sbyte)State(orthogonal(0, -1), orthogonal(1, 0), diagonal(1, -1));
        }
    }
}
