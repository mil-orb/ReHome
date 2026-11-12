using System.Security.Cryptography;
using Rehome.Scan;
using WinGet = Microsoft.Management.Deployment;

namespace Rehome.Restore;

internal enum Verdict
{
    /// <summary>Hash matches the manifest and the signer matches the old PC (or the store/MSIX system vouches for it).</summary>
    Trusted,

    /// <summary>Hash matches, but the installer carries no valid signature. A person decides.</summary>
    Unsigned,

    /// <summary>Hash matches, but a different organization signed it than on the old PC. A person decides.</summary>
    SignerChanged,

    /// <summary>Couldn't be verified. Never installed.</summary>
    Failed,
}

/// <summary>What verification found for one app. Host, hash and signer are shown when a person has to decide.</summary>
internal sealed record Checked(string LocalId, Verdict Verdict, InstallItem? Item, string? Message = null,
    string? Host = null, string? Sha256 = null, string? SignedBy = null, string? Expected = null);

/// <summary>The installer entry winget wrote beside the download (a merged manifest with one installer).</summary>
internal sealed record ManifestInstaller(string? Version, string? Url, string? Sha256, string? Type, string? Scope, string? Architecture);

internal static class Trust
{
    /// <summary>
    /// The decision table. The hash has already matched by the time this runs; this is about who made the file.
    /// No signer on record (the old PC's copy was unsigned) plus a valid signature now is fine: there's nothing
    /// to contradict. A different organization than before, or no signature at all, needs a person.
    /// </summary>
    public static Verdict Decide(Signer? before, Signer? now, string? installerType)
    {
        var windowsChecks = installerType is "msix" or "appx";
        if (now is null || !now.Valid)
            return windowsChecks ? Verdict.Trusted : Verdict.Unsigned;

        var expected = Org(before);
        if (expected.Length == 0)
            return Verdict.Trusted;

        return Org(now) == expected ? Verdict.Trusted : Verdict.SignerChanged;
    }

    /// <summary>"Microsoft Corporation" and "Microsoft Corporation." are the same signer. Punctuation and case don't count.</summary>
    static string Org(Signer? s) =>
        string.Concat((s?.Organization ?? s?.Name ?? "").Where(char.IsLetterOrDigit)).ToLowerInvariant();

    public static ManifestInstaller ReadManifest(string yaml)
    {
        // Root keys are defaults; the single entry under Installers overrides them.
        var root = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = new Dictionary<string, string>(StringComparer.Ordinal);
        var inInstallers = false;

        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            if (!char.IsWhiteSpace(line[0]) && line[0] != '-')
                inInstallers = line.StartsWith("Installers:", StringComparison.Ordinal);

            var text = line.TrimStart(' ', '-');
            var colon = text.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = text[..colon];
            var value = text[(colon + 1)..].Trim().Trim('"', '\'');
            if (value.Length == 0)
                continue;

            var target = inInstallers && (char.IsWhiteSpace(line[0]) || line[0] == '-') ? entry : root;
            target.TryAdd(key, value);
        }

        string? Get(string key) => entry.GetValueOrDefault(key) ?? root.GetValueOrDefault(key);
        return new ManifestInstaller(root.GetValueOrDefault("PackageVersion"), Get("InstallerUrl"), Get("InstallerSha256"),
            Get("InstallerType"), Get("Scope"), Get("Architecture"));
    }
}

