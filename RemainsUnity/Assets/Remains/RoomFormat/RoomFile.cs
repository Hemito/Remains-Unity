using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    // One room template (docs/pipelines/room-format.md). Unity-independent.
    public sealed class RoomFile
    {
        public const string FormatName = "remains-room";
        public const int CurrentVersion = 1;
        public const char Empty = '.';

        public string Template;
        public int Width, Height;
        public double Cell = 0.8;
        // Palette group -> key -> definition ID.
        public readonly SortedDictionary<string, SortedDictionary<char, string>> Palettes = new SortedDictionary<string, SortedDictionary<char, string>>(StringComparer.Ordinal);
        // Layer -> rows top to bottom (row r is y = Height-1-r). Absent layer = empty.
        public readonly SortedDictionary<string, string[]> Layers = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        public readonly List<RoomDecor> Decor = new List<RoomDecor>();
        public readonly List<RoomObject> Objects = new List<RoomObject>();
        public int[] Connectors = Array.Empty<int>();
        public readonly SortedDictionary<string, string> Options = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public JsonValue Extra;           // unrecognised source data, verbatim; null = none
        public RoomSource Source;

        public SortedDictionary<char, string> Palette(string group)
        {
            if (!Palettes.TryGetValue(group, out var palette)) Palettes.Add(group, palette = new SortedDictionary<char, string>());
            return palette;
        }
    }

    public sealed class RoomDecor
    {
        public string Type;
        public int X, Y;
        public readonly SortedDictionary<string, string> Params = new SortedDictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed class RoomObject
    {
        public string Id;                 // persistent identity; null until assigned (see spec §4.3)
        public string Type, Code;         // Code is the original script-link key, not identity
        public int X, Y;
        public readonly SortedDictionary<string, string> Params = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public readonly List<SortedDictionary<string, string>> Items = new List<SortedDictionary<string, string>>();
        public readonly List<SortedDictionary<string, string>> Links = new List<SortedDictionary<string, string>>();
        public JsonValue Extra;
    }

    public sealed class RoomSource
    {
        public string File, Room, Sha256;
        public int MapX, MapY;
    }

    // Layer catalogue: which palette group a layer uses, or which fixed alphabet.
    public static class RoomLayers
    {
        public const string Solid = "solid", Back = "back", Over1 = "over1", Over2 = "over2", Height = "height", Water = "water";
        public const string SolidGroup = "solid", BackGroup = "back", OverGroup = "over";
        public static readonly string[] All = { Solid, Back, Over1, Over2, Height, Water };

        // Palette group of a layer, or null for fixed-alphabet layers.
        public static string GroupOf(string layer)
        {
            switch (layer)
            {
                case Solid: return SolidGroup;
                case Back: return BackGroup;
                case Over1: case Over2: return OverGroup;
                case Height: case Water: return null;
                default: throw new ArgumentException("Неизвестный слой комнаты: " + layer);
            }
        }

        public static bool FixedAllowed(string layer, char c)
        {
            if (c == RoomFile.Empty) return true;
            if (layer == Height) return c == '1' || c == '2' || c == '3';
            if (layer == Water) return c == '~';
            return false;
        }

        public static bool IsKnown(string layer) => Array.IndexOf(All, layer) >= 0;

        public static char[][] NewGrid(int width, int height)
        {
            var grid = new char[height][];
            for (int r = 0; r < height; r++) { grid[r] = new char[width]; for (int x = 0; x < width; x++) grid[r][x] = RoomFile.Empty; }
            return grid;
        }
    }
}
