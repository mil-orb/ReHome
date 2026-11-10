using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Rehome.Scan;

namespace Rehome.Shell;

/// <summary>
/// Each app's own icon as a small PNG data URI, for display only. Never written to a scan file.
/// Read from the app's own installed files, so it's the vendor's icon: the MSIX package logo, the uninstall
/// entry's icon, or the icon inside the app's main exe. Nothing is fetched from the network.
/// </summary>
static class AppIcons
{
    const int Size = 32;

    public static IReadOnlyDictionary<string, string> For(IReadOnlyList<App> apps)
    {
        var icons = new ConcurrentDictionary<string, string>();
        var packages = new Windows.Management.Deployment.PackageManager();

        Parallel.ForEach(apps, new ParallelOptions { MaxDegreeOfParallelism = 8 }, app =>
        {
            try
            {
                var png = app.LocalId.StartsWith(@"MSIX\", StringComparison.Ordinal)
                    ? FromPackage(packages, app.LocalId[5..])
                    : FromUninstallEntry(app.LocalId) ?? FromMainExecutable(app);

                if (png is { Length: > 0 and < 200_000 })
                    icons[app.LocalId] = "data:image/png;base64," + Convert.ToBase64String(png);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                          or ExternalException or COMException or InvalidOperationException)
            {
                // No icon is fine. The UI shows a letter instead.
            }
        });

        return icons;
    }

    static byte[]? FromUninstallEntry(string localId)
    {
        var (path, index) = ParseIcon(Inventory.ReadArpValue(localId, "DisplayIcon"));
        return FromFile(path, index);
    }

    static byte[]? FromMainExecutable(App app) =>
        FromFile(Inventory.MainExecutable(app.LocalId, Inventory.ReadArpValue(app.LocalId, "InstallLocation"), app.Name), 0);

    static byte[]? FromFile(string? path, int index)
    {
        if (path is null || !File.Exists(path))
            return null;

        using var icon = Icon.ExtractIcon(path, index, Size);
        if (icon is null)
            return null;

        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    static byte[]? FromPackage(Windows.Management.Deployment.PackageManager packages, string fullName)
    {
        var package = packages.FindPackageForUser("", fullName);
        if (package is null)
            return null;

        var reference = package.GetLogoAsRandomAccessStreamReference(new Windows.Foundation.Size(Size, Size));
        using var stream = reference.OpenReadAsync().AsTask().GetAwaiter().GetResult();
        if (stream.Size is 0 or > 200_000)
            return null;

        var bytes = new byte[stream.Size];
        stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, Windows.Storage.Streams.InputStreamOptions.None)
            .AsTask().GetAwaiter().GetResult();
        return bytes;
    }

    /// <summary>"C:\App\app.exe,-101" → (path, -101). Negative numbers are resource ids, which ExtractIcon also takes.</summary>
    internal static (string? Path, int Index) ParseIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, 0);

        var s = value.Trim();
        var index = 0;
        var comma = s.LastIndexOf(',');
        if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), out var i))
        {
            index = i;
            s = s[..comma];
        }

        s = Environment.ExpandEnvironmentVariables(s.Trim().Trim('"'));
        return (s.Length > 0 ? s : null, index);
    }
}
