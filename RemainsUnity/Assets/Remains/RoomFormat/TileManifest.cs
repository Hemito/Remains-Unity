using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    // Mixed: vector shapes together with embedded bitmaps — rasterised with the vector reserve.
    public enum TileArtKind { Vector, Bitmap, Mixed }

    // One exported image with provenance (docs/pipelines/svg-export.md): vector art is
    // rasterised with reserve, bitmaps keep their native resolution.
    public sealed class TileArt
    {
        public string Id, File, Sha256, Source;
        public TileArtKind Kind;
        public int Width, Height;              // PNG/JPEG pixels
        public double Zoom;                    // pixels per source pixel (1 for bitmaps)
        public double OriginX, OriginY;        // registration point in source pixels from the top-left of the canvas
    }

    public sealed class TileMaskQuarter
    {
        public string Quarter;                 // topLeft, topRight, bottomLeft, bottomRight
        public int Clip;
        public bool FlipX, FlipY;
    }

    public sealed class TileMask
    {
        public string Name, Kind;              // Kind: corners | whole
        public int Symbol, Clip;
        public readonly List<TileMaskQuarter> Quarters = new List<TileMaskQuarter>();
    }

    public sealed class TileManifest
    {
        public const string FormatName = "remains-tile-manifest";
        public const int CurrentVersion = 1;
        public int QuarterPixels, CellPixels;
        public readonly SortedDictionary<string, TileArt> Art = new SortedDictionary<string, TileArt>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, TileMask> Masks = new SortedDictionary<string, TileMask>(StringComparer.Ordinal);

        public static string MaskFrameId(int clip, int frame) => $"mask:{clip}:{frame}";
        public static string FrontFrameId(int frame) => $"front:{frame}";
        public static string TextureId(string name) => "texture:" + name;
        // Original decor <back>: drawing clip back_<name>_t and background-erase clip back_<name>_e; objects <obj>: clip vis<name>.
        public static string DecorFrameId(string name, int frame) => $"decor:{name}:{frame}";
        public static string DecorEraseFrameId(string name, int frame) => $"decor-erase:{name}:{frame}";
        public static string ObjectFrameId(string name, int frame) => $"object:{name}:{frame}";

        public string Write()
        {
            var root = JsonValue.NewObject().Set("format", JsonValue.Of(FormatName)).Set("version", JsonValue.Of(CurrentVersion))
                .Set("quarterPixels", JsonValue.Of(QuarterPixels)).Set("cellPixels", JsonValue.Of(CellPixels));
            var masks = JsonValue.NewArray();
            foreach (var m in Masks.Values)
            {
                var v = JsonValue.NewObject().Set("name", JsonValue.Of(m.Name)).Set("kind", JsonValue.Of(m.Kind)).Set("symbol", JsonValue.Of(m.Symbol));
                if (m.Kind == "whole") v.Set("clip", JsonValue.Of(m.Clip));
                var quarters = JsonValue.NewArray();
                foreach (var q in m.Quarters)
                    quarters.Add(JsonValue.NewObject().Set("quarter", JsonValue.Of(q.Quarter)).Set("clip", JsonValue.Of(q.Clip))
                        .Set("flipX", JsonValue.Of(q.FlipX)).Set("flipY", JsonValue.Of(q.FlipY)));
                if (quarters.Items.Count > 0) v.Set("quarters", quarters);
                masks.Add(v);
            }
            root.Set("masks", masks);
            var art = JsonValue.NewArray();
            foreach (var a in Art.Values)
                art.Add(JsonValue.NewObject().Set("id", JsonValue.Of(a.Id)).Set("kind", JsonValue.Of(KindName(a.Kind)))
                    .Set("file", JsonValue.Of(a.File)).Set("width", JsonValue.Of(a.Width)).Set("height", JsonValue.Of(a.Height))
                    .Set("zoom", JsonValue.Of(a.Zoom)).Set("originX", JsonValue.Of(a.OriginX)).Set("originY", JsonValue.Of(a.OriginY))
                    .Set("source", JsonValue.Of(a.Source)).Set("sha256", JsonValue.Of(a.Sha256)));
            root.Set("art", art);
            return JsonText.Write(root, 3, new[] { "art", "masks" });
        }

        private static string KindName(TileArtKind kind) => kind == TileArtKind.Vector ? "vector" : kind == TileArtKind.Mixed ? "mixed" : "bitmap";
        private static TileArtKind ParseKind(string kind, string ctx) => kind switch
        {
            "vector" => TileArtKind.Vector, "bitmap" => TileArtKind.Bitmap, "mixed" => TileArtKind.Mixed,
            _ => throw new FormatException($"{ctx}: неизвестный тип рисунка {kind}."),
        };

        public static TileManifest Read(string text, string source)
        {
            var root = JsonText.Parse(text, source);
            if (root.Get("format")?.AsString(source) != FormatName || root.Get("version")?.AsLong(source) != CurrentVersion)
                throw new FormatException($"{source}: ожидался {FormatName} версии {CurrentVersion}.");
            var manifest = new TileManifest
            {
                QuarterPixels = (int)root.Get("quarterPixels").AsLong(source + ".quarterPixels"),
                CellPixels = (int)root.Get("cellPixels").AsLong(source + ".cellPixels"),
            };
            foreach (var v in root.Get("masks").Items)
            {
                var mask = new TileMask
                {
                    Name = v.Get("name").AsString(source + ".masks.name"), Kind = v.Get("kind").AsString(source + ".masks.kind"),
                    Symbol = (int)v.Get("symbol").AsLong(source + ".masks.symbol"), Clip = (int)(v.Get("clip")?.AsLong(source + ".masks.clip") ?? 0),
                };
                var quarters = v.Get("quarters");
                if (quarters != null)
                    foreach (var q in quarters.Items)
                        mask.Quarters.Add(new TileMaskQuarter
                        {
                            Quarter = q.Get("quarter").AsString(source + ".quarter"), Clip = (int)q.Get("clip").AsLong(source + ".clip"),
                            FlipX = q.Get("flipX").Bool, FlipY = q.Get("flipY").Bool,
                        });
                manifest.Masks.Add(mask.Name, mask);
            }
            foreach (var v in root.Get("art").Items)
            {
                string id = v.Get("id").AsString(source + ".art.id");
                string ctx = source + ":" + id;
                manifest.Art.Add(id, new TileArt
                {
                    Id = id, Kind = ParseKind(v.Get("kind").AsString(ctx + ".kind"), ctx),
                    File = v.Get("file").AsString(ctx + ".file"), Width = (int)v.Get("width").AsLong(ctx + ".width"), Height = (int)v.Get("height").AsLong(ctx + ".height"),
                    Zoom = v.Get("zoom").AsDouble(ctx + ".zoom"), OriginX = v.Get("originX").AsDouble(ctx + ".originX"), OriginY = v.Get("originY").AsDouble(ctx + ".originY"),
                    Source = v.Get("source").AsString(ctx + ".source"), Sha256 = v.Get("sha256").AsString(ctx + ".sha256"),
                });
            }
            return manifest;
        }
    }
}
