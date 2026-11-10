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

    public static Snapshot Load(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new RehomeException("That file doesn't exist anymore.");
            if (info.Length > MaxFileBytes)
                throw new RehomeException("That file is too large to be a Rehome scan.");
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
            throw new RehomeException("That file isn't a Rehome scan.");
        }

        if (snapshot.Format > 1)
            throw new RehomeException("That scan was made by a newer Rehome. Update Rehome and open it again.");
        if (snapshot.Apps is null)
            throw new RehomeException("That file isn't a Rehome scan.");
        // Scans from before the seal have no check and still open. One that has a check must match it.
        if (snapshot.Check is not null && !ScanSeal.Matches(bytes, snapshot.Check))
            throw new RehomeException("This scan was changed or damaged after it was saved, so Rehome won't install from it. Export it again on the old PC.");

        return snapshot with
        {
            Apps = snapshot.Apps
                .Where(a => a is not null && !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.LocalId))
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

    public static bool IsInstallable(CatalogRef p) => p.Source is "winget" or "msstore" && PackageId().IsMatch(p.Id);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$")]
    internal static partial Regex PackageId();

    [GeneratedRegex(@"^data:image/png;base64,[A-Za-z0-9+/]+={0,2}$")]
    private static partial Regex PngDataUri();
}
