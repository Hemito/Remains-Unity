using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Remains.RoomFormat
{
    // Factory generator after the original Land.buildRandomLand for the plant (conf 0), reshaped by the
    // user decisions of 2026-10-10 (docs/research/factory-generation.md):
    //  - one persistent factory per index; the original stages are stacked downwards: band 0 is 3 rows,
    //    every later band 6 rows (4×21 = all four original stages), the allowed room level is the band
    //    (capped at 3), the room budget (kol: 2, uniq 1) resets per band as each original stage did;
    //  - repeats are acceptable: when the band's budget runs out, rooms of the allowed level repeat;
    //  - only the entry cell 0,0 is fixed: beg0, unmirrored (user 2026-11-06, the original fixed the whole top row and every
    //    factory opened alike); other cells mirrored with 50 %;
    //  - a room this factory has not used yet weighs UnseenPriority times more (user 2026-11-06): the budget resets per band,
    //    so without it about nine of the fifty rooms stayed unused while others came four or five times;
    //  - passages: right — three random picks among matching slots 0..5, down — one among 6..10;
    //  - roles on the checkerboard: even cells — checkpoint, at the bottom of a band an exit; odd cells —
    //    a trial door, required at the bottom of a band, otherwise with 30 %.
    // Randomness: own SplitMix64 seeded from (world seed, land, index, generator version); System.Random
    // differs between runtimes. Changing any rule bumps GeneratorVersion.
    public static class FactoryLayoutGenerator
    {
        public const int GeneratorVersion = 3; // 3: only the entry cell is fixed, unused rooms first. 2: persist the selected marker for every cell role
        public const int OldestStoredVersion = 2; // A saved layout is read, never regenerated; version 1 has no role markers.
        public const int FirstBandRows = 3, BandRows = 6, MaxRoomLevel = 3, UnseenPriority = 8, MaxAttempts = 64;
        private static readonly HashSet<string> NotRandomTips = new HashSet<string>(StringComparer.Ordinal)
            { "beg0", "back", "roof", "pass", "passroof", "roofpass", "vert", "surf" };

        private sealed class Candidate
        {
            public RoomFile Room; public string Tip; public int Level, Budget; public bool Random;
            public int[] Connectors;
        }

        public static int BandOfRow(int row) => row < FirstBandRows ? 0 : 1 + (row - FirstBandRows) / BandRows;
        public static bool BandBottom(int row, int depth) => row == depth - 1 || (row >= FirstBandRows - 1 && (row - (FirstBandRows - 1)) % BandRows == 0);

        public static FactoryLayout Generate(IReadOnlyList<RoomFile> rooms, string land, ulong worldSeed, int index, int width, int depth)
        {
            if (rooms == null || rooms.Count == 0) throw new ArgumentException("Нет комнат для генерации завода.");
            if (width < 1 || depth < 1) throw new ArgumentException($"Неверный размер завода {width}×{depth}.");
            var random = new LayoutRandom(Mix(worldSeed, land, index));
            // The entry has one exit, to the right: about a fifth of the layouts cut it off from the rest (2026-11-06, measured
            // on the rooms of the plant). The next attempt continues the same random stream, so the result stays reproducible.
            FactoryLayout best = null; int fewestLost = int.MaxValue;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var layout = Attempt(rooms, land, worldSeed, index, width, depth, random, out int lost);
                if (lost < fewestLost) { best = layout; fewestLost = lost; }
                if (lost == 0) return layout;
            }
            return best;   // Its issues name the cut cells; the warning goes out when the factory is prepared.
        }

        private static FactoryLayout Attempt(IReadOnlyList<RoomFile> rooms, string land, ulong worldSeed, int index, int width, int depth, LayoutRandom random, out int lost)
        {
            var layout = new FactoryLayout { GeneratorVersion = GeneratorVersion, Land = land, Seed = worldSeed, Index = index, Width = width, Depth = depth };
            var all = rooms.Select(r => Describe(r, layout.Issues)).OrderBy(c => c.Room.Template, StringComparer.Ordinal).ToList();
            var bySource = all.Where(c => c.Room.Source?.Room != null).GroupBy(c => c.Room.Source.Room, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var grid = new Candidate[width, depth];
            var connectors = new int[width, depth][];
            var budget = new Dictionary<Candidate, int>();
            var used = new HashSet<Candidate>();
            int currentBand = -1;
            for (int y = 0; y < depth; y++)
            {
                int band = BandOfRow(y);
                if (band != currentBand) { currentBand = band; budget.Clear(); foreach (var c in all) budget[c] = c.Budget; }
                int maxLevel = Math.Min(band, MaxRoomLevel);
                for (int x = 0; x < width; x++)
                {
                    var cell = new FactoryCell { X = x, Y = y, MaxLevel = maxLevel, Band = band };
                    Candidate chosen = null;
                    if (y == 0 && x == 0) chosen = all.FirstOrDefault(c => c.Tip == "beg0");
                    if (chosen != null) { cell.Start = true; budget[chosen]--; }
                    else
                    {
                        var left = x > 0 ? grid[x - 1, y] : null; var up = y > 0 ? grid[x, y - 1] : null;
                        var pool = new List<(Candidate room, int weight)>();
                        foreach (var c in all)
                        {
                            if (!c.Random || c.Level > maxLevel || budget[c] <= 0 || c == left || c == up) continue;
                            int weight = budget[c] * budget[c];
                            if (weight == 4 && c.Level == 0 && maxLevel > 1) weight = 2;
                            if (!used.Contains(c)) weight *= UnseenPriority;
                            pool.Add((c, weight));
                        }
                        if (pool.Count == 0)
                        {
                            // Budget exhausted: repeat a room of the allowed level (user decision: repeats are fine).
                            foreach (var c in all) if (c.Random && c.Level <= maxLevel && c != left && c != up) pool.Add((c, 1));
                            cell.Repeat = true;
                        }
                        if (pool.Count == 0) throw new InvalidOperationException($"Нет ни одной комнаты уровня ≤ {maxLevel} для клетки {x},{y}.");
                        chosen = Pick(pool, random);
                        budget[chosen]--;
                    }
                    used.Add(chosen);
                    cell.Template = chosen.Room.Template; cell.Level = chosen.Level;
                    cell.Mirror = !cell.Start && random.Unit() < 0.5;
                    if (chosen.Room.Options.TryGetValue("back", out string back))
                    {
                        if (bySource.TryGetValue(back, out var behind)) cell.Back = behind.Room.Template;
                        else layout.Issues.Add($"Клетка {x},{y}: {chosen.Room.Template} ссылается на задний слой «{back}», такой комнаты нет.");
                    }
                    grid[x, y] = chosen;
                    connectors[x, y] = cell.Mirror ? Mirror(chosen.Connectors) : chosen.Connectors;
                    layout.Cells.Add(cell);
                }
            }
            // Cells were added row-major; store column-major so Cell(x, y) = Cells[x * depth + y].
            var ordered = layout.Cells.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
            layout.Cells.Clear(); layout.Cells.AddRange(ordered);

            for (int x = 0; x < width; x++)
                for (int y = 0; y < depth; y++)
                {
                    var a = connectors[x, y];
                    if (x < width - 1)
                    {
                        var b = connectors[x + 1, y];
                        var options = Enumerable.Range(0, 6).Where(s => Math.Min(a[s], b[s + 11]) >= 2).ToList();
                        var picked = new SortedSet<int>();
                        if (options.Count > 0) for (int i = 0; i < 3; i++) picked.Add(options[random.Range(options.Count)]);
                        foreach (int s in picked) layout.Seams.Add(new FactorySeam { X = x, Y = y, Down = false, Slot = s, Width = Math.Min(a[s], b[s + 11]) });
                    }
                    if (y < depth - 1)
                    {
                        var b = connectors[x, y + 1];
                        // The original loop also tests slot 11 against the non-existent 22: never a match.
                        var options = Enumerable.Range(6, 5).Where(s => Math.Min(a[s], b[s + 11]) >= 2).ToList();
                        if (options.Count > 0)
                        {
                            int s = options[random.Range(options.Count)];
                            layout.Seams.Add(new FactorySeam { X = x, Y = y, Down = true, Slot = s, Width = Math.Min(a[s], b[s + 11]) });
                        }
                    }
                }

            for (int y = depth - 1; y >= 0; y--)
                for (int x = 0; x < width; x++)
                {
                    var cell = layout.Cell(x, y);
                    bool bottom = BandBottom(y, depth);
                    if ((x + y) % 2 == 0) cell.Role = bottom ? FactoryCellRole.Exit : FactoryCellRole.Checkpoint;
                    else if (bottom) { cell.Role = FactoryCellRole.Trial; cell.RoleRequired = true; }
                    else if (random.Unit() < 0.3) cell.Role = FactoryCellRole.Trial;
                }
            foreach(var cell in layout.Cells)
            {
                if(cell.Role==FactoryCellRole.None) continue;
                var points=grid[cell.X,cell.Y].Room.Objects.Where(obj=>obj.Type=="remains:obj:player")
                    .OrderBy(obj=>obj.Id,StringComparer.Ordinal).ToArray();
                if(points.Length==0 || points.Any(obj=>string.IsNullOrWhiteSpace(obj.Id)) ||
                    points.Select(obj=>obj.Id).Distinct(StringComparer.Ordinal).Count()!=points.Length)
                    throw new InvalidOperationException($"Клетка {cell.X},{cell.Y}, {cell.Template}, роль {cell.Role}: нет однозначных постоянных ID исходных точек появления.");
                cell.RolePoint=points[random.Range(points.Length)].Id;
            }
            lost = Reachability(layout);
            return layout;
        }

        private static Candidate Describe(RoomFile room, List<string> issues)
        {
            room.Options.TryGetValue("tip", out string tip); tip ??= "";
            int level = 0;
            if (room.Options.TryGetValue("level", out string levelText) && !int.TryParse(levelText, out level))
                issues.Add($"{room.Template}: нечисловой options.level «{levelText}», принят 0.");
            bool uniq = tip == "uniq" || room.Options.ContainsKey("uniq");
            // As the original Room: only beg0 among the start rooms is excluded; beg1..beg3 may also appear below.
            bool random = !NotRandomTips.Contains(tip) && !room.Options.ContainsKey("nornd");
            int[] c = room.Connectors.Length == 22 ? room.Connectors : Enumerable.Repeat(2, 22).ToArray();   // no data: open everywhere, as the original
            if (room.Connectors.Length != 0 && room.Connectors.Length != 22) issues.Add($"{room.Template}: {room.Connectors.Length} разъёмов вместо 22, приняты открытыми.");
            return new Candidate { Room = room, Tip = tip, Level = level, Budget = uniq ? 1 : 2, Random = random, Connectors = c };
        }

        // Location constructor: a mirrored room swaps its right and left edge slots and reverses the order
        // of the bottom and top slots.
        public static int[] Mirror(int[] c)
        {
            var m = (int[])c.Clone();
            for (int i = 0; i <= 5; i++) { m[i] = c[i + 11]; m[i + 11] = c[i]; }
            (m[6], m[10]) = (c[10], c[6]); (m[7], m[9]) = (c[9], c[7]);
            (m[17], m[21]) = (c[21], c[17]); (m[18], m[20]) = (c[20], c[18]);
            return m;
        }

        private static Candidate Pick(List<(Candidate room, int weight)> pool, LayoutRandom random)
        {
            int total = pool.Sum(p => p.weight), roll = random.Range(total);
            foreach (var p in pool) { if (roll < p.weight) return p.room; roll -= p.weight; }
            return pool[pool.Count - 1].room;
        }

        // Cells not connected to the entry through cut passages: the number of them. Passages only: whether a room can be
        // crossed inside is the room's own design (vertical travel may also use ladders inside rooms).
        private static int Reachability(FactoryLayout layout)
        {
            var open = new HashSet<(int, int)>();
            var queue = new Queue<(int x, int y)>();
            open.Add((0, 0)); queue.Enqueue((0, 0));
            var links = new Dictionary<(int, int), List<(int, int)>>();
            void Link((int, int) a, (int, int) b)
            {
                if (!links.TryGetValue(a, out var la)) links[a] = la = new List<(int, int)>(); la.Add(b);
                if (!links.TryGetValue(b, out var lb)) links[b] = lb = new List<(int, int)>(); lb.Add(a);
            }
            foreach (var s in layout.Seams) Link((s.X, s.Y), s.Down ? (s.X, s.Y + 1) : (s.X + 1, s.Y));
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (links.TryGetValue(c, out var next)) foreach (var n in next) if (open.Add(n)) queue.Enqueue(n);
            }
            var lost = layout.Cells.Where(c => !open.Contains((c.X, c.Y))).Select(c => $"{c.X},{c.Y}").ToList();
            if (lost.Count > 0) layout.Issues.Add("Нет прохода от входа к клеткам: " + string.Join(" ", lost) + ".");
            return lost.Count;
        }

        private static ulong Mix(ulong seed, string land, int index)
        {
            ulong h = 14695981039346656037UL;   // FNV-1a over the identity of this factory
            foreach (byte b in Encoding.UTF8.GetBytes($"{land}|{index}|{GeneratorVersion}")) { h ^= b; h *= 1099511628211UL; }
            return seed ^ h;
        }
    }

    // SplitMix64: small, fixed and identical on every runtime.
    public sealed class LayoutRandom
    {
        private ulong state;
        public LayoutRandom(ulong seed) { state = seed; }
        public ulong Next()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public int Range(int count) => count <= 0 ? throw new ArgumentOutOfRangeException(nameof(count)) : (int)(Next() % (ulong)count);
        public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));
    }
}
