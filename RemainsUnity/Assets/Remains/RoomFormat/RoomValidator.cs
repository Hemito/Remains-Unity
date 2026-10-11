using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    // Format-level validation (spec §6). Every message names the file, layer and cell.
    public static class RoomValidator
    {
        public static List<string> Validate(RoomFile room, string source)
        {
            var errors = new List<string>();
            string ctx = source ?? room.Template ?? "<room>";
            if (string.IsNullOrWhiteSpace(room.Template)) errors.Add($"{ctx}: пустой template.");
            if (room.Width <= 0 || room.Height <= 0) errors.Add($"{ctx}: неверный размер {room.Width}x{room.Height}.");
            if (!(room.Cell > 0) || double.IsInfinity(room.Cell)) errors.Add($"{ctx}: неверный размер клетки {room.Cell}.");

            foreach (var group in room.Palettes)
                foreach (var entry in group.Value)
                {
                    if (entry.Key == RoomFile.Empty || entry.Key < 0x21 || entry.Key == '"' || entry.Key == '\\')
                        errors.Add($"{ctx}: недопустимый ключ палитры '{entry.Key}' в группе {group.Key}.");
                    if (string.IsNullOrWhiteSpace(entry.Value)) errors.Add($"{ctx}: пустой ID для ключа '{entry.Key}' в группе {group.Key}.");
                }

            foreach (var layer in room.Layers)
            {
                if (!RoomLayers.IsKnown(layer.Key)) { errors.Add($"{ctx}: неизвестный слой {layer.Key}."); continue; }
                if (layer.Value.Length != room.Height) errors.Add($"{ctx}: слой {layer.Key} содержит {layer.Value.Length} строк вместо {room.Height}.");
                string group = RoomLayers.GroupOf(layer.Key);
                room.Palettes.TryGetValue(group ?? "", out var palette);
                for (int row = 0; row < layer.Value.Length; row++)
                {
                    string line = layer.Value[row] ?? "";
                    if (line.Length != room.Width) errors.Add($"{ctx}: слой {layer.Key}, строка {row}: длина {line.Length} вместо {room.Width}.");
                    for (int x = 0; x < line.Length; x++)
                    {
                        char c = line[x];
                        bool ok = group == null ? RoomLayers.FixedAllowed(layer.Key, c) : c == RoomFile.Empty || (palette != null && palette.ContainsKey(c));
                        if (!ok) errors.Add($"{ctx}: слой {layer.Key}, клетка x={x} y={room.Height - 1 - row}: символ '{c}' не найден в палитре/алфавите слоя.");
                    }
                }
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obj in room.Objects)
            {
                if (string.IsNullOrWhiteSpace(obj.Type)) errors.Add($"{ctx}: объект без type в клетке {obj.X},{obj.Y}.");
                if (obj.Id != null && !ids.Add(obj.Id)) errors.Add($"{ctx}: повтор id объекта \"{obj.Id}\".");
                if (!Inside(room, obj.X, obj.Y)) errors.Add($"{ctx}: объект {obj.Type} ({obj.Id}) вне комнаты: {obj.X},{obj.Y}.");
            }
            foreach (var decor in room.Decor)
            {
                if (string.IsNullOrWhiteSpace(decor.Type)) errors.Add($"{ctx}: декор без type в клетке {decor.X},{decor.Y}.");
                if (decor.Id != null && !ids.Add(decor.Id)) errors.Add($"{ctx}: повтор id декора \"{decor.Id}\" (id уникальны среди объектов и декора).");
            }
            return errors;
        }

        // Link targets may name objects in other rooms (original scripts are level-wide),
        // so unresolved targets are reported separately, not as format errors.
        public static List<string> UnresolvedLinks(RoomFile room)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obj in room.Objects) if (obj.Id != null) ids.Add(obj.Id);
            var result = new List<string>();
            foreach (var obj in room.Objects)
                foreach (var link in obj.Links)
                    if (link.TryGetValue("targ", out string target) && !ids.Contains(target)) result.Add(target);
            return result;
        }

        private static bool Inside(RoomFile room, int x, int y) => x >= 0 && y >= 0 && x < room.Width && y < room.Height;
    }
}
