using System.Buffers.Binary;

namespace Rehome;

/// <summary>
/// An exported Rehome.exe carries its scan after the end of the program: [scan bytes][length, 8 bytes][magic, 8 bytes].
/// Windows and the .NET single-file host ignore bytes past the bundle, so the exe still runs, and the scan inside goes
/// through the same checks as a scan opened from a file.
/// </summary>
internal static class ScanInExe
{
    static readonly byte[] Magic = "REHOMESC"u8.ToArray();
    const int Trailer = 16;

    /// <summary>How many bytes of the file are the program itself, and the scan after it if there is one.</summary>
    public static (long Program, byte[]? Scan) Read(string exe)
    {
        using var file = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Read(file);
    }

    public static (long Program, byte[]? Scan) Read(Stream file)
    {
        var length = file.Length;
        if (length < Trailer)
            return (length, null);
        var trailer = new byte[Trailer];
        file.Seek(-Trailer, SeekOrigin.End);
        file.ReadExactly(trailer);
        if (!trailer.AsSpan(8).SequenceEqual(Magic))
            return (length, null);

        var size = BinaryPrimitives.ReadInt64LittleEndian(trailer);
        if (size <= 0 || size > Restore.Plan.MaxFileBytes || size > length - Trailer)
            throw new RehomeException("The scan inside this ReHome.exe is damaged. Export it again on the old PC.");
        var scan = new byte[size];
        file.Seek(length - Trailer - size, SeekOrigin.Begin);
        file.ReadExactly(scan);
        return (length - Trailer - size, scan);
    }

    /// <summary>Writes the program part of <paramref name="exe"/> followed by <paramref name="scan"/>.</summary>
    public static void Write(Stream exe, long program, byte[] scan, Stream to)
    {
        exe.Seek(0, SeekOrigin.Begin);
        CopyExactly(exe, to, program);
        to.Write(scan);
        Span<byte> trailer = stackalloc byte[Trailer];
        BinaryPrimitives.WriteInt64LittleEndian(trailer, scan.LongLength);
        Magic.CopyTo(trailer[8..]);
        to.Write(trailer);
    }

    public static void CopyExactly(Stream from, Stream to, long count)
    {
        var buffer = new byte[1 << 20];
        while (count > 0)
        {
            var n = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (n == 0)
                throw new EndOfStreamException();
            to.Write(buffer, 0, n);
            count -= n;
        }
    }
}
