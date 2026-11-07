using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Remains.RoomFormat;

namespace Remains.RoomConvert
{
    // Batch export of terrain art (docs/pipelines/room-format.md, stage B1):
    //  - shape masks: genuine vector, FFDec raster at 8x, registration from the SVG export;
    //  - tile front sprites (ladders/stairs): bitmap fills, FFDec raster at 1x (native pixels);
    //  - material textures: embedded bitmaps copied byte-exact from FFDec image export.
    // Every size is checked against its source; nothing is resampled or silently cropped.
    public static class TileExport
    {
        private const double MaskZoom = 8;
        // Reserve of decor, objects and backdrop clips holding vector shapes (pure vector or mixed with
        // bitmaps): 1x = 50 px/m, the default camera needs ~2.2x at 1080p, ~4.3x at 4K. No size cap here:
        // the full export is the source, the in-game size limit is the Unity import Max Size.
        private const double ArtZoom = 4;
        private static readonly Regex SvgSize = new Regex(@"height=""([0-9.]+)px"" width=""([0-9.]+)px""");
        private static readonly Regex SvgOrigin = new Regex(@"<g transform=""matrix\(1\.0, 0\.0, 0\.0, 1\.0, (-?[0-9.]+), (-?[0-9.]+)\)""");

        public static int Run(Dictionary<string, string> o, Func<Dictionary<string, string>, string, string> required)
        {
            string ffdec = required(o, "ffdec"), dlc = required(o, "dlc"), outDir = required(o, "out");
            string masksFile = required(o, "masks"), definitionsFile = required(o, "definitions");
            string work = Path.Combine(Path.GetTempPath(), "remains-tile-export-" + Guid.NewGuid().ToString("N"));
            var issues = new List<string>();
            try
            {
                var table = JsonText.Parse(File.ReadAllText(masksFile, Encoding.UTF8), masksFile);
                var manifest = new TileManifest
                {
                    QuarterPixels = (int)table.Get("quarterPixels").AsLong("quarterPixels"),
                    CellPixels = (int)table.Get("cellPixels").AsLong("cellPixels"),
                };
                foreach (var entry in table.Get("masks").Fields) manifest.Masks.Add(entry.Key, ReadMask(entry.Key, entry.Value));
                int front = (int)table.Get("frontSprites").Get("symbol").AsLong("frontSprites.symbol");
                var clips = manifest.Masks.Values.SelectMany(m => m.Kind == "whole" ? new[] { m.Clip } : m.Quarters.Select(q => q.Clip)).Distinct().OrderBy(i => i).ToList();
                string ui = Path.Combine(dlc, "pfeUI.swf");
                // Vector art is rendered from copies with hairlines widened (HairlineSwf); bitmaps and symbol
                // tables are read from the originals.
                var wide = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string Wide(string swf)
                {
                    if (wide.TryGetValue(swf, out string copy)) return copy;
                    var (file, widened) = HairlineSwf.Widen(ffdec, swf, Path.Combine(work, "hairlines"), _ => HairlineSwf.TwipsForScale(1));   // room art is drawn at scale 1
                    Console.WriteLine($"Волосяные линии {Path.GetFileName(swf)}: расширено {widened}.");
                    wide.Add(swf, file);
                    return file;
                }

                Ffdec(ffdec, string.Join(",", clips), MaskZoom, "sprite:png", "sprite", Path.Combine(work, "mask-png"), Wide(ui));
                Ffdec(ffdec, string.Join(",", clips.Append(front)), 1, "sprite:svg", "sprite", Path.Combine(work, "svg"), Wide(ui));
                Ffdec(ffdec, front.ToString(CultureInfo.InvariantCulture), 1, "sprite:png", "sprite", Path.Combine(work, "front-png"), Wide(ui));

                Directory.CreateDirectory(Path.Combine(outDir, "masks"));
                foreach (int clip in clips)
                {
                    var svgs = Frames(Path.Combine(work, "svg"), clip, ".svg");
                    var pngs = Frames(Path.Combine(work, "mask-png"), clip, ".png");
                    if (svgs.Count != pngs.Count || svgs.Count == 0) throw new InvalidDataException($"Клип {clip}: кадров SVG {svgs.Count}, PNG {pngs.Count}.");
                    for (int f = 1; f <= svgs.Count; f++)
                    {
                        string svg = File.ReadAllText(svgs[f - 1], Encoding.UTF8);
                        if (svg.Contains("<image")) throw new InvalidDataException($"Маска-клип {clip} кадр {f} содержит растр: экспорт 8x не даст деталей.");
                        var (w, h, ox, oy) = Geometry(svg, $"клип {clip} кадр {f}");
                        string target = Path.Combine("masks", $"clip{clip}-f{f}.png");
                        var art = Copy(pngs[f - 1], Path.Combine(outDir, target), TileManifest.MaskFrameId(clip, f), TileArtKind.Vector, MaskZoom, ox, oy, $"DLC/pfeUI.swf sprite {clip} frame {f}");
                        Expect(art, (int)Math.Ceiling(w * MaskZoom - 1e-9), (int)Math.Ceiling(h * MaskZoom - 1e-9));
                        manifest.Art.Add(art.Id, art);
                    }
                }

                Directory.CreateDirectory(Path.Combine(outDir, "front"));
                var frontSvgs = Frames(Path.Combine(work, "svg"), front, ".svg");
                var frontPngs = Frames(Path.Combine(work, "front-png"), front, ".png");
                for (int f = 1; f <= frontSvgs.Count; f++)
                {
                    var (w, h, ox, oy) = Geometry(File.ReadAllText(frontSvgs[f - 1], Encoding.UTF8), $"tileFront кадр {f}");
                    var art = Copy(frontPngs[f - 1], Path.Combine(outDir, "front", $"front-f{f}.png"), TileManifest.FrontFrameId(f), TileArtKind.Bitmap, 1, ox, oy, $"DLC/pfeUI.swf sprite {front} frame {f} (bitmap fill, 1:1)");
                    art.File = Path.Combine("front", $"front-f{f}.png").Replace('\\', '/');
                    Expect(art, (int)Math.Ceiling(w - 1e-9), (int)Math.Ceiling(h - 1e-9));
                    manifest.Art.Add(art.Id, art);
                }

                var definitions = RoomDefinitionCatalog.Read(File.ReadAllText(definitionsFile, Encoding.UTF8), definitionsFile);
                var wanted = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var d in definitions.Items.Values)
                    foreach (string id in new[] { d.MainTexture, d.AltTexture, d.BorderTexture, d.FloorTexture })
                        if (id != null) wanted.Add(id.Substring("texture:".Length));
                // Room backdrops (options.backwall, land default) and far backgrounds (fon): the same
                // texture pool; "sky" means no backdrop. Collected from lands and every room file.
                var lands = RoomLandCatalog.Read(File.ReadAllText(required(o, "lands"), Encoding.UTF8), required(o, "lands"));
                void Backdrop(string name) { if (!string.IsNullOrEmpty(name) && name != "sky") wanted.Add(name); }
                Backdrop("fonDefault");   // original drawFon: the far background when a land names none
                foreach (var land in lands.Lands) { land.Options.TryGetValue("backwall", out string b); land.Options.TryGetValue("fon", out string f); Backdrop(b); Backdrop(f); }
                foreach (string file in Directory.GetFiles(required(o, "rooms-dir"), "*.room.json", SearchOption.AllDirectories))
                {
                    var room = RoomSerializer.Read(File.ReadAllText(file, Encoding.UTF8), file);
                    room.Options.TryGetValue("backwall", out string b); room.Options.TryGetValue("fon", out string f); Backdrop(b); Backdrop(f);
                }
                Directory.CreateDirectory(Path.Combine(outDir, "textures"));
                var found = new Dictionary<string, (string file, string swf)>(StringComparer.Ordinal);
                var spriteSymbols = new Dictionary<string, (string id, string swf)>(StringComparer.Ordinal);
                foreach (string swf in new[] { "texture.swf", "texture1.swf" })
                {
                    string dir = Path.Combine(work, "tex-" + Path.GetFileNameWithoutExtension(swf));
                    Ffdec(ffdec, null, 1, "image:png_gif_jpeg", "image,symbolClass", dir, Path.Combine(dlc, swf));
                    var names = ReadSymbolClass(dir);
                    var imageIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string image in Directory.GetFiles(Path.Combine(dir, "images")))
                    {
                        string stem = Path.GetFileNameWithoutExtension(image);
                        string id = stem.Split('_')[0];
                        imageIds.Add(id);
                        if (names.TryGetValue(id, out string name) && wanted.Contains(name) && !found.ContainsKey(name)) found.Add(name, (image, swf));
                    }
                    // A class that is not an image is a clip (e.g. fonDarkClouds): exported below as a sprite.
                    foreach (var entry in names)
                        if (!imageIds.Contains(entry.Key) && wanted.Contains(entry.Value) && !spriteSymbols.ContainsKey(entry.Value)) spriteSymbols.Add(entry.Value, (entry.Key, swf));
                }
                foreach (var clip in spriteSymbols)
                {
                    if (found.ContainsKey(clip.Key)) continue;
                    string swfPath = Path.Combine(dlc, clip.Value.swf);
                    string svgDir = Path.Combine(work, "clip-svg-" + clip.Value.id), pngDir = Path.Combine(work, "clip-png-" + clip.Value.id);
                    Ffdec(ffdec, clip.Value.id, 1, "sprite:svg", "sprite", svgDir, Wide(swfPath));
                    int id = int.Parse(clip.Value.id, CultureInfo.InvariantCulture);
                    var svgFrames = Frames(svgDir, id, ".svg");
                    string svg = File.ReadAllText(svgFrames[0], Encoding.UTF8);
                    var kind = ArtKind(svg);
                    double zoom = ReserveZoom(kind);
                    Ffdec(ffdec, clip.Value.id, zoom, "sprite:png", "sprite", pngDir, Wide(swfPath));
                    var (w, h, ox, oy) = Geometry(svg, clip.Key);
                    var art = Copy(Frames(pngDir, id, ".png")[0], Path.Combine(outDir, "textures", clip.Key + ".png"), TileManifest.TextureId(clip.Key),
                        kind, zoom, ox, oy, $"DLC/{clip.Value.swf} sprite {clip.Value.id} {clip.Key} frame 1" + KindNote(kind));
                    art.File = "textures/" + clip.Key + ".png";
                    Expect(art, (int)Math.Ceiling(w * zoom - 1e-9), (int)Math.Ceiling(h * zoom - 1e-9));
                    manifest.Art.Add(art.Id, art);
                    if (svgFrames.Count > 1) issues.Add($"{clip.Key}: у клипа {svgFrames.Count} кадров, выгружен первый.");
                }
                foreach (string name in wanted)
                {
                    if (spriteSymbols.ContainsKey(name) && !found.ContainsKey(name)) continue;
                    if (!found.TryGetValue(name, out var source)) { issues.Add($"Текстура {name} не найдена в texture.swf/texture1.swf."); continue; }
                    string ext = Path.GetExtension(source.file).ToLowerInvariant();
                    var art = Copy(source.file, Path.Combine(outDir, "textures", name + ext), TileManifest.TextureId(name), TileArtKind.Bitmap, 1, 0, 0, $"DLC/{source.swf} image {name}");
                    art.File = Path.Combine("textures", name + ext).Replace('\\', '/');
                    manifest.Art.Add(art.Id, art);
                }

