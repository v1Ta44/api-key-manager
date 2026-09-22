using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace ApiKeyManager;

/// <summary>生成 exe 图标（--makeicon icon.ico）：渐变圆角底 + 白色钥匙。</summary>
internal static class IconFactory
{
    public static void Write(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var blobs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using var bmp = Draw(sizes[i]);
            blobs[i] = Png(bmp);
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0);            // reserved
        bw.Write((ushort)1);            // type: icon
        bw.Write((ushort)sizes.Length); // count

        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            bw.Write(dim);              // width
            bw.Write(dim);              // height
            bw.Write((byte)0);          // color count
            bw.Write((byte)0);          // reserved
            bw.Write((ushort)1);        // planes
            bw.Write((ushort)32);       // bit count
            bw.Write((uint)blobs[i].Length);
            bw.Write((uint)offset);
            offset += blobs[i].Length;
        }
        foreach (var b in blobs) bw.Write(b);
    }

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size / 256f;

            using (var path = Theme.RoundRect(new RectangleF(0, 0, size - 1, size - 1), 56f * s))
            using (var lin = new LinearGradientBrush(new Point(0, 0), new Point(size, size),
                Color.FromArgb(255, 80, 95, 232), Color.FromArgb(255, 124, 92, 240)))
            {
                g.FillPath(lin, path);
            }

            using var white = new SolidBrush(Color.White);
            using (var pen = new Pen(white, 22f * s))
            {
                g.DrawEllipse(pen, 72f * s, 92f * s, 72f * s, 72f * s);
            }
            g.FillRectangle(white, 140f * s, 118f * s, 66f * s, 24f * s);
            g.FillRectangle(white, 162f * s, 142f * s, 18f * s, 34f * s);
            g.FillRectangle(white, 188f * s, 142f * s, 18f * s, 24f * s);
        }
        return bmp;
    }

    private static byte[] Png(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}

/// <summary>解析 TTF/OTF 的 cmap 表，校验字形是否覆盖（--glyphcheck）。</summary>
internal static class TtfGlyphChecker
{
    public static bool HasGlyph(byte[] font, int codepoint)
    {
        ushort numTables = ReadU16(font, 4);
        int cmapOffset = -1;
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + i * 16;
            if (font[rec] == (byte)'c' && font[rec + 1] == (byte)'m' && font[rec + 2] == (byte)'a' && font[rec + 3] == (byte)'p')
            {
                cmapOffset = (int)ReadU32(font, rec + 8);
                break;
            }
        }
        if (cmapOffset < 0) return false;

        ushort subCount = ReadU16(font, cmapOffset + 2);
        int best = -1;
        int bestScore = -1;
        for (int i = 0; i < subCount; i++)
        {
            int rec = cmapOffset + 4 + i * 8;
            ushort platform = ReadU16(font, rec);
            ushort encoding = ReadU16(font, rec + 2);
            int sub = cmapOffset + (int)ReadU32(font, rec + 4);
            ushort format = ReadU16(font, sub);
            int score = -1;
            if (format == 12) score = (platform == 3 ? 4 : 2) + (encoding == 10 ? 2 : 0) + 6;
            else if (format == 4) score = (platform == 3 ? 4 : 2) + (encoding == 1 ? 2 : 0);
            if (score > bestScore) { bestScore = score; best = sub; }
        }
        if (best < 0) return false;

        ushort fmt = ReadU16(font, best);
        return fmt == 12 ? HasFormat12(font, best, codepoint)
             : fmt == 4 ? HasFormat4(font, best, codepoint)
             : false;
    }

    private static bool HasFormat12(byte[] f, int o, int cp)
    {
        uint groups = ReadU32(f, o + 12);
        for (uint i = 0; i < groups; i++)
        {
            int g = o + 16 + (int)i * 12;
            uint start = ReadU32(f, g);
            uint end = ReadU32(f, g + 4);
            if (cp >= start && cp <= end) return true;
        }
        return false;
    }

    private static bool HasFormat4(byte[] f, int o, int cp)
    {
        if (cp > 0xFFFF) return false;
        int segCount = ReadU16(f, o + 6) / 2;
        int endO = o + 14;
        int startO = endO + segCount * 2 + 2;
        int deltaO = startO + segCount * 2;
        int rangeO = deltaO + segCount * 2;
        for (int i = 0; i < segCount; i++)
        {
            int end = ReadU16(f, endO + i * 2);
            if (cp > end) continue;
            int start = ReadU16(f, startO + i * 2);
            if (cp < start) return false;
            int range = ReadU16(f, rangeO + i * 2);
            if (range == 0)
            {
                int delta = ReadS16(f, deltaO + i * 2);
                return ((cp + delta) & 0xFFFF) != 0;
            }
            int addr = rangeO + i * 2 + range + (cp - start) * 2;
            if (addr + 1 >= f.Length) return false;
            int gid = ReadU16(f, addr);
            if (gid == 0) return false;
            int delta2 = ReadS16(f, deltaO + i * 2);
            return ((gid + delta2) & 0xFFFF) != 0;
        }
        return false;
    }

    private static ushort ReadU16(byte[] f, int o) => (ushort)((f[o] << 8) | f[o + 1]);
    private static short ReadS16(byte[] f, int o) => (short)ReadU16(f, o);
    private static uint ReadU32(byte[] f, int o) =>
        ((uint)f[o] << 24) | ((uint)f[o + 1] << 16) | ((uint)f[o + 2] << 8) | f[o + 3];
}
