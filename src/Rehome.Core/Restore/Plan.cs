using System.Text.RegularExpressions;

namespace Rehome.Restore;

/// <summary>
/// A scan file opened on the new machine. Everything in it is treated as untrusted input:
/// it was written on another PC and may have been edited since. Only fields that pass the
/// checks here ever reach winget, and they reach it as separate arguments, never through a shell.
/// </summary>
internal static partial class Plan
{
    public const long MaxFileBytes = 10 << 20;
    const int MaxIconChars = 280_000;

    /// <summary>
    /// A file the person picked or dropped: an exported ReHome.exe (the scan appended to it is read, the program never
    /// runs) or a scan .json. Anything else is refused by name before a byte is parsed.
    /// </summary>
    public static Snapshot Open(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return Load(path);
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new RehomeException("ReHome opens the ReHome installer you exported on the old PC, or a scan .json file.");
        byte[]? scan;
        try
        {
            scan = ScanInExe.Read(path).Scan;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new RehomeException("Couldn't read that file. Check it isn't open somewhere else.");
        }
        return Parse(scan ?? throw new RehomeException("That program doesn't carry a ReHome scan. Open the installer you exported on the old PC."));
    }

    public static Snapshot Load(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new RehomeException("That file doesn't exist anymore.");
            if (info.Length > MaxFileBytes)
                throw new RehomeException("That file is too large to be a ReHome scan.");
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new RehomeException("Couldn't read that file. Check it isn't open somewhere else.");
        }
        return Parse(bytes);
    }

    /// <summary>A scan's bytes, from a file or from inside an exported Rehome.exe.</summary>
    public static Snapshot Parse(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            bytes = bytes[3..];
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        Snapshot snapshot;
        try
        {
            snapshot = Scanner.FromJson(json);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new RehomeException("That file isn't a ReHome scan.");
        }

        if (snapshot.Format > 1)
            throw new RehomeException("That scan was made by a newer ReHome. Update ReHome and open it again.");
        if (snapshot.Apps is null)
            throw new RehomeException("That file isn't a ReHome scan.");
        // Scans from before the seal have no check and still open. One that has a check must match it.
        if (snapshot.Check is not null && !ScanSeal.Matches(bytes, snapshot.Check))
            throw new RehomeException("This scan was changed or damaged after it was saved, so ReHome won't install from it. Export it again on the old PC.");

        return snapshot with
        {
            // The page and the installer refer to apps by LocalId, so each one names exactly one app: the first wins.
            Apps = snapshot.Apps
                .Where(a => a is not null && !string.IsNullOrEmpty(a.Name) && IsLocalId(a.LocalId))
                .DistinctBy(a => a.LocalId, StringComparer.Ordinal)
                .Select(Clean)
                .Where(a => !BuiltIn(a))
                .ToList(),
        };
    }

    /// <summary>
    /// Apps this PC's Windows provides itself. Checked against the new PC, so a scan made by an older Rehome (or on a
    /// Windows that shipped different apps) still leaves out what's already here.
    /// </summary>
    static bool BuiltIn(App app) =>
        WindowsApps.ShipsWithWindows(Verifier.FamilyOf(app.LocalId)) || WindowsApps.IsBuiltInDesktop(app.Name, app.Package);

    /// <summary>Drops anything in an entry that could be used to point somewhere unexpected.</summary>
    static App Clean(App app)
    {
        var package = app.Package is { } p && IsInstallable(p) ? p : null;
        var cleaned = app with
        {
            Package = package,
            Status = package is null && app.Status is Status.Ready or Status.Guess ? Status.Manual : app.Status,
            Links = app.Links is null ? null : new Links
            {
                Homepage = Links.Https(app.Links.Homepage),
                Publisher = Links.Https(app.Links.Publisher),
                Support = Links.Https(app.Links.Support),
                License = Links.Https(app.Links.License),
                Privacy = Links.Https(app.Links.Privacy),
                // Recomputed from the id. A file can't point the catalog link elsewhere.
                Source = Links.CatalogPage(package),
            },
            Icon = app.Icon is { Length: < MaxIconChars } icon && PngDataUri().IsMatch(icon) ? icon : null,
        };
        // Display only, so this PC's rules decide, not whatever the file says.
        return cleaned with { System = Categories.IsSystem(cleaned) ? true : null };
    }

    /// <summary>Apps the plan will install: exact matches and guesses someone confirmed.</summary>
    public static IReadOnlyList<App> Installable(Snapshot snapshot) =>
        snapshot.Apps.Where(a => a.Package is { } p && IsInstallable(p) && (a.Status == Status.Ready || a.Status == Status.Guess && a.Confirmed == true)).ToList();

    /// <summary>An id from a scan: a registry or package path, so printable text of a sane length and no control characters.</summary>
    internal static bool IsLocalId(string? id) => id is { Length: > 0 and <= 512 } && !id.Any(char.IsControl);

    public static bool IsInstallable(CatalogRef p) => p.Source is "winget" or "msstore" && PackageId().IsMatch(p.Id);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$")]
    internal static partial Regex PackageId();

    [GeneratedRegex(@"^data:image/png;base64,[A-Za-z0-9+/]+={0,2}\z")]
    private static partial Regex PngDataUri();
}
