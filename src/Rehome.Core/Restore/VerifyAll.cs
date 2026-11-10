using Rehome.Scan;

namespace Rehome.Restore;

internal static class Verification
{
    const int Parallel = 4;

    /// <summary>A per-user folder for downloads being checked. Emptied before each run.</summary>
    public static string WorkRoot()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rehome", "Downloads");
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked leftover from a crashed run. New downloads go into fresh subfolders anyway.
        }
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Checks every app, four at a time, reporting each one as it starts and finishes.</summary>
    public static async Task<IReadOnlyList<Checked>> Run(IReadOnlyList<App> apps, Inventory inventory, Func<RunEvent, Task> report, CancellationToken cancel)
    {
        var verifier = new Verifier(inventory, WorkRoot());
        var results = new Checked[apps.Count];
        using var gate = new SemaphoreSlim(Parallel);
        var reportLock = new SemaphoreSlim(1);

        async Task Say(RunEvent e)
        {
            await reportLock.WaitAsync(CancellationToken.None);
            try { await report(e); }
            finally { reportLock.Release(); }
        }

        await Task.WhenAll(apps.Select(async (app, i) =>
        {
            await gate.WaitAsync(cancel);
            try
            {
                await Say(new RunEvent { LocalId = app.LocalId, State = "verifying" });
                var last = -1;
                var progress = new Progress<double>(p =>
                {
                    var pct = (int)(p * 100);
                    if (pct / 10 != last / 10)
                    {
                        last = pct;
                        _ = Say(new RunEvent { LocalId = app.LocalId, State = "verifying", Progress = p });
                    }
                });

                Checked result;
                try
                {
                    result = await Task.Run(() => verifier.Check(app, progress, cancel), cancel);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // One bad package never stops the rest.
                    result = new Checked(app.LocalId, Verdict.Failed, null, Errors.Describe(e.HResult));
                }

                results[i] = result;
                await Say(new RunEvent
                {
                    LocalId = app.LocalId,
                    State = result.Verdict switch { Verdict.Trusted => "verified", Verdict.Failed => "failed", _ => "review" },
                    Message = result.Verdict == Verdict.Trusted ? Evidence(result) : result.Message,
                });
            }
            finally
            {
                gate.Release();
            }
        }));

        return results;
    }

    /// <summary>What a passed check proved, in words. Shown in the log so a person can see why something was trusted.</summary>
    internal static string Evidence(Checked r) =>
        r.Sha256 is null
            ? "From the Microsoft Store. Windows checks the Store's signature when it installs."
            : "SHA-256 matches the winget catalog."
              + (r.SignedBy is { } by ? $" Signed by {by.TrimEnd('.')}." : " Windows checks the package signature when it installs.")
              + (r.Host is { } host ? $" From {host}." : "");
}
