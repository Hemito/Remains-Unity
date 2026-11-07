using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Remains.RoomFormat;

namespace Remains.RoomConvert
{
    // grid-checks --definitions <definitions.json>: rules of FactoryGridBuilder on small synthetic factories
    // (room-format §4.4, Location.setDoor / mainFrame). Data rules only; Tilemap and timing are checked in Unity.
    public static class FactoryGridChecks
    {
        private const int W = 48, H = 25;
        private const string Steel = "remains:solid:A", Concrete = "remains:solid:C", Beam = "remains:beam:rusty";

        public static int Run(Dictionary<string, string> o, Func<Dictionary<string, string>, string, string> required)
        {
            var definitions = RoomDefinitionCatalog.Read(File.ReadAllText(required(o, "definitions"), Encoding.UTF8), required(o, "definitions"));
            int failed = 0;
            void Check(string name, Action body)
            {
                try { body(); Console.WriteLine("PASS " + name); }
                catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
            }
            Check("повтор шаблона и разные палитры дают одну клетку уровня", () =>
            {
                var a = Room("a", ('C', Concrete)); Set(a, RoomLayers.Solid, 10, 5, 'C');
                var b = Room("b", ('z', Concrete)); Set(b, RoomLayers.Solid, 10, 5, 'z');
                var grid = Build(definitions, 3, 1, new[] { "a", "b", "a" }, new[] { false, false, false }, a, b);
                for (int i = 0; i < 3; i++) Expect(Id(grid, RoomLayers.Solid, 47 * i + 10, 5) == Concrete, $"комната {i}: бетон в 10,5");
                Expect(grid.Level.Palettes[RoomLayers.Solid].Values.Count(v => v == Concrete) == 1, "один ключ палитры на определение");
            });
            Check("зеркало: столбцы и парные формы", () =>
            {
                var a = Room("a", ('L', "remains:ladder:right"), ('S', "remains:stairs:metal-rise-right"));
                Set(a, RoomLayers.Ladder, 5, 10, 'L'); Set(a, RoomLayers.Stairs, 7, 12, 'S');
                var grid = Build(definitions, 1, 1, new[] { "a" }, new[] { true }, a);
                Expect(Id(grid, RoomLayers.Ladder, 42, 10) == "remains:ladder:left", "лестница справа → слева в столбце 42");
                Expect(Id(grid, RoomLayers.Stairs, 40, 12) == "remains:stairs:metal-rise-left", "ступени / → \\ в столбце 40");
            });
            Check("стена любой стороны на общей линии → граница, проём обеих → проём", () =>
            {
                var a = Room("a", ('C', Concrete)); Set(a, RoomLayers.Solid, 47, 4, 'C');
                var b = Room("b");
                var grid = Build(definitions, 2, 1, new[] { "a", "b" }, new[] { false, false }, a, b);
                Expect(Id(grid, RoomLayers.Solid, 47, 4) == Steel && grid.Border[47, 4], "стена левой → граница (сталь)");
                Expect(Id(grid, RoomLayers.Back, 47, 4) == "remains:backwall:A", "задник границы — задник формы A");
                Expect(Id(grid, RoomLayers.Solid, 47, 6) == null, "открыто у обеих — открыто");
            });
            Check("угол четырёх комнат принадлежит нижней левой", () =>
            {
                var a = Room("a");
                var grid = Build(definitions, 2, 2, new[] { "a", "a", "a", "a" }, new[] { false, false, false, false }, a);
                Expect(grid.Owner(47, 24).Cell.X == 0 && grid.Owner(47, 24).Cell.Y == 1, "угол 47,24 → клетка 0,1");
                Expect(grid.Owner(47, 10).Cell.X == 0, "общий столбец → левая");
                Expect(grid.Owner(20, 24).Cell.Y == 1, "общая строка → нижняя");
            });
            Check("проход прорубает по две клетки каждой комнаты, пересекающиеся проходы объединяются", () =>
            {
                var a = Room("a", ('C', Concrete)); Fill(a, 'C');
                var grid = Build(definitions, 2, 2, new[] { "a", "a", "a", "a" }, new[] { false, false, false, false }, a,
                    seams: new[] { new FactorySeam { X = 0, Y = 0, Slot = 5, Width = 2 }, new FactorySeam { X = 0, Y = 0, Down = true, Slot = 10, Width = 3 } });
                foreach (int x in new[] { 46, 47, 48 }) foreach (int row in new[] { 22, 23 }) Expect(Id(grid, RoomLayers.Solid, x, row) == null, $"вправо: {x},{row} открыт");
                Expect(Id(grid, RoomLayers.Solid, 45, 23) != null && Id(grid, RoomLayers.Solid, 49, 23) != null, "вправо: глубже двух клеток стена");
                foreach (int x in new[] { 40, 41, 42, 43 }) foreach (int row in new[] { 23, 24, 25 }) Expect(Id(grid, RoomLayers.Solid, x, row) == null, $"вниз (ширина 3): {x},{row} открыт");
                Expect(Id(grid, RoomLayers.Solid, 41, 22) != null && Id(grid, RoomLayers.Solid, 41, 26) != null, "вниз: глубже двух клеток стена");
                Expect(Id(grid, RoomLayers.Solid, 47, 24) == Steel, "угол не в проходе остаётся границей");
                Expect(grid.Passages.All(p => p.FirstBrokeWall && p.SecondBrokeWall), "край был стеной у обеих сторон — указатель");
            });
            Check("внешний край закрыт границей", () =>
            {
                var a = Room("a");
                var grid = Build(definitions, 1, 1, new[] { "a" }, new[] { false }, a);
                Expect(grid.Border[0, 10] && grid.Border[47, 10] && grid.Border[10, 0] && grid.Border[10, 24], "все четыре стороны");
                Expect(!grid.Border[10, 10], "внутри не граница");
            });
            Check("лестница через горизонтальный стык — одна лестница", () =>
            {
                var a = Room("a", ('L', "remains:ladder:right"));
                for (int row = 18; row < H; row++) Set(a, RoomLayers.Ladder, 20, row, 'L');
                for (int row = 0; row < 6; row++) Set(a, RoomLayers.Ladder, 20, row, 'L');
                var grid = Build(definitions, 1, 2, new[] { "a", "a" }, new[] { false, false }, a,
                    seams: new[] { new FactorySeam { X = 0, Y = 0, Down = true, Slot = 8, Width = 2 } });
                var ladders = RoomGeometryBuilder.Build(grid.Level, definitions).Ladders.Where(l => l.X > 20 * 0.8 && l.X < 21 * 0.8).ToList();
                Expect(ladders.Count(l => l.Bottom < 25 * 0.8 && l.Top > 24 * 0.8 + 1) == 1, $"лестниц через стык: {ladders.Count}");
            });
            Check("повторяемость: один вход — байт в байт", () =>
            {
                var a = Room("a", ('C', Concrete)); Set(a, RoomLayers.Solid, 3, 3, 'C');
                string first = RoomSerializer.Write(Build(definitions, 2, 2, new[] { "a", "a", "a", "a" }, new[] { false, true, true, false }, a).Level);
                string second = RoomSerializer.Write(Build(definitions, 2, 2, new[] { "a", "a", "a", "a" }, new[] { false, true, true, false }, a).Level);
                Expect(first == second, "сериализация совпала");
            });
            Check("общая клетка без стены: конструкция одной стороны сохраняется, иначе значение владельца; всё в Issues", () =>
            {
                var a = Room("a", ('P', Beam)); Set(a, RoomLayers.Platform, 47, 5, 'P');
                var b = Room("b", ('L', "remains:ladder:right"), ('P', Beam));
                Set(b, RoomLayers.Ladder, 0, 5, 'L'); Set(b, RoomLayers.Platform, 0, 5, 'P'); Set(b, RoomLayers.Height, 0, 5, '3'); Set(b, RoomLayers.Water, 0, 5, '~');
                Set(b, RoomLayers.Platform, 0, 9, 'P'); Set(b, RoomLayers.Height, 0, 9, '2');
                var grid = Build(definitions, 2, 1, new[] { "a", "b" }, new[] { false, false }, a, b);
                Expect(grid.Owner(47, 5).Cell.X == 0, "владелец — левая");
                Expect(Id(grid, RoomLayers.Ladder, 47, 5) == "remains:ladder:right", "лестница только правой стороны сохранена (в оригине оба края)");
                Expect(Id(grid, RoomLayers.Platform, 47, 5) == Beam, "балка");
                Expect(grid.Level.Layers[RoomLayers.Height][5][47] == RoomFile.Empty, "у обеих балка — высота владельца (полная), не соседа");
                Expect(grid.Level.Layers[RoomLayers.Water][5][47] == RoomFile.Empty, "вода владельца (нет)");
                Expect(Id(grid, RoomLayers.Platform, 47, 9) == Beam && grid.Level.Layers[RoomLayers.Height][9][47] == '2', "балка только соседа — с её высотой");
                Expect(grid.Issues.Any(i => i.Contains("конструкций только одной стороны") && i.Contains("ladder 1") && i.Contains("platform 1")), "сохранённые конструкции в Issues: " + string.Join(" | ", grid.Issues));
                string conflict = grid.Issues.SingleOrDefault(i => i.Contains("расхождений без стены"));
                Expect(conflict != null && conflict.Contains("47,5 height") && conflict.Contains("47,5 water"), "расхождения высоты и воды в Issues: " + string.Join(" | ", grid.Issues));
            });
            Check("формы разных комнат с разной высотой клетки не смешиваются (оба направления), с одинаковой — смешиваются", () =>
            {
                const string Stairs = "remains:stairs:metal-rise-right";
                var a = Room("a", ('P', Beam), ('S', Stairs));
                Set(a, RoomLayers.Platform, 47, 5, 'P');                                        // owner: full beam
                Set(a, RoomLayers.Stairs, 47, 7, 'S'); Set(a, RoomLayers.Height, 47, 7, '3');   // owner: low stairs
                Set(a, RoomLayers.Platform, 47, 9, 'P');                                        // owner: full beam
                var b = Room("b", ('P', Beam), ('S', Stairs));
                Set(b, RoomLayers.Stairs, 0, 5, 'S'); Set(b, RoomLayers.Height, 0, 5, '3');
                Set(b, RoomLayers.Platform, 0, 7, 'P');
                Set(b, RoomLayers.Stairs, 0, 9, 'S');
                var grid = Build(definitions, 2, 1, new[] { "a", "b" }, new[] { false, false }, a, b);
                Expect(Id(grid, RoomLayers.Stairs, 47, 5) == null && grid.Level.Layers[RoomLayers.Height][5][47] == RoomFile.Empty, "балка владельца + низкие ступени соседа: ступени не добавлены, высота полная");
                Expect(Id(grid, RoomLayers.Platform, 47, 7) == null && grid.Level.Layers[RoomLayers.Height][7][47] == '3', "низкие ступени владельца + полная балка соседа: балка не добавлена, высота 3");
                Expect(Id(grid, RoomLayers.Stairs, 47, 9) == Stairs && Id(grid, RoomLayers.Platform, 47, 9) == Beam, "одинаковая высота: обе формы");
                Expect(grid.Issues.Any(i => i.Contains("2 форм соседа с другой высотой") && i.Contains("47,5") && i.Contains("47,7")), "несовместимые в Issues: " + string.Join(" | ", grid.Issues));
            });
            Check("угол четырёх комнат: формы соседей с разной высотой не смешиваются", () =>
            {
                var a = Room("a", ('P', Beam)); Set(a, RoomLayers.Platform, 0, 0, 'P'); Set(a, RoomLayers.Height, 0, 0, '2');   // lower right room, top-left corner
                var e = Room("e", ('P', Beam)); Set(e, RoomLayers.Platform, 47, 24, 'P');                                     // upper left room, bottom-right corner
                var grid = Build(definitions, 2, 2, new[] { "e", "x", "e", "a" }, new[] { false, false, false, false }, e, Room("x"), a);
                // corner 47,24: owner is the lower left room "x" (empty); first form from the upper left (full), the lower right (2) does not join
                Expect(grid.Owner(47, 24).Cell.X == 0 && grid.Owner(47, 24).Cell.Y == 1, "владелец угла — нижняя левая");
                Expect(Id(grid, RoomLayers.Platform, 47, 24) == Beam && grid.Level.Layers[RoomLayers.Height][24][47] == RoomFile.Empty, "форма одной высоты, без смешения");
            });
            Check("ширина прохода < 2 и стык вне завода — ошибка до построения", () =>
            {
                var a = Room("a", ('C', Concrete)); Fill(a, 'C');
                foreach (int width in new[] { 0, 1 })
                    Throws(() => Build(definitions, 2, 1, new[] { "a", "a" }, new[] { false, false }, a, seams: new[] { new FactorySeam { Slot = 0, Width = width } }), "ширина прохода меньше 2");
                Build(definitions, 2, 1, new[] { "a", "a" }, new[] { false, false }, a, seams: new[] { new FactorySeam { Slot = 0, Width = 2 } });
                Throws(() => Build(definitions, 2, 1, new[] { "a", "a" }, new[] { false, false }, a, seams: new[] { new FactorySeam { X = 1, Slot = 0, Width = 2 } }), "вне завода");
                Throws(() => Build(definitions, 2, 1, new[] { "a", "a" }, new[] { false, false }, a, seams: new[] { new FactorySeam { X = -1, Slot = 0, Width = 2 } }), "вне завода");
                Throws(() => Build(definitions, 2, 1, new[] { "a", "a" }, new[] { false, false }, a, seams: new[] { new FactorySeam { Down = true, Slot = 6, Width = 2 } }), "вне завода");
            });
            Check("неизвестный ключ клетки — ошибка с шаблоном, слоем и клеткой", () =>
            {
                var a = Room("a"); Set(a, RoomLayers.Solid, 2, 3, 'Q');
                try { Build(definitions, 1, 1, new[] { "a" }, new[] { false }, a); }
                catch (FormatException e) when (e.Message.Contains("a слой solid клетка 2,21")) { return; }
                throw new Exception("ошибки нет или она без места");
            });
            Console.WriteLine(failed == 0 ? "Все проверки сетки завода пройдены." : $"Не пройдено: {failed}.");
            return failed == 0 ? 0 : 1;
        }

