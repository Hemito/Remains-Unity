using System;
using System.Collections.Generic;
using System.Linq;

namespace Remains.RoomFormat
{
    // One room of a factory as placed in the level grid.
    public sealed class FactoryRoomPlacement
    {
        public FactoryCell Cell;
        public RoomFile Template;
        public int Column, Row;                // top-left cell of the room in the level grid (rows top down)
    }

    // A cut passage: level cells whose solid was removed, and per side whether that room's own edge had a
    // wall there (the original then puts a sign post at the opening, Location.setDoor).
    public sealed class FactoryPassage
    {
        public FactorySeam Seam;
        public readonly List<(int X, int Row)> Cells = new List<(int, int)>();
        public bool FirstBrokeWall, SecondBrokeWall;   // first = left/upper room, second = right/lower
    }

    // The composed level grid of a generated factory (docs/pipelines/room-format.md §4.4, decision
    // 2026-10-10: walls, floors, ladders and light occluders are built from it at level preparation).
    // Level is an ordinary RoomFile, so RoomGeometryBuilder, RoomTerrainShapes and RoomShadowShapes
    // read it unchanged, with real neighbours across seams. Its palettes are keyed by definition ID;
    // per-room options (background, colour, darkness) stay with the placements.
    public sealed class FactoryGrid
    {
        public RoomFile Level;
        public int StepX, StepY;
        public FactoryLayout Layout;
        public readonly List<FactoryRoomPlacement> Rooms = new List<FactoryRoomPlacement>();
        public readonly List<FactoryPassage> Passages = new List<FactoryPassage>();
        public readonly List<string> Issues = new List<string>();
        public bool[,] Border;                 // [x, row]: room edge wall turned into the land border (indestructible)

        // §4.4: a shared column belongs to the left room, a shared row to the lower one, a corner to the lower left.
        public FactoryRoomPlacement Owner(int x, int row)
        {
            int cx = x == 0 ? 0 : (x - 1) / StepX, cy = Math.Min(row / StepY, Layout.Depth - 1);
            return Rooms[cx * Layout.Depth + cy];
        }
    }

    public static class FactoryGridBuilder
    {
        private static readonly string[] IdLayers = { RoomLayers.Solid, RoomLayers.Back, RoomLayers.Ladder, RoomLayers.Platform, RoomLayers.Stairs };
        private static readonly string[] Structural = { RoomLayers.Ladder, RoomLayers.Platform, RoomLayers.Stairs };

        // borderSolid: land border form (Location.mainFrame: options.border of the land, "A" by default in the
        // original code); its BackForm is the border's back wall. closeOuter: the outer edge of the factory becomes border wall too (our rule: the
        // original's openings at the land edge led nowhere); transitions are objects, not edge openings.
        public static FactoryGrid Build(FactoryLayout layout, IReadOnlyDictionary<string, RoomFile> templates, RoomDefinitionCatalog definitions,
            string borderSolid, bool closeOuter)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var border = definitions.Require(borderSolid, "граница земли");
            if (border.Kind != RoomDefinitionKind.Solid) throw new FormatException($"Граница земли {borderSolid}: вид {border.Kind}, нужен Solid.");
            string borderBack = border.BackForm == null ? null : definitions.Require(border.BackForm, "задник границы земли").Id;
            var first = Template(templates, layout.Cell(0, 0));
            int w = first.Width, h = first.Height;
            var grid = new FactoryGrid { Layout = layout, StepX = w - 1, StepY = h - 1 };
            int width = layout.Width * grid.StepX + 1, height = layout.Depth * grid.StepY + 1;

            // Placed rooms: definition IDs per layer after mirroring, [x, row] in room cells.
            var placed = new List<Dictionary<string, string[,]>>();
            var fixedLayers = new List<Dictionary<string, char[,]>>();
            for (int cx = 0; cx < layout.Width; cx++)
                for (int cy = 0; cy < layout.Depth; cy++)
                {
                    var cell = layout.Cell(cx, cy);
                    var room = Template(templates, cell);
                    if (room.Width != w || room.Height != h) throw new FormatException($"{room.Template}: размер {room.Width}×{room.Height}, у завода {w}×{h}.");
                    if (Math.Abs(room.Cell - first.Cell) > 1e-9) throw new FormatException($"{room.Template}: клетка {room.Cell}, у завода {first.Cell}.");
                    if (cell.Back != null) grid.Issues.Add($"Клетка {cx},{cy} ({room.Template}): комната заднего слоя {cell.Back} в сетку уровня пока не входит.");
                    grid.Rooms.Add(new FactoryRoomPlacement { Cell = cell, Template = room, Column = cx * grid.StepX, Row = cy * grid.StepY });
                    placed.Add(Place(room, cell.Mirror, definitions));
                    fixedLayers.Add(PlaceFixed(room, cell.Mirror));
                }

