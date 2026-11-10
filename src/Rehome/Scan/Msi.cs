using System.Runtime.InteropServices;

namespace Rehome.Scan;

internal static unsafe partial class Msi
{
    /// <summary>Windows keeps a signed copy of every installed MSI. Returns its path, or null for non-MSI installs.</summary>
    public static string? CachedPackage(string? productCode)
    {
        if (productCode is null || productCode.Length != 38 || productCode[0] != '{')
            return null;

        var buffer = stackalloc char[MaxPath];
        var length = (uint)MaxPath;
        if (MsiGetProductInfoW(productCode, "LocalPackage", buffer, &length) != 0 || length == 0)
            return null;

        var path = new string(buffer, 0, (int)length);
        return path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    const int MaxPath = 1024;

    [LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint MsiGetProductInfoW(string product, string property, char* value, uint* length);
}
