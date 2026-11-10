using Microsoft.Win32;
using WinGet = Microsoft.Management.Deployment;
using Appx = Windows.Management.Deployment;

namespace Rehome.Scan;

/// <summary>
/// Everything installed on this machine, through winget's own installed-apps view.
/// Only Microsoft's two official catalogs are consulted. User-added winget sources are ignored on purpose:
/// anyone can host one, and a restore must never pull from a source the user didn't think about.
/// </summary>
internal sealed class Inventory : ICatalog
{
    readonly WinGet.PackageManager _pm = new();
    readonly WinGet.PackageCatalog _winget;
    readonly WinGet.PackageCatalog? _store;

    public string WingetVersion { get; }

    Inventory(WinGet.PackageCatalog winget, WinGet.PackageCatalog? store, string version)
    {
        _winget = winget;
        _store = store;
        WingetVersion = version;
    }

    public static Inventory Open()
    {
        var pm = new WinGet.PackageManager();
        var winget = Timing.Time("connect winget", () => Connect(pm.GetPredefinedPackageCatalog(WinGet.PredefinedPackageCatalog.OpenWindowsCatalog)))
            ?? throw new RehomeException("Can't open the winget catalog. Run 'winget source update' and try again.");

        // The Store needs the network. Without it, Store apps fall back to name matching.
        var store = Timing.Time("connect msstore", () => Connect(pm.GetPredefinedPackageCatalog(WinGet.PredefinedPackageCatalog.MicrosoftStore)));

        return new Inventory(winget, store, pm.Version);
    }

    static WinGet.PackageCatalog? Connect(WinGet.PackageCatalogReference reference)
    {
        var result = reference.Connect();
        return result.Status == WinGet.ConnectResultStatus.Ok ? result.PackageCatalog : null;
    }

    public IReadOnlyList<Installed> ReadInstalled()
    {
        var options = new WinGet.CreateCompositePackageCatalogOptions
        {
            CompositeSearchBehavior = WinGet.CompositeSearchBehavior.LocalCatalogs,
        };
        options.Catalogs.Add(_pm.GetPredefinedPackageCatalog(WinGet.PredefinedPackageCatalog.OpenWindowsCatalog));

        // MSIX facts come from a different API and don't depend on winget. Read them at the same time.
        var msixTask = Task.Run(() => Timing.Time("read msix", ReadMsix));

        var composite = _pm.CreateCompositePackageCatalog(options).Connect();
        if (composite.Status != WinGet.ConnectResultStatus.Ok)
            throw new RehomeException($"Can't read installed apps ({composite.Status}).");

        var found = Timing.Time("list installed", () => composite.PackageCatalog.FindPackages(new WinGet.FindPackagesOptions()));
        if (found.Status != WinGet.FindPackagesResultStatus.Ok)
            throw new RehomeException($"Can't read installed apps ({found.Status}).");

        var msix = msixTask.GetAwaiter().GetResult();

        return Timing.Time("describe", () => Items(found.Matches)
            .Select(m => m.CatalogPackage)
            .AsParallel()
            .AsOrdered()
            .Select(p => Describe(p, msix))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList());
    }

    static Installed? Describe(WinGet.CatalogPackage package, Dictionary<string, MsixFacts> msix)
    {
        var local = package.InstalledVersion;
        if (local is null)
            return null;

        var familyNames = Items(local.PackageFamilyNames);
        var location = local.GetMetadata(WinGet.PackageVersionMetadataField.InstalledLocation);
        var remote = package.DefaultInstallVersion;

        MsixFacts? facts = null;
        foreach (var f in familyNames)
            if (msix.TryGetValue(f, out facts))
                break;

        var localId = local.Id;

        return new Installed
        {
            LocalId = localId,
            Name = local.DisplayName,
            Publisher = NullIfEmpty(local.Publisher) ?? NullIfEmpty(facts?.PublisherName),
            Version = NullIfEmpty(local.Version),
            Location = NullIfEmpty(location),
            ProductCodes = Items(local.ProductCodes),
            FamilyNames = familyNames,
            Correlated = remote is null ? null : new CatalogRef(package.Id, remote.PackageCatalog.Info.Name),
            Msix = facts,
            // Windows refuses to install an MSIX whose signature doesn't verify, so the package publisher is the signer.
            Signer = facts is null
                ? Authenticode.Read(SignedFile(localId, location, local.ProductCodes.Count > 0 ? local.ProductCodes[0] : null))
                : new Signer(facts.Publisher, Valid: true),
        };
    }