            // Level layers as IDs; each level cell is covered by 1, 2 (seam) or 4 (corner) rooms.
            var ids = IdLayers.ToDictionary(l => l, l => new string[width, height], StringComparer.Ordinal);
            var heights = new char[width, height];
            var water = new bool[width, height];
            var conflicts = new List<string>();
            int conflictCount = 0;
            for (int x = 0; x < width; x++)
                for (int row = 0; row < height; row++)
                {
                    var owner = grid.Owner(x, row);
                    var covering = Covering(grid, x, row);
                    int ownerIndex = grid.Rooms.IndexOf(owner);
                    covering.Remove(ownerIndex);
                    covering.Insert(0, ownerIndex);   // owner first, then the others in layout order
                    bool solid = false;
                    foreach (int i in covering) if (Local(placed[i], RoomLayers.Solid, grid.Rooms[i], x, row) != null) { solid = true; break; }
                    foreach (string layer in IdLayers)
                    {
                        string chosen = null;
                        foreach (int i in covering)
                        {
                            string v = Local(placed[i], layer, grid.Rooms[i], x, row);
                            if (v == null) continue;
                            if (chosen == null) chosen = v;
                            else if (v != chosen && !solid)   // a solid shared cell becomes the border below
                            {
                                conflictCount++;
                                if (conflicts.Count < 8) conflicts.Add($"{x},{row} {layer}: {chosen} ({grid.Rooms[covering[0]].Template.Template}) / {v} ({grid.Rooms[i].Template.Template})");
                            }
                        }
                        ids[layer][x, row] = chosen;
                    }
                    char hc = RoomFile.Empty;
                    foreach (int i in covering)
                    {
                        var room = grid.Rooms[i];
                        char c = fixedLayers[i][RoomLayers.Height][x - room.Column, row - room.Row];
                        if (c != RoomFile.Empty) { hc = c; break; }
                    }
                    foreach (int i in covering)
                    {
                        var room = grid.Rooms[i];
                        if (fixedLayers[i][RoomLayers.Water][x - room.Column, row - room.Row] != RoomFile.Empty) water[x, row] = true;
                    }
                    heights[x, row] = hc;
                }
            if (conflictCount > 0)
                grid.Issues.Add($"Общие клетки стыков: {conflictCount} расхождений слоёв без стены, взято значение владельца (§4.4): {string.Join("; ", conflicts)}{(conflictCount > conflicts.Count ? "; …" : "")}.");

            // Passages (Location.setDoor): each side clears its edge cell and the next one inward.
            foreach (var seam in layout.Seams) grid.Passages.Add(Cut(grid, seam, ids, placed));
            foreach (var p in grid.Passages)
                foreach (var (x, row) in p.Cells)
                {
                    ids[RoomLayers.Solid][x, row] = null;
                    if (ids[RoomLayers.Platform][x, row] == null && ids[RoomLayers.Stairs][x, row] == null) heights[x, row] = RoomFile.Empty;
                }

            // Location.mainFrame: a solid cell on a room edge becomes the land border (indestructible,
            // its own back, no ladder or stairs). With closeOuter the outer edge is closed first.
            grid.Border = new bool[width, height];
            for (int x = 0; x < width; x++)
                for (int row = 0; row < height; row++)
                {
                    bool edge = x % grid.StepX == 0 || row % grid.StepY == 0;
                    bool outer = x == 0 || row == 0 || x == width - 1 || row == height - 1;
                    if (!edge || ids[RoomLayers.Solid][x, row] == null && !(closeOuter && outer)) continue;
                    grid.Border[x, row] = true;
                    ids[RoomLayers.Solid][x, row] = borderSolid;
                    ids[RoomLayers.Back][x, row] = borderBack;
                    foreach (string layer in Structural) ids[layer][x, row] = null;
                    heights[x, row] = RoomFile.Empty;
                }

            grid.Level = Compose(layout, first.Cell, width, height, ids, heights, water);
            return grid;
        }

