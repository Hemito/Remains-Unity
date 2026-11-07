using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Remains.RoomFormat;
using Remains.SwfTools;

namespace Remains.RoomConvert
{
    // FFDec side of the hairline rule (Tools/Shared/SwfXmlPatch.cs holds the rule itself, shared with
    // ContentExport): swf2xml, widen, xml2swf. Room art draws at scale 1 (9 twips); pony parts at their
    // largest scale in pony.json, so shrunk mane strands get wider lines and enlarged shins thinner.
    public static class HairlineSwf
    {
        public const double ReferenceScreenPixels = SwfXmlPatch.ReferenceScreenPixels;

        public static int TwipsForScale(double scale) => SwfXmlPatch.TwipsForScale(scale);

        // Writes <work>/<name>-wide.swf; returns its path and the number of widened strokes. twips(shapeId)
        // is the minimum stroke width of that shape.
        public static (string swf, int widened) Widen(string ffdec, string swf, string work, Func<int, int> twips)
        {
            Directory.CreateDirectory(work);
            string name = Path.GetFileNameWithoutExtension(swf);
            string xml = Path.Combine(work, name + ".xml"), wideXml = Path.Combine(work, name + "-wide.xml"), wide = Path.Combine(work, name + "-wide.swf");
            TileExport.RunFfdec(ffdec, "-swf2xml", swf, xml);
            string text = SwfXmlPatch.WidenHairlines(File.ReadAllText(xml, Encoding.UTF8), twips, out int widened);
            File.WriteAllText(wideXml, text, new UTF8Encoding(false));
            TileExport.RunFfdec(ffdec, "-xml2swf", wideXml, wide);
            File.Delete(xml); File.Delete(wideXml);
            return (wide, widened);
        }

        // Largest linear scale of every shape drawn by the pony (pony.json parts: shape id + affine matrix).
        public static Dictionary<int, double> PonyScales(string ponyJson)
        {
            var scales = new Dictionary<int, double>();
            var root = JsonText.Parse(File.ReadAllText(ponyJson, Encoding.UTF8), ponyJson);
            foreach (var clip in root.Get("clips").Items)
                foreach (var frame in clip.Get("frames").Items)
                    foreach (var part in frame.Get("parts").Items)
                    {
                        int shape = int.Parse(part.Get("shape").AsString(ponyJson + ".shape"), CultureInfo.InvariantCulture);
                        var m = part.Get("matrix").Items;
                        double a = m[0].AsDouble("a"), b = m[1].AsDouble("b"), c = m[2].AsDouble("c"), d = m[3].AsDouble("d");
                        double scale = Math.Max(Math.Sqrt(a * a + b * b), Math.Sqrt(c * c + d * d));
                        scales[shape] = Math.Max(scales.TryGetValue(shape, out double s) ? s : 0, scale);
                    }
            return scales;
        }

        // CLI: widen-hairlines --ffdec <jar> --swf <in.swf> --out <out.swf> [--pony <pony.json>].
        // Every shape gets TwipsForScale(1); with --pony, shapes the pony draws get TwipsForScale(their scale).
        public static int Run(string ffdec, string swf, string output, string ponyJson)
        {
            string work = Path.Combine(Path.GetTempPath(), "remains-hairlines-" + Guid.NewGuid().ToString("N"));
            try
            {
                Func<int, int> twips = _ => TwipsForScale(1);
                string rule = $"1 пиксель экрана при {ReferenceScreenPixels.ToString(CultureInfo.InvariantCulture)} px/исходный px ({TwipsForScale(1)} твипов)";
                if (ponyJson != null)
                {
                    var scales = PonyScales(ponyJson);
                    twips = shape => TwipsForScale(scales.TryGetValue(shape, out double s) ? s : 1);
                    rule = $"1 пиксель экрана при {ReferenceScreenPixels.ToString(CultureInfo.InvariantCulture)} px/исходный px / масштаб части ({scales.Count} форм пони)";
                }
                var (wide, widened) = Widen(ffdec, swf, work, twips);
                File.Copy(wide, output, true);
                Console.WriteLine($"Волосяные линии: {swf} -> {output}; правило: {rule}; расширено {widened}; SHA256 источника {Sha(swf)}.");
                return 0;
            }
            finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
        }

        private static string Sha(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
