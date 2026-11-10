using System.Security.Cryptography;
using System.Text;

namespace Rehome;

/// <summary>
/// Every scan Rehome writes carries SHA-256 of its own bytes (with the hash field zeroed) in <c>check</c>. Opening a
/// scan recomputes it, and a mismatch refuses the file. That catches a scan damaged on the way (a half-copied USB
/// drive, a bad sector) or edited by hand.
///
/// There's no key, so it doesn't stop someone who edits the file on purpose and recomputes the hash. Nothing on the
/// drive could: the unsigned Rehome.exe beside the scan can be swapped just as easily. What stands between a changed
/// scan and a bad install is the rest of restore: only winget and Store ids, each installer's hash and signer checked.
/// </summary>
internal static class ScanSeal
{
    static readonly string Zeros = new('0', 64);

    /// <summary>The file's bytes with the hash filled in. Write exactly these bytes.</summary>
    public static byte[] Seal(Snapshot snapshot)
    {
        var bytes = Encoding.UTF8.GetBytes(Scanner.ToJson(snapshot with { Check = Zeros }));
        var at = FindOnce(bytes, Zeros) ?? throw new InvalidOperationException("The check placeholder appears more than once.");
        Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(bytes))).CopyTo(bytes, at);
        return bytes;
    }

    /// <summary>True only when the hash is well formed, appears exactly once, and matches the bytes around it.</summary>
    public static bool Matches(byte[] fileBytes, string? check)
    {
        if (check is not { Length: 64 } || !check.All(Uri.IsHexDigit))
            return false;
        var copy = (byte[])fileBytes.Clone();
        if (FindOnce(copy, check) is not { } at)
            return false;
        Encoding.ASCII.GetBytes(Zeros).CopyTo(copy, at);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(copy), Convert.FromHexString(check));
    }

    static int? FindOnce(byte[] haystack, string ascii)
    {
        var needle = Encoding.ASCII.GetBytes(ascii);
        var span = haystack.AsSpan();
        var first = span.IndexOf(needle);
        if (first < 0)
            return null;
        return span[(first + 1)..].IndexOf(needle) >= 0 ? null : first;
    }
}
