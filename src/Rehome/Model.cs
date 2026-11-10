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

    public bool IsMsix => Msix is not null;
}

internal sealed record CatalogRef(string Id, string Source);

internal sealed record MsixFacts(string SignatureKind, bool IsFramework, bool HasVisibleEntry, string Publisher, string PublisherName);

/// <summary>Who signed the installed binary. Captured on the old machine, checked again on restore.</summary>
internal sealed record Signer(string Subject, bool Valid);

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
    public required string LocalId { get; init; }
}

internal sealed record Snapshot
{
    public int Format { get; init; } = 1;
    public required string Machine { get; init; }
    public required DateTimeOffset Taken { get; init; }
    public required string Winget { get; init; }
    public required IReadOnlyList<App> Apps { get; init; }
}
