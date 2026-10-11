using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    // A rectangular part of a room or level grid as its own RoomFile (same palettes, cells and options),
    // e.g. the light window of a large level. Rows stay top-down; column/row are the top-left cell.
    public static class RoomFileRegion
    {
        public static RoomFile Crop(RoomFile source, int column, int row, int width, int height)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (column < 0 || row < 0 || width <= 0 || height <= 0 || column + width > source.Width || row + height > source.Height)
                throw new ArgumentOutOfRangeException(nameof(column), $"{source.Template}: область {column},{row} {width}×{height} вне {source.Width}×{source.Height}.");
            var result = new RoomFile { Template = source.Template, Width = width, Height = height, Cell = source.Cell };
            foreach (var palette in source.Palettes) foreach (var entry in palette.Value) result.Palette(palette.Key).Add(entry.Key, entry.Value);
            foreach (var layer in source.Layers)
            {
                var rows = new string[height];
                for (int r = 0; r < height; r++) rows[r] = layer.Value[row + r].Substring(column, width);
                result.Layers.Add(layer.Key, rows);
            }
            foreach (var option in source.Options) result.Options.Add(option.Key, option.Value);
            return result;
        }

        // Rooms side by side without a shared column (a fixed land such as the hub's camp, where rooms keep their own
        // edges), as one grid: palettes merged by definition ID, fixed-alphabet layers copied. Same height and cell.
        public static RoomFile JoinHorizontal(string template, IReadOnlyList<RoomFile> parts)
        {
            if (parts == null || parts.Count == 0) throw new ArgumentException("Нет частей для объединения сетки.");
            int height = parts[0].Height, width = 0;
            foreach (var part in parts)
            {
                if (part.Height != height || Math.Abs(part.Cell - parts[0].Cell) > 1e-9) throw new FormatException($"{part.Template}: высота или клетка отличаются от {parts[0].Template}.");
                width += part.Width;
            }
            var result = new RoomFile { Template = template, Width = width, Height = height, Cell = parts[0].Cell };
            var layers = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var part in parts) foreach (var layer in part.Layers.Keys) layers.Add(layer);
            const string Keys = "#ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!$%&'()*+,-/:;<=>?@[]^_`{|}~";
            foreach (string layer in layers)
            {
                string group = RoomLayers.GroupOf(layer);
                var keys = new Dictionary<string, char>(StringComparer.Ordinal);
                if (group != null)
                {
                    var ids = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var part in parts) if (part.Palettes.TryGetValue(group, out var palette)) foreach (var id in palette.Values) ids.Add(id);
                    if (ids.Count > Keys.Length) throw new FormatException($"Слой {layer}: {ids.Count} определений, ключей {Keys.Length}.");
                    int k = 0;
                    foreach (string id in ids) { keys.Add(id, Keys[k]); result.Palette(group).Add(Keys[k++], id); }
                }
                var rows = new string[height];
                for (int r = 0; r < height; r++)
                {
                    var line = new System.Text.StringBuilder(width);
                    foreach (var part in parts)
                    {
                        if (!part.Layers.TryGetValue(layer, out var source)) { line.Append(RoomFile.Empty, part.Width); continue; }
                        foreach (char c in source[r])
                            line.Append(c == RoomFile.Empty || group == null ? c : keys[part.Palettes[group][c]]);
                    }
                    rows[r] = line.ToString();
                }
                result.Layers.Add(layer, rows);
            }
            return result;
        }
    }
}