        private static FactoryGrid Build(RoomDefinitionCatalog definitions, int width, int depth, string[] templates, bool[] mirror,
            RoomFile a, RoomFile b = null, FactorySeam[] seams = null)
            => Build(definitions, width, depth, templates, mirror, new[] { a, b }, seams);

        private static FactoryGrid Build(RoomDefinitionCatalog definitions, int width, int depth, string[] templates, bool[] mirror,
            RoomFile a, RoomFile b, RoomFile c)
            => Build(definitions, width, depth, templates, mirror, new[] { a, b, c }, null);

        private static FactoryGrid Build(RoomDefinitionCatalog definitions, int width, int depth, string[] templates, bool[] mirror,
            RoomFile[] list, FactorySeam[] seams)
        {
            var layout = new FactoryLayout { Land = "test", Width = width, Depth = depth };
            for (int i = 0; i < templates.Length; i++)
                layout.Cells.Add(new FactoryCell { X = i / depth, Y = i % depth, Template = templates[i], Mirror = mirror[i] });
            if (seams != null) layout.Seams.AddRange(seams);
            var rooms = new Dictionary<string, RoomFile>(StringComparer.Ordinal);
            foreach (var room in list) if (room != null) rooms[room.Template] = room;
            return FactoryGridBuilder.Build(layout, rooms, definitions, Steel, closeOuter: true);
        }

