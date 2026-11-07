using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Remains.SwfTools
{
    // One implementation of the hairline rule for every exporter (RoomConvert tiles/pony, ContentExport),
    // working on FFDec swf2xml text; the caller converts back with xml2swf.
    //
    // Flash draws a stroke thinner than one pixel (LINESTYLE width under 20 twips; the original art uses
    // 1 twip for most outlines and highlights) one *screen* pixel wide at any scale, and FFDec does the
    // same at its export zoom. A raster exported with reserve (4x, 8x) therefore gets lines a quarter or an
    // eighth as wide as on screen. A raster cannot follow the screen, so such strokes are widened to one
    // screen pixel at the reference display — 1080p with the default camera (10 m high, 50 source px per
    // metre) = ReferenceScreenPixels per source pixel — divided by the scale the shape is drawn at
    // (user decisions 2026-10-10). Strokes already at least that wide stay. FFDec round trip
    // (swf2xml -> xml2swf) checked: shapes without such strokes export byte-identical.
    public static class SwfXmlPatch
    {
        public const double ReferenceScreenPixels = 2.16;
        private static readonly Regex Stroke = new Regex(@"(<item type=""LINESTYLE2?""[^>]*?\bwidth="")(\d+)""");
        private static readonly Regex Definition = new Regex(@"<item type=""Define\w*Tag""[^>]*>");
        private static readonly Regex ShapeId = new Regex(@"\bshapeId=""(\d+)""");

        public static int TwipsForScale(double scale) => Math.Max(1, (int)Math.Round(20 / (ReferenceScreenPixels * scale)));

        // twips(shapeId) is the minimum stroke width of that shape.
        public static string WidenHairlines(string xml, Func<int, int> twips, out int widened)
        {
            var output = new StringBuilder(xml.Length + 4096);
            int count = 0, last = 0;
            var starts = Definition.Matches(xml);
            for (int i = 0; i < starts.Count; i++)
            {
                int start = starts[i].Index, end = i + 1 < starts.Count ? starts[i + 1].Index : xml.Length;
                output.Append(xml, last, start - last);
                string segment = xml.Substring(start, end - start);
                var id = ShapeId.Match(starts[i].Value);
                if (id.Success)
                {
                    int minimum = twips(int.Parse(id.Groups[1].Value, CultureInfo.InvariantCulture));
                    segment = Stroke.Replace(segment, m =>
                    {
                        if (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) >= minimum) return m.Value;
                        count++;
                        return m.Groups[1].Value + minimum.ToString(CultureInfo.InvariantCulture) + "\"";
                    });
                }
                output.Append(segment);
                last = end;
            }
            output.Append(xml, last, xml.Length - last);
            widened = count;
            return output.ToString();
        }
    }
}
