using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Remains.RoomFormat;

namespace Remains.RoomConvert
{
    // Reads a hand-made draft (factory-*-geometry.json) and compares it with generated
    // geometry through the shared RoomCoverage rule. Explicit ladder-landing records are
    // excluded: they are reviewed data, not something the grid must produce.
    public static class GeometryComparison
    {
        public static bool Compare(RoomGeometry geometry, RoomFile room, string draftFile, string landingsFile, int sourceIndex)
        {
            var draft = JsonText.Parse(File.ReadAllText(draftFile, Encoding.UTF8), draftFile);
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            if (landingsFile != null)
            {
                var landings = JsonText.Parse(File.ReadAllText(landingsFile, Encoding.UTF8), landingsFile);
                foreach (var record in landings.Get("records").Items)
                    if (record.Get("sourceRoom").AsLong("sourceRoom") == sourceIndex)
                    {
                        var s = record.Get("surface");
                        excluded.Add(Key(D(s, "x"), D(s, "y"), D(s, "width"), D(s, "height")));
                    }
            }
            // Older drafts predate movement sound banks: then sound is not compared at all.
            bool withSound = draft.Get("platforms").Items.Any(p => p.Get("movementSound") != null);
            if (!withSound) Console.WriteLine("В черновике нет movementSound: звук шагов не сравнивается.");
            var expected = new List<CoverageRect>();
            int skipped = 0;
            foreach (var p in draft.Get("platforms").Items)
            {
                double x = D(p, "x"), y = D(p, "y"), w = D(p, "width"), h = D(p, "height");
                if (excluded.Contains(Key(x, y, w, h))) { skipped++; continue; }
                expected.Add(new CoverageRect
                {
                    X = x, Y = y, Width = w, Height = h,
                    OneWay = p.Get("oneWay")?.Bool ?? false,
                    Slope = (int)(p.Get("slopeDirection")?.AsLong("slopeDirection") ?? 0),
                    Sound = withSound ? p.Get("movementSound")?.AsString("movementSound") ?? "" : null,
                });
            }
            var ladders = draft.Get("ladders").Items.Select(l => new CoverageLadder
            {
                X = D(l, "x"), Bottom = D(l, "bottom"), Top = D(l, "top"),
                Facing = (int)l.Get("facing").AsLong("facing"), MovementOffset = D(l, "movementOffset"),
            });
            var result = RoomCoverage.Compare(expected, RoomCoverage.From(geometry, withSound), ladders, RoomCoverage.Ladders(geometry), room.Cell);
            Console.WriteLine($"Сверка по клеткам: ожидалось {result.ExpectedCells}, получено {result.ActualCells}, совпало {result.MatchedCells}; лестниц {result.ExpectedLadders}/{result.ActualLadders}; исключено явных площадок: {skipped}.");
            foreach (string d in result.Differences.Take(30)) Console.WriteLine("  " + d);
            if (result.Differences.Count > 30) Console.WriteLine($"  … ещё {result.Differences.Count - 30}");
            return result.Equal;
        }

        private static double D(JsonValue v, string key) => v.Get(key)?.AsDouble(key) ?? 0;
        private static string Key(double x, double y, double w, double h)
            => string.Join(",", new[] { x, y, w, h }.Select(v => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture)));
    }
}
