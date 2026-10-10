using System;
using System.Globalization;
using System.Linq;

namespace Remains.RoomFormat
{
    // Reads the stored selection without invoking the generator.
    public static class FactoryLayoutReader
    {
        public static FactoryLayout Read(string text, string source)
        {
            var root = JsonText.Parse(text, source);
            JsonValue Required(JsonValue parent, string key) => parent.Get(key) ?? throw new FormatException($"{source}: нет поля {key}.");
            int Number(JsonValue parent, string key) => checked((int)Required(parent, key).AsLong(source + "." + key));
            string String(JsonValue parent, string key) => Required(parent, key).AsString(source + "." + key);
            bool Flag(JsonValue parent, string key, bool required = false)
            {
                var value = parent.Get(key);
                if (value == null && !required) return false; // optional flags omitted by FactoryLayout.Write
                if (value == null) throw new FormatException($"{source}: нет поля {key}.");
                if (value.Kind != JsonKind.Bool) throw new FormatException($"{source}.{key}: ожидалось логическое значение.");
                return value.Bool;
            }
            if (String(root, "format") != FactoryLayout.FormatName || Number(root, "version") != FactoryLayout.CurrentVersion)
                throw new FormatException($"{source}: неподдерживаемый формат раскладки завода.");
            if (!ulong.TryParse(String(root, "seed"), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
                throw new FormatException($"{source}: неверный seed завода.");
            var result = new FactoryLayout
            {
                GeneratorVersion = Number(root, "generator"), Land = String(root, "land"), Seed = seed,
                Index = Number(root, "index"), Width = Number(root, "width"), Depth = Number(root, "depth")
            };
            if (result.GeneratorVersion <= 0 || string.IsNullOrWhiteSpace(result.Land) || result.Width <= 0 || result.Depth <= 0)
                throw new FormatException($"{source}: неверная версия генератора, земля или размер завода.");
            var cells = Required(root, "cells");
            if (cells.Kind != JsonKind.Array || cells.Items.Count != checked(result.Width * result.Depth))
                throw new FormatException($"{source}: число комнат не соответствует размеру {result.Width}×{result.Depth}.");
            foreach (var value in cells.Items)
            {
                var cell = new FactoryCell
                {
                    X = Number(value, "x"), Y = Number(value, "y"), Template = String(value, "template"),
                    Mirror = Flag(value, "mirror", true), Level = Number(value, "level"), MaxLevel = Number(value, "maxLevel"),
                    Band = Number(value, "band"), Start = Flag(value, "start"), Repeat = Flag(value, "repeat"),
                    RoleRequired = Flag(value, "required"), Back = value.Get("back")?.AsString(source + ".back")
                };
                string role = value.Get("role")?.AsString(source + ".role");
                cell.Role = role == null ? FactoryCellRole.None : role == "checkpoint" ? FactoryCellRole.Checkpoint :
                    role == "exit" ? FactoryCellRole.Exit : role == "trial" ? FactoryCellRole.Trial :
                    throw new FormatException($"{source}: неизвестная роль клетки {cell.X},{cell.Y}: {role}.");
                if (cell.X < 0 || cell.X >= result.Width || cell.Y < 0 || cell.Y >= result.Depth ||
                    string.IsNullOrWhiteSpace(cell.Template) || cell.Level < 0 || cell.MaxLevel < cell.Level || cell.Band < 0)
                    throw new FormatException($"{source}: неверная комната {cell.X},{cell.Y}: {cell.Template}.");
                result.Cells.Add(cell);
            }
            var ordered = result.Cells.OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();
            for (int i = 0; i < ordered.Length; i++)
                if (ordered[i].X != i / result.Depth || ordered[i].Y != i % result.Depth)
                    throw new FormatException($"{source}: повтор или пропуск клетки раскладки рядом с {ordered[i].X},{ordered[i].Y}.");
            result.Cells.Clear(); result.Cells.AddRange(ordered);
            var seams = Required(root, "seams");
            if (seams.Kind != JsonKind.Array) throw new FormatException(source + ": seams должен быть массивом.");
            foreach (var value in seams.Items)
            {
                string side = String(value, "side");
                if (side != "right" && side != "down") throw new FormatException($"{source}: неизвестная сторона стыка {side}.");
                var seam = new FactorySeam { X = Number(value, "x"), Y = Number(value, "y"), Down = side == "down", Slot = Number(value, "slot"), Width = Number(value, "width") };
                if (seam.X < 0 || seam.Y < 0 || seam.X >= result.Width || seam.Y >= result.Depth || seam.Width < 2 ||
                    (seam.Down ? seam.Y >= result.Depth - 1 || seam.Slot < 6 || seam.Slot > 10 : seam.X >= result.Width - 1 || seam.Slot < 0 || seam.Slot > 5))
                    throw new FormatException($"{source}: неверный стык {seam.X},{seam.Y}, {side}, slot={seam.Slot}, width={seam.Width}.");
                result.Seams.Add(seam);
            }
            var issues = Required(root, "issues");
            if (issues.Kind != JsonKind.Array) throw new FormatException(source + ": issues должен быть массивом.");
            foreach (var issue in issues.Items) result.Issues.Add(issue.AsString(source + ".issues"));
            return result;
        }
    }
}
