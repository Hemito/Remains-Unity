using System;
using System.IO;
using System.IO.Compression;

namespace Remains.RoomConvert
{
    // Minimal PNG for research images: reads 8-bit RGB/RGBA non-interlaced (Unity EncodeToPNG),
    // writes 8-bit RGBA. Pixels are RGBA bytes, rows top-down.
    public sealed class PngImage
    {
        public int Width, Height;
        public byte[] Rgba;

        public static PngImage Read(string file)
        {
            byte[] data = File.ReadAllBytes(file);
            int pos = 8, width = 0, height = 0, colorType = 0;
            var idat = new MemoryStream();
            while (pos < data.Length)
            {
                int length = BigEndian(data, pos);
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                if (type == "IHDR")
                {
                    width = BigEndian(data, pos + 8); height = BigEndian(data, pos + 12);
                    byte depth = data[pos + 16]; colorType = data[pos + 17]; byte interlace = data[pos + 20];
                    if (depth != 8 || (colorType != 6 && colorType != 2) || interlace != 0)
                        throw new FormatException($"{file}: поддерживается только PNG 8 бит RGB/RGBA без чересстрочности.");
                }
                else if (type == "IDAT") idat.Write(data, pos + 8, length);
                else if (type == "IEND") break;
                pos += 12 + length;
            }
            int channels = colorType == 6 ? 4 : 3, stride = width * channels;
            byte[] raw;
            idat.Position = 0;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var output = new MemoryStream()) { z.CopyTo(output); raw = output.ToArray(); }
            var image = new PngImage { Width = width, Height = height, Rgba = new byte[width * height * 4] };
            var previous = new byte[stride];
            var row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int filter = raw[y * (stride + 1)];
                Buffer.BlockCopy(raw, y * (stride + 1) + 1, row, 0, stride);
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= channels ? row[i - channels] : 0, b = previous[i], c = i >= channels ? previous[i - channels] : 0;
                    int add = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => throw new FormatException($"{file}: фильтр PNG {filter}.") };
                    row[i] = (byte)(row[i] + add);
                }
                for (int x = 0; x < width; x++)
                {
                    int o = (y * width + x) * 4;
                    image.Rgba[o] = row[x * channels]; image.Rgba[o + 1] = row[x * channels + 1]; image.Rgba[o + 2] = row[x * channels + 2];
                    image.Rgba[o + 3] = channels == 4 ? row[x * channels + 3] : (byte)255;
                }
                (previous, row) = (row, previous);
            }
            return image;
        }

        public void Write(string file)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < Height; y++) { raw.WriteByte(0); raw.Write(Rgba, y * Width * 4, Width * 4); }
            var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) { raw.Position = 0; raw.CopyTo(z); }
            using var output = File.Create(file);
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var header = new byte[13];
            WriteBigEndian(header, 0, Width); WriteBigEndian(header, 4, Height);
            header[8] = 8; header[9] = 6;
            Chunk(output, "IHDR", header);
            Chunk(output, "IDAT", compressed.ToArray());
            Chunk(output, "IEND", Array.Empty<byte>());
        }

        private static void Chunk(Stream output, string type, byte[] body)
        {
            var head = new byte[8];
            WriteBigEndian(head, 0, body.Length);
            System.Text.Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
            output.Write(head);
            output.Write(body);
            uint crc = Crc(head, 4, 4, 0xFFFFFFFFu);
            crc = Crc(body, 0, body.Length, crc) ^ 0xFFFFFFFFu;
            var tail = new byte[4];
            WriteBigEndian(tail, 0, (int)crc);
            output.Write(tail);
        }

        private static uint[] crcTable;
        private static uint Crc(byte[] data, int offset, int count, uint crc)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    crcTable[n] = c;
                }
            }
            for (int i = offset; i < offset + count; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static int BigEndian(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
        private static void WriteBigEndian(byte[] d, int o, int v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }
    }
}