        // LandAct.border: the original default when the land has no options.border.
        public const string OriginalBorderDefault = "A";

        // Border solid of a land's random levels: "random_<land>" options.border, the original default otherwise.
        public static string BorderSolid(RoomLandCatalog lands, string land, string solidPrefix = "remains:solid:")
        {
            RoomLand entry = null;
            foreach (var l in lands.Lands) if (l.Id == "random_" + land) entry = l;
            if (entry == null) throw new KeyNotFoundException($"Нет земли random_{land} в каталоге земель.");
            return solidPrefix + (entry.Options.TryGetValue("border", out string b) ? b : OriginalBorderDefault);
        }

        private static RoomFile Template(IReadOnlyDictionary<string, RoomFile> templates, FactoryCell cell) =>
            templates.TryGetValue(cell.Template, out var room) ? room : throw new KeyNotFoundException($"Клетка {cell.X},{cell.Y}: нет шаблона {cell.Template}.");

        private static List<int> Covering(FactoryGrid grid, int x, int row)
        {
            var result = new List<int>(4);
            int depth = grid.Layout.Depth;
            for (int cx = Math.Max(0, (x - 1) / grid.StepX); cx <= Math.Min(grid.Layout.Width - 1, x / grid.StepX); cx++)
                for (int cy = Math.Max(0, (row - 1) / grid.StepY); cy <= Math.Min(depth - 1, row / grid.StepY); cy++)
                {
                    var room = grid.Rooms[cx * depth + cy];
                    if (x >= room.Column && x <= room.Column + grid.StepX && row >= room.Row && row <= room.Row + grid.StepY) result.Add(cx * depth + cy);
                }
            return result;
        }

        private static string Local(Dictionary<string, string[,]> room, string layer, FactoryRoomPlacement p, int x, int row) => room[layer][x - p.Column, row - p.Row];

        // Mirroring (Location constructor + Tile.dec): columns reversed, directional forms replaced by their pair.
        private static Dictionary<string, string[,]> Place(RoomFile room, bool mirror, RoomDefinitionCatalog definitions)
        {
            var result = new Dictionary<string, string[,]>(StringComparer.Ordinal);
            foreach (string layer in IdLayers)
            {
                var cells = new string[room.Width, room.Height];
                result.Add(layer, cells);
                if (!room.Layers.TryGetValue(layer, out var rows)) continue;
                room.Palettes.TryGetValue(layer, out var palette);
                var kind = KindOf(layer);
                for (int row = 0; row < room.Height; row++)
                    for (int sx = 0; sx < room.Width; sx++)
                    {
                        char key = rows[row][sx];
                        if (key == RoomFile.Empty) continue;
                        string ctx = $"{room.Template} слой {layer} клетка {sx},{room.Height - 1 - row}";
                        if (palette == null || !palette.TryGetValue(key, out string id)) throw new FormatException($"{ctx}: ключ «{key}» не в палитре.");
                        var definition = definitions.Require(id, ctx);
                        if (definition.Kind != kind) throw new FormatException($"{ctx}: {id} вида {definition.Kind}, в слое ожидается {kind}.");
                        if (mirror && (definition.Slope != 0 || definition.Facing != 0))
                        {
                            if (definition.MirrorForm == null) throw new FormatException($"{ctx}: у направленной формы {id} нет зеркальной пары (mirrorForm).");
                            id = definitions.Require(definition.MirrorForm, ctx + " (зеркало)").Id;
                        }
                        cells[mirror ? room.Width - 1 - sx : sx, row] = id;
                    }
            }
            return result;
        }

        private static Dictionary<string, char[,]> PlaceFixed(RoomFile room, bool mirror)
        {
            var result = new Dictionary<string, char[,]>(StringComparer.Ordinal);
            foreach (string layer in new[] { RoomLayers.Height, RoomLayers.Water })
            {
                var cells = new char[room.Width, room.Height];
                room.Layers.TryGetValue(layer, out var rows);
                for (int row = 0; row < room.Height; row++)
                    for (int sx = 0; sx < room.Width; sx++)
                        cells[mirror ? room.Width - 1 - sx : sx, row] = rows == null ? RoomFile.Empty : rows[row][sx];
                result.Add(layer, cells);
            }
            return result;
        }