                var haloOnly = new HashSet<string>(StringComparer.Ordinal);
                var (decorClips, objectClips) = ExportWorldArt(ffdec, Path.Combine(dlc, "texture1.swf"), Wide(Path.Combine(dlc, "texture1.swf")), work, outDir, manifest, issues, haloOnly);
                // Coverage: decor and furniture/doors must have art. Light-zone decor has only a halo (_l):
                // in the new lighting it is a light/shade source, not a picture. Other object types
                // (units, pickups, traps, checkpoints, markers) are drawn by their own systems.
                var lightZones = new List<string>();
                var otherObjects = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var d in definitions.Items.Values)
                {
                    if (d.Kind == RoomDefinitionKind.Decor && !manifest.Art.ContainsKey(TileManifest.DecorFrameId(d.Art, 1)))
                    {
                        if (haloOnly.Contains(d.Art)) lightZones.Add(d.Art);
                        else issues.Add($"{d.Id}: нет рисунка back_{d.Art}_t.");
                    }
                    if (d.Kind == RoomDefinitionKind.Object && !manifest.Art.ContainsKey(TileManifest.ObjectFrameId(d.Art, 1)))
                    {
                        if (d.ObjectType == "box" || d.ObjectType == "door") issues.Add($"{d.Id}: нет рисунка vis{d.Art}.");
                        else otherObjects[d.ObjectType ?? "?"] = otherObjects.TryGetValue(d.ObjectType ?? "?", out int n) ? n + 1 : 1;
                    }
                }
                Console.WriteLine($"Декор-светозоны без рисунка (только ореол _l): {lightZones.Count} ({string.Join(", ", lightZones)}). Объекты без рисунка по типам (своя отрисовка): {string.Join(", ", otherObjects.Select(e => e.Key + " " + e.Value))}.");

