using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Rehome.Restore;

/// <summary>
/// Exactly what to install, after verification. Version, scope, architecture and installer type are the ones
/// the verify step downloaded and checked, so winget picks the same installer and its hash check proves
/// these are the same bytes.
/// </summary>
internal sealed record InstallItem
{
    public required string LocalId { get; init; }
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string Source { get; init; }
    public string? Version { get; init; }
    public string? Scope { get; init; }
    public string? Architecture { get; init; }
    public string? InstallerType { get; init; }
}

internal static partial class WingetCli
{
    static readonly HashSet<string> Scopes = ["user", "machine"];
    static readonly HashSet<string> Architectures = ["x86", "x64", "arm", "arm64", "neutral"];
    static readonly HashSet<string> InstallerTypes = ["msix", "msi", "appx", "exe", "zip", "inno", "nullsoft", "wix", "burn", "portable"];

    /// <summary>
    /// Checked again on the elevated side: the request crossed a process boundary.
    /// A field that isn't exactly what we expect stops the item. It's never trimmed or escaped into shape.
    /// </summary>
    public static string? Problem(InstallItem item)
    {
        if (item.Source is not ("winget" or "msstore")) return "Unknown source.";
        if (!Plan.PackageId().IsMatch(item.Id)) return "Invalid package id.";
        if (item.Version is { } v && !VersionText().IsMatch(v)) return "Invalid version.";
        if (item.Scope is { } s && !Scopes.Contains(s)) return "Invalid scope.";
        if (item.Architecture is { } a && !Architectures.Contains(a)) return "Invalid architecture.";
        if (item.InstallerType is { } t && !InstallerTypes.Contains(t)) return "Invalid installer type.";
        return null;
    }

    public static IReadOnlyList<string> InstallArgs(InstallItem item)
    {
        if (Problem(item) is { } problem)
            throw new ArgumentException(problem, nameof(item));

        List<string> args = ["install", "--id", item.Id, "--exact", "--source", item.Source];
        if (item.Source == "winget")
        {
            if (item.Version is { } v) args.AddRange(["--version", v]);
            if (item.Scope is { } s) args.AddRange(["--scope", s]);
            if (item.Architecture is { } a and not "neutral") args.AddRange(["--architecture", a]);
            if (item.InstallerType is { } t) args.AddRange(["--installer-type", t]);
        }
        // Never --ignore-security-hash, never --force. A hash mismatch is a hard stop.
        args.AddRange(["--silent", "--disable-interactivity", "--no-upgrade", "--accept-package-agreements", "--accept-source-agreements"]);
        return args;
    }

    /// <summary>winget's app execution alias. Present for every user with App Installer.</summary>
    public static string? Path()
    {
        var alias = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(alias) ? alias : null;
    }

    /// <summary>Runs one install. Returns winget's exit code as an HRESULT (0 is success).</summary>
    public static async Task<int> Run(InstallItem item, TimeSpan timeout, CancellationToken cancel)
    {
        var exe = Path() ?? throw new RehomeException("winget isn't available. Install App Installer from the Microsoft Store.");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in InstallArgs(item))
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new RehomeException("Couldn't start winget.");
        // Drain output so winget never blocks on a full pipe. Its progress bars aren't useful to us.
        _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancel only reaches here on timeout: a stop request waits for the current install to finish.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return Errors.TimedOut;
        }
        return process.ExitCode;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,63}$")]
    private static partial Regex VersionText();
}
