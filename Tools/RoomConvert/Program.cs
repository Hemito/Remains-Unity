using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Remains.RoomFormat;
using Remains.RoomFormat.Original;

namespace Remains.RoomConvert
{
    // Original Rooms/*.xml -> *.room.json + coverage report (docs/pipelines/room-format.md).
    public static class Program
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "convert") return Convert(Options(args));
                if (args.Length > 0 && args[0] == "validate") return Validate(Options(args));
                if (args.Length > 0 && args[0] == "geometry") return Geometry(Options(args));
                if (args.Length > 0 && (args[0] == "tiles" || args[0] == "widen-hairlines") && Options(args).TryGetValue("java", out string java)) TileExport.Java = java;
                if (args.Length > 0 && args[0] == "tiles") return TileExport.Run(Options(args), Required);
                if (args.Length > 0 && args[0] == "layout") return FactoryMap.Run(Options(args), Required);
                if (args.Length > 0 && args[0] == "grid-checks") return FactoryGridChecks.Run(Options(args), Required);
                if (args.Length > 0 && args[0] == "trace") return LightReference.Run(Options(args), Required);
                if (args.Length > 0 && args[0] == "widen-hairlines") { var o = Options(args); return HairlineSwf.Run(Required(o, "ffdec"), Required(o, "swf"), Required(o, "out"), o.TryGetValue("pony", out string pony) ? pony : null); }
                Console.Error.WriteLine("Использование:\n  convert --rooms <Rooms> --alldata <AllData.as> --gamedata <GameData.as> --forms <original-forms.json> --out <папка> [--overwrite]\n  validate --dir <папка>\n  geometry --room <файл.room.json> --definitions <definitions.json> [--compare <черновик geometry.json> --landings <factory-ladder-landings.json> --source-index <N>]\n  tiles --ffdec <ffdec.jar> --dlc <Remains/DLC> --masks <tile-masks.json> --definitions <definitions.json> --lands <lands.json> --rooms-dir <папка комнат> --out <папка> [--java <java.exe>]\n  widen-hairlines --ffdec <ffdec.jar> --swf <in.swf> --out <out.swf> [--pony <pony.json>] [--java <java.exe>]\n  layout --rooms-dir <комнаты земли> --definitions <definitions.json> --lands <lands.json> --seed <сид мира> [--land plant --index 0 --width 4 --depth 21] --out <layout.json> [--grid-out <сетка.room.json>] [--png <схема.png> --scale 3]\n  grid-checks --definitions <definitions.json>\n  trace --room <файл.room.json> --definitions <definitions.json> --lands <lands.json> --lighting <room-lighting.json> --out <итог.png> [--albedo-image <предпросмотр без света.png> --ambient <0..1> --resolution 10 --rays 256 --bounces 2 --element-rays 512 --albedo 0.5 --power 1 --lamp-size 0.5 --disk-samples 8 --exposure 1]");
                return 2;
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is ArgumentException || e is InvalidOperationException)
            {
                Console.Error.WriteLine("Ошибка: " + e.Message);
                return 1;
            }
        }

        private static Dictionary<string, string> Options(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Неожиданный аргумент: " + args[i]);
                string key = args[i].Substring(2);
                bool flag = i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal);
                result[key] = flag ? "true" : args[++i];
            }
            return result;
        }

        private static string Required(Dictionary<string, string> options, string key)
            => options.TryGetValue(key, out string value) ? value : throw new ArgumentException("Нет параметра --" + key);

        private static int Convert(Dictionary<string, string> options)
        {
            string roomsDir = Required(options, "rooms"), outDir = Required(options, "out");
            string allData = Required(options, "alldata"), formsFile = Required(options, "forms");
            if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any() && !options.ContainsKey("overwrite"))
                throw new IOException($"Папка {outDir} не пуста; укажите --overwrite (файлы перезаписываются, лишние не удаляются).");

            var forms = OriginalForms.Parse(File.ReadAllText(allData, Encoding.UTF8), allData);
            var map = OriginalFormMap.Parse(File.ReadAllText(formsFile, Encoding.UTF8), formsFile);
            var converter = new OriginalRoomConverter(forms, map);
            var definitionIssues = new List<string>();
            var definitions = map.BuildDefinitions(forms, definitionIssues);
            foreach (var decor in OriginalForms.ParseDecor(File.ReadAllText(allData, Encoding.UTF8), allData)) definitions.Add(decor);
            foreach (var obj in OriginalForms.ParseObjects(File.ReadAllText(allData, Encoding.UTF8), allData)) definitions.Add(obj);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "definitions.json"), definitions.Write(), Utf8);
            foreach (string issue in definitionIssues) Console.WriteLine("Каталог: " + issue);
            string gameData = Required(options, "gamedata");
            var lands = OriginalLands.Parse(File.ReadAllText(gameData, Encoding.UTF8), gameData);
            File.WriteAllText(Path.Combine(outDir, "lands.json"), lands.Write(), Utf8);
            int written = 0, roundTripFailures = 0, assigned = 0, decorAssigned = 0;
            var idIssues = new List<string>();
            foreach (string file in Directory.GetFiles(roomsDir, "*.xml").OrderBy(f => f, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);
                string land = OriginalRoomConverter.Slug(OriginalRoomConverter.LandOf(name));
                foreach (var room in converter.ConvertFile(name, File.ReadAllText(file, Encoding.UTF8)))
                {
                    string target = Path.Combine(outDir, land, SafeName(room.Template.Substring(room.Template.LastIndexOf(':') + 1)) + ".room.json");
                    assigned += AssignObjectIds(room, target, idIssues);
                    decorAssigned += AssignDecorIds(room, target, new HashSet<string>(room.Objects.Where(o => o.Id != null).Select(o => o.Id), StringComparer.Ordinal));
                    string text = RoomSerializer.Write(room);
                    string reread = RoomSerializer.Write(RoomSerializer.Read(text, room.Template));
                    if (reread != text) { roundTripFailures++; converter.Report.Failures.Add(room.Template + ": повторная запись после чтения отличается."); }
                    string dir = Path.Combine(outDir, land);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(target, text, Utf8);
                    written++;
                }
            }
            string report = Report(converter.Report, forms, written, roundTripFailures);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "coverage.md"), report, Utf8);
            Console.WriteLine($"Комнат записано: {written}; не сконвертировано: {converter.Report.RoomsFailed}; ошибок формата: {converter.Report.ValidationErrors.Count}; расхождений повторной записи: {roundTripFailures}.");
            Console.WriteLine("Отчёт: " + Path.Combine(outDir, "coverage.md"));
            Console.WriteLine($"ID объектов: назначено новых {assigned} (прежние сохранены по коду оригинала из уже записанных файлов); ID декора: новых {decorAssigned}.");
            foreach (string issue in idIssues) Console.WriteLine("  " + issue);
            return converter.Report.RoomsFailed == 0 && converter.Report.ValidationErrors.Count == 0 && roundTripFailures == 0 ? 0 : 3;
        }

        // Persistent object IDs (room-format §4.3): an object without the original uid gets a random ID once; a
        // later conversion keeps it by finding the same object in the already written file (matched by the
        // original's per-room code, which identifies the source object; the ID is never derived from it).
        // A code that is missing or repeated cannot be matched: the object gets a new ID and it is reported.
        private static int AssignObjectIds(RoomFile room, string previousFile, List<string> issues)
        {
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var repeated = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(previousFile))
                foreach (var o in RoomSerializer.Read(File.ReadAllText(previousFile, Encoding.UTF8), previousFile).Objects)
                    if (o.Id != null && o.Code != null && !previous.TryAdd(o.Code, o.Id)) repeated.Add(o.Code);
            var codes = room.Objects.Where(o => o.Code != null).GroupBy(o => o.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            var used = new HashSet<string>(room.Objects.Where(o => o.Id != null).Select(o => o.Id), StringComparer.Ordinal);
            int assigned = 0;
            foreach (var o in room.Objects)
            {
                if (o.Id != null) continue;   // the original's own uid
                if (o.Code != null && !codes.Contains(o.Code) && !repeated.Contains(o.Code) && previous.TryGetValue(o.Code, out string kept) && used.Add(kept)) { o.Id = kept; continue; }
                if (o.Code == null || codes.Contains(o.Code) || repeated.Contains(o.Code))
                    issues.Add($"{room.Template}: объект {o.Type} в {o.X},{o.Y} без однозначного кода — ID не переносится между конверсиями.");
                string id;
                do id = NewObjectId(); while (!used.Add(id));
                o.Id = id; assigned++;
            }
            return assigned;
        }

        // Decor IDs (lamps can be broken, so they need identity): assigned once, kept on reconversion by matching
        // the same decor (type, cell, n-th of that type in that cell) in the already written file.
        private static int AssignDecorIds(RoomFile room, string previousFile, HashSet<string> used)
        {
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(previousFile))
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var d in RoomSerializer.Read(File.ReadAllText(previousFile, Encoding.UTF8), previousFile).Decor)
                {
                    string key = $"{d.Type}|{d.X}|{d.Y}";
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 0;
                    if (d.Id != null) previous[key + "|" + counts[key]] = d.Id;
                }
            }
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            int assigned = 0;
            foreach (var d in room.Decor)
            {
                string key = $"{d.Type}|{d.X}|{d.Y}";
                seen[key] = seen.TryGetValue(key, out int n) ? n + 1 : 0;
                if (d.Id != null) { used.Add(d.Id); continue; }
                if (previous.TryGetValue(key + "|" + seen[key], out string kept) && used.Add(kept)) { d.Id = kept; continue; }
                string id;
                do id = NewObjectId(); while (!used.Add(id));
                d.Id = id; assigned++;
            }
            return assigned;
        }

        private static string NewObjectId()
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var id = new char[16];
            for (int i = 0; i < 16; i++) id[i] = alphabet[bytes[i] % alphabet.Length];
            return new string(id);
        }

        private static int Validate(Dictionary<string, string> options)
        {
            string dir = Required(options, "dir");
            int files = 0, errors = 0;
            foreach (string file in Directory.GetFiles(dir, "*.room.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                files++;
                try
                {
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    var room = RoomSerializer.Read(text, file);
                    foreach (string error in RoomValidator.Validate(room, file)) { errors++; Console.WriteLine(error); }
                    if (RoomSerializer.Write(room) != text.Replace("\r\n", "\n")) { errors++; Console.WriteLine(file + ": файл не в канонической записи (повторная запись отличается)."); }
                }
                catch (FormatException e) { errors++; Console.WriteLine(e.Message); }
            }
            Console.WriteLine($"Файлов: {files}; ошибок: {errors}.");
            return errors == 0 ? 0 : 3;
        }

        private static int Geometry(Dictionary<string, string> options)
        {
            string roomFile = Required(options, "room");
            var room = RoomSerializer.Read(File.ReadAllText(roomFile, Encoding.UTF8), roomFile);
            var definitions = RoomDefinitionCatalog.Read(File.ReadAllText(Required(options, "definitions"), Encoding.UTF8), Required(options, "definitions"));
            var geometry = RoomGeometryBuilder.Build(room, definitions);
            Console.WriteLine($"{room.Template}: поверхностей {geometry.Surfaces.Count}, лестниц {geometry.Ladders.Count}, размер {geometry.Width}x{geometry.Height} м.");
            foreach (string issue in geometry.Issues) Console.WriteLine("  " + issue);
            if (!options.TryGetValue("compare", out string draftFile)) return 0;
            return GeometryComparison.Compare(geometry, room, draftFile, options.TryGetValue("landings", out string landings) ? landings : null,
                options.TryGetValue("source-index", out string index) ? int.Parse(index, System.Globalization.CultureInfo.InvariantCulture) : -1) ? 0 : 3;
        }

        private static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            if (name.Any(c => invalid.Contains(c))) throw new ArgumentException("Недопустимое имя комнаты для файла: " + name);
            return name;
        }

        private static string Report(ConversionReport r, OriginalForms forms, int written, int roundTrip)
        {
            var b = new StringBuilder();
            b.AppendLine("# Отчёт покрытия конвертации комнат оригинала");
            b.AppendLine();
            b.AppendLine("Сгенерировано Tools/RoomConvert. «Сконвертировано» не означает «готово для runtime»: каталог определений и архетипов на этом этапе не оценивается.");
            b.AppendLine();
            b.AppendLine("## Итог");
            b.AppendLine($"- Файлов XML: {r.Files}; комнат: {r.Rooms}; записано: {written}; не сконвертировано: {r.RoomsFailed}");
            b.AppendLine($"- Ошибок проверки формата: {r.ValidationErrors.Count}; расхождений повторной записи: {roundTrip}");
            b.AppendLine($"- Клеток: {r.Cells}; различных токенов: {r.Tokens.Count}");
            b.AppendLine($"- Форм оригинала: ed=1 {forms.Front.Count}, прочих {forms.Other.Count}");
            b.AppendLine();
            b.AppendLine("## Клетки");
            b.AppendLine($"- Структурных форм в клетке: 0 — {r.OverlayCount[0]}, 1 — {r.OverlayCount[1]}, 2 — {r.OverlayCount[2]}, 3+ — {r.OverlayCount[3]}");
            if (r.OverlayExamples.Count > 0) b.AppendLine("- Примеры двух и более накладок: " + string.Join(", ", r.OverlayExamples.Select(t => "`" + t + "`")));
            b.AppendLine($"- Частичная высота: {r.PartialHeight} (без твёрдой формы: {r.PartialWithoutSolid}); вода: {r.Water}");
            b.AppendLine($"- Правило zad заменило задний план: {r.ZadApplied}; задний план с неизвестной формой: {r.ZadUnknown}");
            Section(b, "Первый символ пропущен оригиналом (нет формы ed=1)", r.IgnoredFirst);
            Section(b, "Неизвестные символы после первого", r.UnknownChars, r.UnknownCharExample);
            Section(b, "Накладки с ed вне 2/3/4", r.OtherEd);
            Section(b, "Структурные формы без сопоставления в original-forms.json", r.UnmappedOver);
            Section(b, "Сочетания смысловых слоёв в клетке (в порядке применения)", r.LayerCombinations);
            Section(b, "Две формы одного смыслового слоя в клетке (вторая не сохранена)", r.SameLayerTwice);
            b.AppendLine($"- Лестница применена после платформы/ступеней (нарушило бы правило материала): {r.MaterialOrderExceptions}");
            Section(b, "Использование твёрдых форм", r.SolidUse);
            Section(b, "Использование задних стен", r.BackUse);
            Section(b, "Использование структурных форм", r.OverUse);
            b.AppendLine();
            b.AppendLine("## Объекты и декор");
            b.AppendLine($"- Объектов: {r.ObjectsTotal}; без uid (ID будет назначен при принятии файлов): {r.ObjectsWithoutUid}");
            b.AppendLine($"- Типов объектов: {r.ObjectTypes.Count}; типов декора: {r.DecorTypes.Count}");
            b.AppendLine($"- Нецелых координат: {r.NonIntegerCoordinates}; координат вне комнаты: {r.OutOfRoomCoordinates}");
            b.AppendLine($"- Ссылок на цели вне комнаты (scr targ): {r.UnresolvedLinks}");
            Section(b, "Дочерние элементы объектов", r.ObjectChildren);
            Section(b, "Параметры объектов", r.ObjectParams);
            Section(b, "Типы объектов", r.ObjectTypes);
            b.AppendLine();
            b.AppendLine("## Комнаты");
            Section(b, "Элементы комнаты", r.RoomChildren);
            Section(b, "Атрибуты <room> кроме name/x/y", r.RoomAttributes);
            Section(b, "Ключи options", r.OptionKeys);
            Section(b, "Элементы файла кроме <room>", r.FileChildren);
            b.AppendLine($"- Строк doors: {r.ConnectorRows}; не из 22 значений: {r.ConnectorBadLength}");
            Section(b, "Значения разъёмов", r.ConnectorValues);
            if (r.Failures.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## Не сконвертировано / сбои");
                foreach (string f in r.Failures) b.AppendLine("- " + f);
            }
            if (r.ValidationErrors.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## Ошибки проверки формата (первые 50)");
                foreach (string e in r.ValidationErrors.Take(50)) b.AppendLine("- " + e);
            }
            return b.ToString();
        }

        private static void Section(StringBuilder b, string title, SortedDictionary<string, int> map, SortedDictionary<string, string> examples = null)
        {
            b.AppendLine();
            b.AppendLine($"### {title} ({map.Count})");
            if (map.Count == 0) { b.AppendLine("- нет"); return; }
            foreach (var entry in map.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal))
            {
                string example = examples != null && examples.TryGetValue(entry.Key, out string ex) ? " — " + ex : "";
                b.AppendLine($"- `{entry.Key}`: {entry.Value}{example}");
            }
        }
    }
}