/// <summary>
/// Downloads each installer into a private folder, checks it, then deletes it. Runs unelevated:
/// files from the internet are only ever parsed at the user's own privilege level.
/// </summary>
internal sealed class Verifier(Inventory inventory, string workRoot)
{
    public async Task<Checked> Check(App app, IProgress<double>? progress, CancellationToken cancel)
    {
        if (app.Package is not { } package || !Plan.IsInstallable(package))
            return new Checked(app.LocalId, Verdict.Failed, null, "No official package.");

        var found = inventory.Find(package);
        if (found?.DefaultInstallVersion is not { } version)
            return new Checked(app.LocalId, Verdict.Failed, null, Errors.Describe(unchecked((int)0x8A150014)));

        var item = new InstallItem { LocalId = app.LocalId, Name = app.Name, Id = found.Id, Source = package.Source };

        // Store apps come signed and licensed by the Store; Windows checks them on install.
        if (package.Source == "msstore")
            return new Checked(app.LocalId, Verdict.Trusted, item);

        var dir = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new WinGet.DownloadOptions { DownloadDirectory = dir, AcceptPackageAgreements = true };
            var operation = inventory.PackageManager.DownloadPackageAsync(found, options);
            if (progress is not null)
                operation.Progress = (_, p) => progress.Report(p.DownloadProgress);

            using var registration = cancel.Register(operation.Cancel);
            var result = await operation.AsTask();
            cancel.ThrowIfCancellationRequested();

            if (result.Status != WinGet.DownloadResultStatus.Ok)
            {
                var code = result.ExtendedErrorCode?.HResult ?? unchecked((int)0x8A150008);
                return new Checked(app.LocalId, Verdict.Failed, null, Errors.Describe(code));
            }

            var yamlPath = Directory.EnumerateFiles(dir, "*.yaml").FirstOrDefault();
            var file = Directory.EnumerateFiles(dir).FirstOrDefault(f => !f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
            if (yamlPath is null || file is null)
                return new Checked(app.LocalId, Verdict.Failed, null, "winget didn't produce an installer to check.");

            var manifest = Trust.ReadManifest(await Shared(yamlPath, s => new StreamReader(s).ReadToEndAsync(cancel), cancel));
            var sha = Convert.ToHexStringLower(await Shared(file, s => SHA256.HashDataAsync(s, cancel).AsTask(), cancel));

            // winget already compared the hash. Comparing again costs one read and catches a bug or a swapped file.
            if (manifest.Sha256 is null || !string.Equals(manifest.Sha256, sha, StringComparison.OrdinalIgnoreCase))
                return new Checked(app.LocalId, Verdict.Failed, null, Errors.Describe(Errors.HashMismatch), Sha256: sha);

            var from = file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? "msi" : "exe";
            var signer = Authenticode.Read(file, from);
            if (signer is not null && await Task.Run(() => Authenticode.Distrusted(file), cancel))
                return new Checked(app.LocalId, Verdict.Failed, null,
                    "Windows says the certificate this installer is signed with was revoked or isn't trusted. Not installed.", Sha256: sha);
            var verdict = Trust.Decide(app.Signer, signer, manifest.Type);

            item = item with
            {
                Version = manifest.Version ?? version.Version,
                Scope = manifest.Scope?.ToLowerInvariant(),
                Architecture = manifest.Architecture?.ToLowerInvariant(),
                InstallerType = manifest.Type?.ToLowerInvariant(),
            };
            if (WingetCli.Problem(item) is { } problem)
                return new Checked(app.LocalId, Verdict.Failed, null, $"The manifest has an unexpected value. {problem}");

            // winget-pkgs only accepts https installer URLs. A manifest that says otherwise didn't come through that
            // review, so it doesn't install, whatever the hash says.
            if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                return new Checked(app.LocalId, Verdict.Failed, null, "The catalog lists this installer without https. Not installed.");

            var host = url.Host;
            return new Checked(app.LocalId, verdict, item, null, host, sha,
                signer is { Valid: true } ? signer.Organization ?? signer.Name : null,
                app.Signer?.Organization ?? app.Signer?.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new Checked(app.LocalId, Verdict.Failed, null, Errors.Describe(e.HResult));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Reads a fresh download. Antivirus often holds it open for a moment right after it lands,
    /// so share generously and retry a sharing violation a few times before calling it a failure.
    /// </summary>
    static async Task<T> Shared<T>(string path, Func<Stream, Task<T>> read, CancellationToken cancel)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
                return await read(stream);
            }
            catch (IOException e) when (attempt < 5 && (e.HResult & 0xFFFF) is 32 or 33)
            {
                await Task.Delay(400 * attempt, cancel);
            }
        }
    }

    /// <summary>
    /// Local ids of the apps already on this PC, so a restore skips them. winget correlates most installs to a
    /// package id. Store apps often aren't correlated, so they match on package family, the identity Windows itself uses.
    /// </summary>
    public static HashSet<string> Present(IEnumerable<App> apps, IReadOnlyList<Installed> installed)
    {
        var ids = installed.Where(i => i.Correlated is not null).Select(i => i.Correlated!.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var families = installed.SelectMany(i => i.FamilyNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return apps
            .Where(a => a.Package is { } p && (ids.Contains(p.Id) || FamilyOf(a.LocalId) is { } f && families.Contains(f)))
            .Select(a => a.LocalId)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>"MSIX\Name_Version_Arch_Resource_PublisherId" → "Name_PublisherId". Null for anything else.</summary>
    internal static string? FamilyOf(string localId)
    {
        return localId.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase) ? WindowsApps.FamilyOf(localId[5..]) : null;
    }
}
