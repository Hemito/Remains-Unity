using System;
using System.Collections.Generic;
using System.Globalization;

namespace Remains.RoomFormat
{
    // Layout of a generated factory (docs/research/factory-generation.md): which room template stands in
    // each grid cell, mirrored or not, which passages are cut on the seams and what the cell hosts. Data
    // for the level builder; the same seed, index and generator version always give the same layout, and
    // the world keeps the layout it was built with.
    public enum FactoryCellRole { None, Checkpoint, Exit, Trial }

    public sealed class FactoryCell
    {
        public int X, Y;                       // grid column, row (row 0 at the top)
        public string Template;                // room template ID
        public bool Mirror;
        public string Back;                    // template of the back layer room, null = none
        public int Level;                      // the room's own level (options.level)
        public int MaxLevel;                   // highest room level allowed at this depth
        public int Band;                       // original stage the row corresponds to
        public bool Start, Repeat;             // fixed start room; chosen after the band's room budget ran out
        public FactoryCellRole Role;
        public bool RoleRequired;              // trial at the bottom of a band (original "important" trial)
    }

    // A passage cut through the shared edge of two neighbours. Slot is in world orientation of the first
    // cell: Right — rows 4*k+3 of the right edge (k = 0..5); Down — columns 9*k+4 of the bottom edge
    // (slot 6 + k, k = 0..4). Width 2 cuts a 2-cell opening, more — 3 rows / 4 columns.
    public sealed class FactorySeam
    {
        public int X, Y;
        public bool Down;
        public int Slot, Width;
    }

    public sealed class FactoryLayout
    {
        public const string FormatName = "remains-factory-layout";
        public const int CurrentVersion = 1;
        public int GeneratorVersion;
        public string Land;
        public ulong Seed;                     // the world seed the layout was derived from
        public int Index;                      // factory index (0 = main)
        public int Width, Depth;
        public readonly List<FactoryCell> Cells = new List<FactoryCell>();
        public readonly List<FactorySeam> Seams = new List<FactorySeam>();
        public readonly List<string> Issues = new List<string>();

        public FactoryCell Cell(int x, int y) => Cells[x * Depth + y];

        public string Write()
        {
            var root = JsonValue.NewObject().Set("format", JsonValue.Of(FormatName)).Set("version", JsonValue.Of(CurrentVersion))
                .Set("generator", JsonValue.Of(GeneratorVersion)).Set("land", JsonValue.Of(Land))
                .Set("seed", JsonValue.Of(Seed.ToString(CultureInfo.InvariantCulture))).Set("index", JsonValue.Of(Index))
                .Set("width", JsonValue.Of(Width)).Set("depth", JsonValue.Of(Depth));
            var cells = JsonValue.NewArray();
            foreach (var c in Cells)
            {
                var v = JsonValue.NewObject().Set("x", JsonValue.Of(c.X)).Set("y", JsonValue.Of(c.Y)).Set("template", JsonValue.Of(c.Template))
                    .Set("mirror", JsonValue.Of(c.Mirror)).Set("level", JsonValue.Of(c.Level)).Set("maxLevel", JsonValue.Of(c.MaxLevel))
                    .Set("band", JsonValue.Of(c.Band));
                if (c.Back != null) v.Set("back", JsonValue.Of(c.Back));
                if (c.Start) v.Set("start", JsonValue.Of(true));
                if (c.Repeat) v.Set("repeat", JsonValue.Of(true));
                if (c.Role != FactoryCellRole.None) v.Set("role", JsonValue.Of(c.Role.ToString().ToLowerInvariant()));
                if (c.RoleRequired) v.Set("required", JsonValue.Of(true));
                cells.Add(v);
            }
            root.Set("cells", cells);
            var seams = JsonValue.NewArray();
            foreach (var s in Seams)
                seams.Add(JsonValue.NewObject().Set("x", JsonValue.Of(s.X)).Set("y", JsonValue.Of(s.Y))
                    .Set("side", JsonValue.Of(s.Down ? "down" : "right")).Set("slot", JsonValue.Of(s.Slot)).Set("width", JsonValue.Of(s.Width)));
            root.Set("seams", seams);
            var issues = JsonValue.NewArray();
            foreach (string issue in Issues) issues.Add(JsonValue.Of(issue));
            root.Set("issues", issues);
            return JsonText.Write(root, 3, new[] { "cells", "seams", "issues" });
        }
    }
}