    /// <summary>MSIX facts winget doesn't expose: how it was signed, and whether it has anything you can launch.</summary>
    static Dictionary<string, MsixFacts> ReadMsix()
    {
        var packages = new Appx.PackageManager().FindPackagesForUser("")
            .Where(p => !p.IsResourcePackage)
            .DistinctBy(p => p.Id.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Listing app entries costs ~10 ms per package, so do it in parallel.
        return packages
            .AsParallel()
            .Select(p =>
            {
                var kind = p.SignatureKind.ToString();
                var skip = p.IsFramework || kind is "System" or "None";
                return (p.Id.FamilyName, Facts: new MsixFacts(kind, p.IsFramework, !skip && HasVisibleEntry(p), p.Id.Publisher, p.PublisherDisplayName));
            })
            .ToDictionary(x => x.FamilyName, x => x.Facts, StringComparer.OrdinalIgnoreCase);
    }

    static bool HasVisibleEntry(Windows.ApplicationModel.Package p)
    {
        try { return p.GetAppListEntries().Count > 0; }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return false; }
    }

    /// <summary>
    /// The file whose signature speaks for the app, in order of how directly it came from the vendor:
    /// the MSI Windows cached at install time, the exe the uninstall entry's icon points at,
    /// then the only exe in the install folder.
    /// </summary>
    internal static string? SignedFile(string localId, string? location, string? productCode)
    {
        if (Msi.CachedPackage(productCode) is { } msi)
            return msi;

        var icon = ReadArpValue(localId, "DisplayIcon");
        var path = ParseIconPath(icon);
        if (path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            return path;

        if (string.IsNullOrEmpty(location) || !Directory.Exists(location))
            return null;

        var exes = Directory.EnumerateFiles(location, "*.exe")
            .Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return exes.Count == 1 ? exes[0] : null;
    }

    internal static string? ParseIconPath(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;

        var s = icon.Trim();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : null;
        }

        var comma = s.LastIndexOf(',');
        if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), out _))
            s = s[..comma];

        return Environment.ExpandEnvironmentVariables(s.Trim());
    }

    /// <summary>Local ids look like ARP\Machine\X64\{key}. Maps that back to the registry entry it came from.</summary>
    static string? ReadArpValue(string localId, string value)
    {
        var parts = localId.Split('\\', 4);
        if (parts.Length != 4 || parts[0] != "ARP")
            return null;

        var (hive, view) = (parts[1], parts[2]) switch
        {
            ("User", _) => (RegistryHive.CurrentUser, RegistryView.Default),
            ("Machine", "X86") => (RegistryHive.LocalMachine, RegistryView.Registry32),
            ("Machine", _) => (RegistryHive.LocalMachine, RegistryView.Registry64),
            _ => (RegistryHive.CurrentUser, RegistryView.Default),
        };

        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{parts[3]}");
            return key?.GetValue(value) as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public IReadOnlyList<CatalogEntry> Search(string name, string source)
    {
        var catalog = source == "msstore" ? _store : _winget;
        if (catalog is null || string.IsNullOrWhiteSpace(name))
            return [];

        var options = new WinGet.FindPackagesOptions { ResultLimit = 10 };
        options.Selectors.Add(new WinGet.PackageMatchFilter
        {
            Field = WinGet.PackageMatchField.Name,
            Option = WinGet.PackageFieldMatchOption.ContainsCaseInsensitive,
            Value = name,
        });

        var result = Timing.Time($"search {source} {name}", () => catalog.FindPackages(options));
        if (result.Status != WinGet.FindPackagesResultStatus.Ok)
            return [];

        // Reading a publisher can cost a manifest fetch. Only pay it for results the resolver could accept.
        var wanted = Resolver.NormalizeName(name);
        return [.. Items(result.Matches).Select(m =>
        {
            var p = m.CatalogPackage;
            var publisher = source == "winget" && Resolver.NormalizeName(p.Name) == wanted ? p.DefaultInstallVersion?.Publisher : null;
            return new CatalogEntry(p.Id, p.Name, publisher, source);
        })];
    }
    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// winget's lists live in another process and only support Count and index access.
    /// foreach asks for an iterator they don't provide.
    /// </summary>
    static List<T> Items<T>(IReadOnlyList<T> list)
    {
        var count = list.Count;
        var items = new List<T>(count);
        for (var i = 0; i < count; i++)
            items.Add(list[i]);
        return items;
    }
}
