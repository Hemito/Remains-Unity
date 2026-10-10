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
    }
}
