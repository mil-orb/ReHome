using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Rehome.Shell.Native;

/// <summary>An icon from a file, as PNG bytes. Same source as Explorer's: the icon resource inside the file.</summary>
static unsafe partial class IconPng
{
    /// <param name="index">Icon index, or a resource id when negative ("app.exe,-101").</param>
    public static byte[]? FromFile(string path, int index, int size)
    {
        nint icon = 0;
        if (SHDefExtractIconW(path, index, 0, &icon, null, (uint)size) != 0 || icon == 0)
            return null;
        try
        {
            return Encode(icon);
        }
        finally
        {
            Win32.DestroyIcon(icon);
        }
    }

    /// <summary>
    /// The icon's pixels with their alpha. Older icons have no alpha channel and use a mask for transparency
    /// instead, so the mask fills in alpha when every pixel's alpha is zero.
    /// </summary>
    static byte[]? Encode(nint icon)
    {
        if (!GetIconInfo(icon, out var info))
            return null;
        try
        {
            if (info.Color == 0 || GetObjectW(info.Color, sizeof(Bitmap), out var bm) == 0)
                return null; // a black and white icon. Rare enough to show a letter instead.

            int w = bm.Width, h = bm.Height;
            if (w <= 0 || h <= 0 || w > 256 || h > 256)
                return null;

            var bgra = Pixels(info.Color, w, h);
            if (bgra is null)
                return null;

            if (!HasAlpha(bgra) && info.Mask != 0 && Pixels(info.Mask, w, h) is { } mask)
                for (var i = 0; i < w * h; i++)
                    bgra[i * 4 + 3] = (byte)(mask[i * 4] == 0 ? 255 : 0); // black in the mask means opaque

            return Png(bgra, w, h);
        }
        finally
        {
            if (info.Color != 0) Win32.DeleteObject(info.Color);
            if (info.Mask != 0) Win32.DeleteObject(info.Mask);
        }
    }

    static bool HasAlpha(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
            if (bgra[i] != 0)
                return true;
        return false;
    }

    /// <summary>Top-down 32-bit BGRA rows of a bitmap.</summary>
    static byte[]? Pixels(nint bitmap, int w, int h)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = w,
            Height = -h, // negative: rows run top to bottom
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[w * h * 4];
        var dc = GetDC(0);
        try
        {
            fixed (byte* p = pixels)
                return GetDIBits(dc, bitmap, 0, (uint)h, p, &header, 0) == h ? pixels : null;
        }
        finally
        {
            ReleaseDC(0, dc);
        }
    }

    static byte[] Png(byte[] bgra, int w, int h)
    {
        // Each row: a filter byte (0, none), then RGBA.
        var raw = new byte[h * (1 + w * 4)];
        for (int y = 0, o = 0; y < h; y++)
        {
            raw[o++] = 0;
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                raw[o++] = bgra[i + 2];
                raw[o++] = bgra[i + 1];
                raw[o++] = bgra[i];
                raw[o++] = bgra[i + 3];
            }
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, w);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), h);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static void Chunk(Stream png, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);

        Span<byte> name = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
            name[i] = (byte)type[i];
        png.Write(name);
        png.Write(data);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32.Of(name, data));
        png.Write(number);
    }

    /// <summary>The CRC-32 PNG chunks end with (ISO 3309, polynomial 0xEDB88320).</summary>
    static class Crc32
    {
        static readonly uint[] Table = Build();

        static uint[] Build()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        public static uint Of(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var c = 0xFFFFFFFFu;
            foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo { public int IsIcon, HotspotX, HotspotY; public nint Mask, Color; }

    [StructLayout(LayoutKind.Sequential)]
    struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
        // One spare color slot, so the call has room for a BITMAPINFO's color table.
        public uint Spare;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHDefExtractIconW(string iconFile, int index, uint flags, nint* large, nint* small, uint size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out IconInfo info);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint obj, int size, out Bitmap bitmap);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte* bits, BitmapInfoHeader* info, uint usage);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);
}
