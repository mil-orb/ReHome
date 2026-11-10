namespace Rehome;

/// <summary>Raw facts about one installed package, as reported by the system. No judgement applied yet.</summary>
internal sealed record Installed
{
    public required string LocalId { get; init; }
    public required string Name { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string? Location { get; init; }
    public IReadOnlyList<string> ProductCodes { get; init; } = [];
    public IReadOnlyList<string> FamilyNames { get; init; } = [];

    /// <summary>Package id and source winget correlated this install to, if any.</summary>
    public CatalogRef? Correlated { get; init; }

    /// <summary>Present only for MSIX packages.</summary>
    public MsixFacts? Msix { get; init; }

    public Signer? Signer { get; init; }

    /// <summary>Windows provisioned this package itself (Game Bar, Paint, Microsoft Store). See <see cref="WindowsApps"/>.</summary>
    public bool ShipsWithWindows { get; init; }

    public bool IsMsix => Msix is not null;
}

internal sealed record CatalogRef(string Id, string Source);

internal sealed record MsixFacts(string SignatureKind, bool IsFramework, bool HasVisibleEntry, string Publisher, string PublisherName);

/// <summary>
/// Who signed the installed app. Captured on the old machine, checked again on restore.
/// Restore compares Organization: vendors renew certificates and reorder subject fields, but keep their legal name.
/// For Store-signed MSIX the subject is a Store-assigned id, so the package family name is the identity instead.
/// </summary>
internal sealed record Signer(string? Organization, string? Name, string Subject, string From, bool Valid)
{
    public static Signer Parse(string subject, string from, bool valid)
    {
        string? o = null, cn = null;
        try
        {
            foreach (var rdn in new System.Security.Cryptography.X509Certificates.X500DistinguishedName(subject).EnumerateRelativeDistinguishedNames())
            {
                var oid = rdn.GetSingleElementType().Value;
                if (oid == "2.5.4.10") o ??= rdn.GetSingleElementValue();
                else if (oid == "2.5.4.3") cn ??= rdn.GetSingleElementValue();
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Unparseable subject: keep the raw string, leave the parts empty.
        }

        return new Signer(o, cn, subject, from, valid);
    }
}

internal enum Status
{
    /// <summary>Exact package identity from winget or the Store. Safe to reinstall unattended.</summary>
    Ready,

    /// <summary>Name and publisher match a catalog entry. Needs a human yes.</summary>
    Guess,

    /// <summary>No official package found. Listed so nothing silently disappears.</summary>
    Manual,

    /// <summary>Owned by a game launcher. Reinstalled from the launcher's library.</summary>
    Launcher,

    /// <summary>Installed by its parent app (shell extensions, sparse packages). Not restored on its own.</summary>
    Companion,
}

internal sealed record App
{
    public required string Name { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public required Status Status { get; init; }
    public CatalogRef? Package { get; init; }
    public Signer? Signer { get; init; }
    public string? Parent { get; init; }

    /// <summary>A person said yes to this guess. Only ever set on Guess entries.</summary>
    public bool? Confirmed { get; init; }

    /// <summary>
    /// A driver, runtime or part of Windows: something a new PC gets from Windows Update, the PC maker or the app
    /// that needs it. Starts unchecked. Display only: it never changes what can be installed or how it's checked.
    /// </summary>
    public bool? System { get; init; }

    /// <summary>
    /// Settings found for this app, if any. Added within format 1: older readers ignore the field,
    /// and a scan without it means nothing was offered.
    /// </summary>
    public IReadOnlyList<Setting>? Settings { get; init; }

    /// <summary>The vendor's own pages, from the winget manifest or the app's uninstall entry. https only.</summary>
    public Links? Links { get; init; }

    /// <summary>
    /// The app's icon from the old machine, as a small PNG data URI. Only the desktop app writes it, so the
    /// restore screen can show icons before anything is installed. Display only.
    /// </summary>
    public string? Icon { get; init; }

    public required string LocalId { get; init; }
}

/// <summary>
/// Where to read about an app. <see cref="Source"/> is the package's page in its catalog: the winget-pkgs
/// manifest folder (installer URLs and hashes, reviewed by Microsoft) or the Microsoft Store listing.
/// </summary>
internal sealed record Links
{
    public string? Homepage { get; init; }
    public string? Publisher { get; init; }
    public string? Support { get; init; }
    public string? License { get; init; }
    public string? Privacy { get; init; }
    public string? Source { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Homepage is null && Publisher is null && Support is null && License is null && Privacy is null && Source is null;

    /// <summary>The link for a name the page asks for. The page never sends a URL.</summary>
    public string? Get(string kind) => kind switch
    {
        "homepage" => Homepage,
        "publisher" => Publisher,
        "support" => Support,
        "license" => License,
        "privacy" => Privacy,
        "source" => Source,
        _ => null,
    };

    /// <summary>Only absolute https URLs survive. Everything else is dropped, never "fixed".</summary>
    public static string? Https(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Length > 0 && u.UserInfo.Length == 0
            ? u.AbsoluteUri
            : null;

    public static string? CatalogPage(CatalogRef? package) => package switch
    {
        { Source: "msstore" } => $"https://apps.microsoft.com/detail/{Uri.EscapeDataString(package.Id)}",
        { Source: "winget" } when package.Id.Length > 0 =>
            "https://github.com/microsoft/winget-pkgs/tree/master/manifests/"
            + char.ToLowerInvariant(package.Id[0]) + "/"
            + string.Join('/', package.Id.Split('.').Select(Uri.EscapeDataString)),
        _ => null,
    };
}

internal sealed record Snapshot
{
    public int Format { get; init; } = 1;
    public required string Machine { get; init; }
    public required DateTimeOffset Taken { get; init; }
    public required string Winget { get; init; }
    public required IReadOnlyList<App> Apps { get; init; }

    /// <summary>
    /// SHA-256 of this file's exact bytes, with this value zeroed. Written on every save and export, checked on open.
    /// Added within format 1: older readers ignore it, and scans without it still open. See <see cref="ScanSeal"/>.
    /// </summary>
    public string? Check { get; init; }
}
