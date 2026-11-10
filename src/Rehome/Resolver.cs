using System.Text;
using System.Text.RegularExpressions;

namespace Rehome;

internal sealed record CatalogEntry(string Id, string Name, string? Publisher, string Source);

/// <summary>Read-only view of the package catalogs. Real one talks to winget; tests use a stub.</summary>
internal interface ICatalog
{
    /// <summary>Name search in one source: "winget" or "msstore". Must be safe to call from several threads.</summary>
    IReadOnlyList<CatalogEntry> Search(string name, string source);
}

/// <summary>
/// Turns raw installs into a restore plan. Pure: same input, same output, no I/O except through the catalog.
/// Order matters: cheapest and most certain evidence first.
/// </summary>
internal static partial class Resolver
{
    public static IReadOnlyList<App> Resolve(IReadOnlyList<Installed> installed, ICatalog catalog)
    {
        var desktop = installed.Where(i => !i.IsMsix).ToList();

        // Catalog lookups are network-bound for the Store, so run them side by side.
        var apps = installed
            .Where(i => !IsOperatingSystem(i))
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(i => ResolveOne(i, desktop, catalog))
            .ToList();

        return [.. apps.OrderBy(a => a.Status).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.LocalId, StringComparer.Ordinal)];
    }

    static App ResolveOne(Installed item, List<Installed> desktop, ICatalog catalog)
    {
        // 1. winget already correlated it: exact identity.
        if (item.Correlated is { } exact)
            return Make(item, Status.Ready, exact);

        // 2. A game library entry. The launcher restores it, not us.
        if (Launcher(item) is { } launcher)
            return Make(item, Status.Launcher) with { Parent = launcher };

        // 3. No visible app: a shell extension or component that ships with something else.
        if (item.Msix is { HasVisibleEntry: false })
            return Make(item, Status.Companion) with { Parent = FindParent(item, desktop)?.Name };

        // 4. Same normalized name and publisher in an official catalog. Plausible, never automatic.
        //    Store-signed packages are looked up in the Store, everything else in winget.
        var source = item.Msix?.SignatureKind == "Store" ? "msstore" : "winget";
        if (Guess(item, catalog, source) is { } guess)
            return Make(item, Status.Guess, new CatalogRef(guess.Id, guess.Source));

        return Make(item, Status.Manual);
    }

    static App Make(Installed i, Status status, CatalogRef? package = null) => new()
    {
        Name = i.Name,
        Publisher = i.Publisher,
        Version = i.Version,
        Status = status,
        Package = package,
        Signer = i.Signer,
        LocalId = i.LocalId,
    };

    static bool IsOperatingSystem(Installed i) =>
        i.Msix is { } m && (m.IsFramework || m.SignatureKind is "System" or "None");

    static string? Launcher(Installed item)
    {
        var key = item.LocalId.Split('\\').LastOrDefault() ?? "";
        return key.StartsWith("Steam App ", StringComparison.Ordinal) ? "Steam" : null;
    }

    static CatalogEntry? Guess(Installed item, ICatalog catalog, string source)
    {
        var name = NormalizeName(item.Name);
        var publisher = NormalizePublisher(item.Publisher);
        if (name.Length < 3 || publisher.Length == 0)
            return null;

        // Store packages carry a signed publisher already; the Store often omits it in search results.
        bool SamePublisher(CatalogEntry c) =>
            NormalizePublisher(c.Publisher) == publisher || (source == "msstore" && c.Publisher is null);

        var hits = catalog.Search(StripNoise(item.Name), source)
            .Where(c => NormalizeName(c.Name) == name && SamePublisher(c))
            .ToList();

        // Two equally good answers means we don't know. Say so instead of picking one.
        // Stable channels only. "Mullvad VPN (Beta)" normalizes like "Mullvad VPN" but isn't what was installed.
        hits = [.. hits.Where(c => !IsPrerelease(c))];

        return hits.Count == 1 ? hits[0] : null;
    }

    internal static bool IsPrerelease(CatalogEntry c)
    {
        var id = c.Id.ToLowerInvariant();
        return PrereleaseIds.Any(w => id.EndsWith("." + w, StringComparison.Ordinal)) || PrereleaseName().IsMatch(c.Name);
    }

    static readonly string[] PrereleaseIds = ["beta", "preview", "alpha", "canary", "nightly", "insiders", "dev"];

    [GeneratedRegex(@"\b(beta|preview|alpha|canary|nightly|insiders)\b", RegexOptions.IgnoreCase)] private static partial Regex PrereleaseName();

    static Installed? FindParent(Installed companion, List<Installed> desktop)
    {
        var key = NormalizeName(companion.FamilyNames.FirstOrDefault()?.Split('_')[0] ?? companion.Name);
        if (key.Length < 3)
            return null;

        return desktop.FirstOrDefault(d =>
        {
            var n = NormalizeName(d.Name);
            return n.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(n, StringComparison.Ordinal);
        });
    }

    /// <summary>"Notepad++ (64-bit x64)" and "Notepad++" compare equal. So do "7-Zip 24.09 (x64)" and "7-Zip".</summary>
    internal static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var s = StripNoise(name).ToLowerInvariant().Replace("++", "plusplus").Replace("#", "sharp");
        return AlphaNum(s);
    }

    internal static string NormalizePublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
            return "";

        var words = Words().Matches(publisher.ToLowerInvariant().Replace("++", "plusplus"))
            .Select(m => m.Value)
            .Where(w => !CorporateWords.Contains(w))
            .ToList();

        // First meaningful word is enough: "Microsoft Corporation" vs "Microsoft".
        return words.Count > 0 ? words[0] : "";
    }

    static string StripNoise(string name)
    {
        var s = Parenthetical().Replace(name, " ");
        s = VersionLike().Replace(s, " ");
        s = ArchWords().Replace(s, " ");
        return Spaces().Replace(s, " ").Trim();
    }

    static string AlphaNum(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(c);
        return sb.ToString();
    }

    static readonly HashSet<string> CorporateWords =
    [
        "inc", "corp", "corporation", "llc", "ltd", "limited", "gmbh", "co", "company",
        "team", "software", "technologies", "technology", "the", "sa", "ag", "bv", "oy", "ab", "pty",
    ];

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")] private static partial Regex Parenthetical();
    [GeneratedRegex(@"v?\d+(\.\d+)+\b|\b\d{4,}\b")] private static partial Regex VersionLike();
    [GeneratedRegex(@"\b(x64|x86|amd64|arm64|64-bit|32-bit|64bit|32bit)\b", RegexOptions.IgnoreCase)] private static partial Regex ArchWords();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[a-z0-9]+")] private static partial Regex Words();
}