        private static RoomFile Room(string template, params (char key, string id)[] palette)
        {
            var room = new RoomFile { Template = template, Width = W, Height = H };
            foreach (var (key, id) in palette)
            {
                string layer = id.StartsWith("remains:solid:", StringComparison.Ordinal) ? RoomLayers.Solid
                    : id.StartsWith("remains:ladder:", StringComparison.Ordinal) ? RoomLayers.Ladder
                    : id.StartsWith("remains:beam:", StringComparison.Ordinal) ? RoomLayers.Platform : RoomLayers.Stairs;
                room.Palette(layer).Add(key, id);
            }
            foreach (string layer in new[] { RoomLayers.Solid, RoomLayers.Ladder, RoomLayers.Platform, RoomLayers.Stairs, RoomLayers.Height, RoomLayers.Water })
                room.Layers[layer] = Enumerable.Repeat(new string(RoomFile.Empty, W), H).ToArray();
            return room;
        }

        private static void Set(RoomFile room, string layer, int x, int row, char key)
        {
            var line = room.Layers[layer][row].ToCharArray(); line[x] = key; room.Layers[layer][row] = new string(line);
        }

        private static void Fill(RoomFile room, char key) => room.Layers[RoomLayers.Solid] = Enumerable.Repeat(new string(key, W), H).ToArray();

        private static string Id(FactoryGrid grid, string layer, int x, int row)
        {
            if (!grid.Level.Layers.TryGetValue(layer, out var rows)) return null;
            char c = rows[row][x];
            return c == RoomFile.Empty ? null : grid.Level.Palettes[layer][c];
        }

        private static void Expect(bool condition, string what) { if (!condition) throw new Exception(what); }

        private static void Throws(Action action, string message)
        {
            try { action(); }
            catch (FormatException e) when (e.Message.Contains(message)) { return; }
            throw new Exception("нет ошибки «" + message + "»");
        }
    }
}