                foreach (var d in definitions.Items.Values)
                {
                    foreach (string mask in new[] { d.MainMask, d.BorderMask, d.FloorMask })
                        if (mask != null && !manifest.Masks.ContainsKey(mask)) issues.Add($"{d.Id}: маска {mask} не экспортирована.");
                    if (d.FrontSprite != null && !manifest.Art.ContainsKey(d.FrontSprite)) issues.Add($"{d.Id}: нет спрайта {d.FrontSprite}.");
                }
                File.WriteAllText(Path.Combine(outDir, "tiles-manifest.json"), manifest.Write(), new UTF8Encoding(false));
                int vector = manifest.Art.Keys.Count(k => k.StartsWith("mask:", StringComparison.Ordinal));
                Console.WriteLine($"Экспорт графики клеток: масок {manifest.Masks.Count}, кадров масок {vector} (вектор, 8x), спрайтов {frontSvgs.Count} (растр 1:1), текстур-картинок {found.Count} (исходные байты), фонов-клипов {spriteSymbols.Count}, всего запрошено {wanted.Count}; декор {decorClips} клипов, объекты {objectClips} клипов.");
                foreach (string issue in issues) Console.WriteLine("  " + issue);
                return issues.Count == 0 ? 0 : 3;
            }
            finally
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
        }

        // Decor (back_<name>_t drawing, back_<name>_e background erase; halos _l are not used — real
        // lights replace them) and objects (vis<name>) from texture1.swf, every frame. The kind is decided
        // per clip: bitmap-only clips are exported 1:1, clips with vector shapes (vector or mixed) at ArtZoom.
        private static (int decor, int objects) ExportWorldArt(string ffdec, string swf, string render, string work, string outDir, TileManifest manifest, List<string> issues, HashSet<string> haloOnly)
        {
            string symbols = Path.Combine(work, "world-symbols");
            Ffdec(ffdec, null, 1, "", "symbolClass", symbols, swf);
            var clips = new List<(int id, string name, string kind)>();
            foreach (string line in File.ReadAllLines(Directory.GetFiles(symbols, "*.csv", SearchOption.AllDirectories).Single(), Encoding.UTF8))
            {
                var m = Regex.Match(line, @"^(\d+);""?([^""]*)""?$");
                if (!m.Success) continue;
                int id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var decor = Regex.Match(m.Groups[2].Value, @"^back_(.+)_(t|e)$");
                var obj = Regex.Match(m.Groups[2].Value, @"^vis(.+)$");
                if (decor.Success) clips.Add((id, decor.Groups[1].Value, decor.Groups[2].Value == "t" ? "decor" : "decor-erase"));
                var halo = Regex.Match(m.Groups[2].Value, @"^back_(.+)_l$");
                if (halo.Success) haloOnly.Add(halo.Groups[1].Value);
                else if (obj.Success) clips.Add((id, obj.Groups[1].Value, "object"));
            }
            string svgDir = Path.Combine(work, "world-svg");
            Ffdec(ffdec, string.Join(",", clips.Select(c => c.id)), 1, "sprite:svg", "sprite", svgDir, render);
            var zooms = new Dictionary<int, double>(); var kinds = new Dictionary<int, TileArtKind>();
            foreach (var c in clips)
            {
                var svgs = Frames(svgDir, c.id, ".svg");
                string all = string.Concat(svgs.Select(f => File.ReadAllText(f, Encoding.UTF8)));
                kinds[c.id] = ArtKind(all);
                zooms[c.id] = ReserveZoom(kinds[c.id]);
            }
            foreach (var group in clips.GroupBy(c => zooms[c.id]))
                Ffdec(ffdec, string.Join(",", group.Select(c => c.id)), group.Key, "sprite:png", "sprite", Path.Combine(work, "world-png-" + group.Key.ToString(CultureInfo.InvariantCulture)), render);

            foreach (var c in clips)
            {
                double zoom = zooms[c.id];
                var svgs = Frames(svgDir, c.id, ".svg");
                var pngs = Frames(Path.Combine(work, "world-png-" + zoom.ToString(CultureInfo.InvariantCulture)), c.id, ".png");
                if (svgs.Count != pngs.Count) { issues.Add($"{c.kind} {c.name}: кадров SVG {svgs.Count}, PNG {pngs.Count} — клип пропущен."); continue; }
                string folder = c.kind == "object" ? "objects" : "decor";
                // FFDec draws every frame on one canvas: all frames' bounds plus filter margins.
                var clipSvgs = svgs.Select(f => File.ReadAllText(f, Encoding.UTF8)).ToList();
                bool clipFilter = clipSvgs.Any(t => t.Contains("<filter"));
                bool clipOffset = clipSvgs.Any(t => Regex.IsMatch(t, @"<feOffset dx=""(?!0\.0"")|<feOffset dx=""[^""]*"" dy=""(?!0\.0"")"));
                if (clipFilter && clipOffset) issues.Add($"{c.kind} {c.name}: фильтр со смещением — привязка по центру расширения холста может быть неточной.");
                for (int f = 1; f <= svgs.Count; f++)
                {
                    string svg = File.ReadAllText(svgs[f - 1], Encoding.UTF8);
                    var (w, h, ox, oy) = Geometry(svg, $"{c.kind} {c.name} кадр {f}");
                    bool empty = w * zoom < 0.5 || h * zoom < 0.5;   // e.g. a lamp's "off" frame draws nothing; FFDec writes 1×1
                    string id = c.kind == "object" ? TileManifest.ObjectFrameId(c.name, f) : c.kind == "decor" ? TileManifest.DecorFrameId(c.name, f) : TileManifest.DecorEraseFrameId(c.name, f);
                    string file = $"{c.name}{(c.kind == "decor-erase" ? "-erase" : "")}-f{f}.png";
                    var art = Copy(pngs[f - 1], Path.Combine(outDir, folder, file), id, kinds[c.id], zoom, ox, oy,
                        $"DLC/texture1.swf sprite {c.id} {(c.kind == "object" ? "vis" + c.name : "back_" + c.name + (c.kind == "decor" ? "_t" : "_e"))} frame {f}" + (empty ? " (empty frame)" : KindNote(kinds[c.id])));
                    int ew = (int)Math.Ceiling(w * zoom - 1e-9), eh = (int)Math.Ceiling(h * zoom - 1e-9);
                    if (empty) Expect(art, 1, 1);
                    else if (clipFilter && art.Width >= ew && art.Height >= eh)
                    {
                        // Filters (glow, shadow) widen the rendered canvas, the SVG size excludes them:
                        // centred margins move the registration point.
                        art.OriginX += (art.Width - ew) / 2.0 / zoom;
                        art.OriginY += (art.Height - eh) / 2.0 / zoom;
                    }
                    else Expect(art, ew, eh);
                    manifest.Art.Add(art.Id, art);
                }
            }
            haloOnly.ExceptWith(clips.Where(c => c.kind == "decor").Select(c => c.name));
            return (clips.Count(c => c.kind != "object"), clips.Count(c => c.kind == "object"));
        }

        private static TileMask ReadMask(string name, JsonValue v)
        {
            var mask = new TileMask { Name = name, Kind = v.Get("kind").AsString(name + ".kind"), Symbol = (int)v.Get("symbol").AsLong(name + ".symbol") };
            if (mask.Kind == "whole") mask.Clip = (int)v.Get("clip").AsLong(name + ".clip");
            else foreach (var q in v.Get("quarters").Fields)
                    mask.Quarters.Add(new TileMaskQuarter { Quarter = q.Key, Clip = (int)q.Value.Get("clip").AsLong(name + ".clip"), FlipX = q.Value.Get("flipX").Bool, FlipY = q.Value.Get("flipY").Bool });
            return mask;
        }

        // Clip content from its FFDec SVG frames: embedded bitmaps are <image>, vector shapes are paths with
        // a solid fill or a stroke, or gradients (bitmap fills are paths with a url(#pattern) fill). Agrees with
        // the SWF fill-style classification of Tools/ContentExport on all texture1 decor/object symbols.
        private static TileArtKind ArtKind(string svg)
        {
            bool bitmap = svg.Contains("<image"), vector = Regex.IsMatch(svg, @"<path\b[^>]*(fill=""#|stroke=)") || svg.Contains("Gradient");
            return vector ? bitmap ? TileArtKind.Mixed : TileArtKind.Vector : TileArtKind.Bitmap;
        }
        // Hairlines are already widened in the rendered SWF copy (HairlineSwf), so every clip with vector
        // shapes takes the reserve.
        private static double ReserveZoom(TileArtKind kind) => kind == TileArtKind.Bitmap ? 1 : ArtZoom;
        private static string KindNote(TileArtKind kind) => kind == TileArtKind.Bitmap ? " (bitmap, 1:1)" : kind == TileArtKind.Mixed ? " (mixed)" : "";

        private static void Ffdec(string jar, string ids, double zoom, string format, string export, string outDir, string swf)
        {
            Directory.CreateDirectory(outDir);
            var args = new List<string>();
            if (ids != null) { args.Add("-selectid"); args.Add(ids); }
            args.AddRange(new[] { "-zoom", zoom.ToString(CultureInfo.InvariantCulture) });
            if (!string.IsNullOrEmpty(format)) args.AddRange(new[] { "-format", format });
            args.AddRange(new[] { "-export", export, outDir, swf });
            RunFfdec(jar, args.ToArray());
        }

        // Java executable for FFDec: --java <path> (tiles, widen-hairlines), otherwise "java" from PATH.
        internal static string Java = "java";

        internal static void RunFfdec(string jar, params string[] args)
        {
            var info = new ProcessStartInfo(Java) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in new[] { "-Djava.awt.headless=true", "-Xmx4g", "-jar", jar }.Concat(args)) info.ArgumentList.Add(a);
            using (var p = Process.Start(info))
            {
                var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();   // both at once: no pipe deadlock
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException($"FFDec завершился с кодом {p.ExitCode}: {stderr.Result}{stdout.Result}");
            }
        }

        private static List<string> Frames(string dir, int id, string ext)
        {
            var folder = Directory.GetDirectories(dir).SingleOrDefault(d => Regex.IsMatch(Path.GetFileName(d), $@"^DefineSprite_{id}(_|$)"))
                ?? throw new InvalidDataException($"FFDec не выгрузил спрайт {id} в {dir}.");
            return Directory.GetFiles(folder, "*" + ext).OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f), CultureInfo.InvariantCulture)).ToList();
        }

        private static (double w, double h, double ox, double oy) Geometry(string svg, string context)
        {
            var size = SvgSize.Match(svg);
            var origin = SvgOrigin.Match(svg);
            if (!size.Success || !origin.Success) throw new InvalidDataException($"{context}: не найдены размер или точка привязки в SVG (ожидается перенос без поворота).");
            double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
            return (D(size.Groups[2]), D(size.Groups[1]), D(origin.Groups[1]), D(origin.Groups[2]));
        }

        private static TileArt Copy(string from, string to, string id, TileArtKind kind, double zoom, double ox, double oy, string source)
        {
            byte[] bytes = File.ReadAllBytes(from);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.WriteAllBytes(to, bytes);
            var (w, h) = ImageSize(bytes, from);
            string relative = Path.Combine(Path.GetFileName(Path.GetDirectoryName(to)), Path.GetFileName(to)).Replace('\\', '/');
            using (var sha = SHA256.Create())
                return new TileArt
                {
                    Id = id, File = relative, Kind = kind, Zoom = zoom, OriginX = ox, OriginY = oy, Source = source, Width = w, Height = h,
                    Sha256 = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))),
                };
        }

        private static void Expect(TileArt art, int width, int height)
        {
            if (art.Width != width || art.Height != height)
                throw new InvalidDataException($"{art.Id}: размер {art.Width}x{art.Height}, ожидался {width}x{height} (масштаб {art.Zoom}).");
        }

        private static (int, int) ImageSize(byte[] b, string file)
        {
            if (b.Length > 24 && b[0] == 0x89 && b[1] == 'P') return (BigEndian(b, 16), BigEndian(b, 20));
            if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
            {
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    byte marker = b[i + 1];
                    int length = (b[i + 2] << 8) | b[i + 3];
                    if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                        return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                    i += 2 + length;
                }
            }
            if (b.Length > 10 && b[0] == 'G' && b[1] == 'I') return (b[6] | (b[7] << 8), b[8] | (b[9] << 8));
            throw new InvalidDataException($"{file}: неизвестный формат изображения.");
        }

        private static int BigEndian(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        private static Dictionary<string, string> ReadSymbolClass(string dir)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string csv = Directory.GetFiles(Path.Combine(dir, "symbolClass"), "*.csv").SingleOrDefault()
                ?? throw new InvalidDataException("FFDec не выгрузил symbolClass в " + dir);
            foreach (string line in File.ReadAllLines(csv, Encoding.UTF8))
            {
                var m = Regex.Match(line, @"^(\d+);""?([^""]*)""?$");
                if (m.Success) result[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return result;
        }
    }
}