        private static RoomDefinitionKind KindOf(string layer)
        {
            switch (layer)
            {
                case RoomLayers.Solid: return RoomDefinitionKind.Solid;
                case RoomLayers.Back: return RoomDefinitionKind.Backwall;
                case RoomLayers.Ladder: return RoomDefinitionKind.Ladder;
                case RoomLayers.Platform: return RoomDefinitionKind.Platform;
                default: return RoomDefinitionKind.Stairs;
            }
        }

        // Slots (room-format §3.2) in the frame of the placed left/upper room: right 0..5 at rows 4s+3 from the
        // top (2 rows, 3 when wider than 2); down 6..10 at columns 9(s-6)+4 (+1,+2; also +0,+3 when wider).
        // Each room clears its edge cell and the next one inward; with shared edges that is 3 level cells deep.
        private static FactoryPassage Cut(FactoryGrid grid, FactorySeam seam, Dictionary<string, string[,]> ids, List<Dictionary<string, string[,]>> placed)
        {
            var passage = new FactoryPassage { Seam = seam };
            int depth = grid.Layout.Depth;
            if (seam.Down ? seam.Y + 1 >= depth || seam.Slot < 6 || seam.Slot > 10 : seam.X + 1 >= grid.Layout.Width || seam.Slot < 0 || seam.Slot > 5)
                throw new FormatException($"Стык {seam.X},{seam.Y} {(seam.Down ? "вниз" : "вправо")} слот {seam.Slot}: вне завода или неверный слот.");
            int a = seam.X * depth + seam.Y, b = seam.Down ? a + 1 : a + depth;
            var first = grid.Rooms[a]; var second = grid.Rooms[b];
            if (!seam.Down)
            {
                int top = 4 * seam.Slot + 3, shared = second.Column;
                var rows = seam.Width > 2 ? new[] { top, top - 1, top - 2 } : new[] { top, top - 1 };
                foreach (int r in rows)
                {
                    int row = first.Row + r;
                    passage.FirstBrokeWall |= placed[a][RoomLayers.Solid][grid.StepX, r] != null;
                    passage.SecondBrokeWall |= placed[b][RoomLayers.Solid][0, r] != null;
                    for (int x = shared - 1; x <= shared + 1; x++) passage.Cells.Add((x, row));
                }
            }
            else
            {
                int left = 9 * (seam.Slot - 6) + 4, shared = second.Row;
                var cols = seam.Width > 2 ? new[] { left, left + 1, left + 2, left + 3 } : new[] { left + 1, left + 2 };
                foreach (int c in cols)
                {
                    int x = first.Column + c;
                    passage.FirstBrokeWall |= placed[a][RoomLayers.Solid][c, grid.StepY] != null;
                    passage.SecondBrokeWall |= placed[b][RoomLayers.Solid][c, 0] != null;
                    for (int row = shared - 1; row <= shared + 1; row++) passage.Cells.Add((x, row));
                }
            }
            return passage;
        }

        private const string KeyAlphabet = "#ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!$%&'()*+,-/:;<=>?@[]^_`{|}~";

        private static RoomFile Compose(FactoryLayout layout, double cell, int width, int height,
            Dictionary<string, string[,]> ids, char[,] heights, bool[,] water)
        {
            var level = new RoomFile { Template = $"factory:{layout.Land}:{layout.Index}", Width = width, Height = height, Cell = cell };
            foreach (string layer in IdLayers)
            {
                var used = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string id in ids[layer]) if (id != null) used.Add(id);
                if (used.Count == 0) continue;
                if (used.Count > KeyAlphabet.Length) throw new FormatException($"Слой {layer}: {used.Count} определений, ключей только {KeyAlphabet.Length}.");
                var palette = level.Palette(layer);
                var keys = new Dictionary<string, char>(StringComparer.Ordinal);
                int k = 0;
                foreach (string id in used) { keys.Add(id, KeyAlphabet[k]); palette.Add(KeyAlphabet[k++], id); }
                level.Layers.Add(layer, Rows(width, height, (x, row) => ids[layer][x, row] == null ? RoomFile.Empty : keys[ids[layer][x, row]]));
            }
            level.Layers.Add(RoomLayers.Height, Rows(width, height, (x, row) => heights[x, row]));
            level.Layers.Add(RoomLayers.Water, Rows(width, height, (x, row) => water[x, row] ? '~' : RoomFile.Empty));
            return level;
        }

        private static string[] Rows(int width, int height, Func<int, int, char> at)
        {
            var rows = new string[height];
            var line = new char[width];
            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++) line[x] = at(x, row);
                rows[row] = new string(line);
            }
            return rows;
        }
    }
}
