using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Remains.RoomFormat
{
    public struct CoverageRect
    {
        public double X, Y, Width, Height;
        public bool OneWay;
        public int Slope;
        public string Sound;      // null: not compared
    }

    public struct CoverageLadder
    {
        public double X, Bottom, Top, MovementOffset;
        public int Facing;
    }

    public sealed class CoverageResult
    {
        public int ExpectedCells, ActualCells, MatchedCells, ExpectedLadders, ActualLadders;
        public readonly List<string> Differences = new List<string>();
        public bool Equal => Differences.Count == 0;
    }

    // Compares two rectangle sets by the cells they cover and the properties of each cell,
    // independent of how runs were merged. One implementation for the converter tool and
    // the Unity checks (rule: one implementation per shared rule).
    public static class RoomCoverage
    {
        public static CoverageResult Compare(IEnumerable<CoverageRect> expected, IEnumerable<CoverageRect> actual,
            IEnumerable<CoverageLadder> expectedLadders, IEnumerable<CoverageLadder> actualLadders, double cell)
        {
            var result = new CoverageResult();
            var want = Cells(expected, cell, "ожидаемое", result);
            var have = Cells(actual, cell, "полученное", result);
            result.ExpectedCells = want.Count;
            result.ActualCells = have.Count;
            foreach (var entry in want)
            {
                if (!have.TryGetValue(entry.Key, out string value)) result.Differences.Add($"нет {entry.Key}: ожидалось [{entry.Value}]");
                else if (value != entry.Value) result.Differences.Add($"различие {entry.Key}: ожидалось [{entry.Value}], получено [{value}]");
                else result.MatchedCells++;
            }
            foreach (var entry in have)
                if (!want.ContainsKey(entry.Key)) result.Differences.Add($"лишнее {entry.Key}: [{entry.Value}]");

            var a = expectedLadders.Select(Ladder).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var b = actualLadders.Select(Ladder).OrderBy(k => k, StringComparer.Ordinal).ToList();
            result.ExpectedLadders = a.Count;
            result.ActualLadders = b.Count;
            foreach (string l in a.Except(b)) result.Differences.Add("нет лестницы " + l);
            foreach (string l in b.Except(a)) result.Differences.Add("лишняя лестница " + l);
            return result;
        }

        private static Dictionary<string, string> Cells(IEnumerable<CoverageRect> rects, double cell, string side, CoverageResult result)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in rects)
            {
                int x0 = (int)Math.Round(r.X / cell), x1 = (int)Math.Round((r.X + r.Width) / cell);
                int y0 = (int)Math.Round(r.Y / cell), y1 = (int)Math.Ceiling((r.Y + r.Height) / cell - 1e-6);
                if (Math.Abs(r.X / cell - x0) > 1e-4 || Math.Abs(r.Y / cell - y0) > 1e-4)
                {
                    result.Differences.Add($"{side}: прямоугольник не по сетке клеток x={Round(r.X)} y={Round(r.Y)} w={Round(r.Width)} h={Round(r.Height)}");
                    continue;
                }
                string kind = r.Slope != 0 ? "ступень" + (r.Slope > 0 ? "+" : "-") : r.OneWay ? "односторонняя" : "твёрдая";
                for (int cy = y0; cy < Math.Max(y1, y0 + 1); cy++)
                {
                    double top = Math.Min(r.Y + r.Height, (cy + 1) * cell) - cy * cell;
                    string value = $"{kind} h={Round(top)}" + (r.Sound == null ? "" : " звук=" + r.Sound);
                    for (int cx = x0; cx < x1; cx++)
                    {
                        string key = $"({cx},{cy})/{kind}";
                        if (map.ContainsKey(key)) { result.Differences.Add($"{side}: клетка {key} покрыта дважды"); continue; }
                        map.Add(key, value);
                    }
                }
            }
            return map;
        }

        private static string Round(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
        private static string Ladder(CoverageLadder l) => $"x={Round(l.X)} {Round(l.Bottom)}..{Round(l.Top)} facing={l.Facing} offset={Round(l.MovementOffset)}";

        public static IEnumerable<CoverageRect> From(RoomGeometry geometry, bool withSound)
            => geometry.Surfaces.Select(s => new CoverageRect { X = s.X, Y = s.Y, Width = s.Width, Height = s.Height, OneWay = s.OneWay, Slope = s.Slope, Sound = withSound ? s.MovementSound ?? "" : null });

        public static IEnumerable<CoverageLadder> Ladders(RoomGeometry geometry)
            => geometry.Ladders.Select(l => new CoverageLadder { X = l.X, Bottom = l.Bottom, Top = l.Top, Facing = l.Facing, MovementOffset = l.MovementOffset });
    }
}
