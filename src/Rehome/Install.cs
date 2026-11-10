using System.Security.Principal;
using System.Text.Json;
using Rehome.Restore;
using Rehome.Scan;

namespace Rehome;

/// <summary>
/// `rehome install`. Same engine as the app: verify everything first, ask once, then install one at a time.
/// Piped, it writes one JSON event per line for scripts and agents.
/// </summary>
internal static class Install
{
    public static async Task<int> Run(string path, bool yes, bool verifyOnly = false)
    {
        var json = Console.IsOutputRedirected;
        var snapshot = Plan.Load(path);
        var inventory = Inventory.Open();
        var present = Verifier.Present(Plan.Installable(snapshot), inventory.ReadInstalled());
        var names = snapshot.Apps.ToDictionary(a => a.LocalId, a => a.Name);

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

        var apps = new List<App>();
        foreach (var app in Plan.Installable(snapshot))
        {
            if (present.Contains(app.LocalId))
                await Report(new RunEvent { LocalId = app.LocalId, State = "present", Message = "Already installed" });
            else
                apps.Add(app);
        }

        if (apps.Count == 0)
        {
            Console.Error.WriteLine("Nothing to install.");
            return 0;
        }

        Task Report(RunEvent e)
        {
            if (json)
                Console.Out.WriteLine(JsonSerializer.Serialize(e, RunJson.Default.RunEvent));
            else if (e.LocalId is { } id && e.State is not ("verifying" or "review") && e.Progress is null && (verifyOnly || e.State != "verified"))
                Console.Error.WriteLine($"  {Label(e.State)}  {names.GetValueOrDefault(id, id)}{(e.Message is { } m && e.State != "present" ? "  " + m : "")}");
            return Task.CompletedTask;
        }

        if (!json)
            Console.Error.WriteLine($"Checking {apps.Count} installers…");

        IReadOnlyList<Checked> results;
        try
        {
            results = await Verification.Run(apps, inventory, Report, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Stopped. Nothing was installed.");
            return 130;
        }

        var approved = results.Where(r => r.Verdict == Verdict.Trusted).ToList();
        var failedChecks = results.Count(r => r.Verdict == Verdict.Failed);
        var questions = results.Where(r => r.Verdict is Verdict.Unsigned or Verdict.SignerChanged).ToList();

        if (questions.Count > 0)
        {
            Console.Error.WriteLine();
            foreach (var q in questions)
            {
                var what = q.Verdict == Verdict.Unsigned
                    ? "not signed"
                    : $"signed by {q.SignedBy}, was {q.Expected}";
                Console.Error.WriteLine($"  {names.GetValueOrDefault(q.LocalId, q.LocalId)}: {what}");
                Console.Error.WriteLine($"      from {q.Host}  sha256 {q.Sha256}");
            }

            if (verifyOnly)
            {
            }
            else if (yes || json)
            {
                Console.Error.WriteLine("Skipping these. Run without --yes to decide.");
            }
            else
            {
                Console.Error.Write($"Install these {questions.Count} anyway? [y/N] ");
                if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                    approved.AddRange(questions);
            }
        }

        if (verifyOnly)
        {
            Console.Error.WriteLine($"{approved.Count} verified, {questions.Count} need a decision, {failedChecks} failed");
            return failedChecks > 0 ? 1 : 0;
        }

        var items = approved.Select(r => r.Item!).ToList();
        if (items.Count == 0)
        {
            Console.Error.WriteLine("Nothing passed verification.");
            return 1;
        }

        if (!IsElevated() && !json)
            Console.Error.WriteLine("Not running as administrator: some installers will ask for approval.");

        int installed = 0, already = 0, restart = 0, failed = 0, skipped = 0;
        await Runner.Run(items, e =>
        {
            switch (e.State)
            {
                case "installed": installed++; break;
                case "present": already++; break;
                case "restart": restart++; break;
                case "failed": failed++; break;
                case "skipped": skipped++; break;
            }
            return Report(e);
        }, () => cancel.IsCancellationRequested, CancellationToken.None);

        var bad = failedChecks + failed;
        var parts = new[]
        {
            $"{installed} installed",
            already > 0 ? $"{already} already installed" : null,
            restart > 0 ? $"{restart} need a restart" : null,
            $"{bad} failed",
            skipped > 0 ? $"{skipped} skipped" : null,
        };
        Console.Error.WriteLine(string.Join(", ", parts.Where(p => p is not null)));
        // A stopped run isn't a successful one, even if nothing that ran failed. Scripts need to tell them apart.
        return cancel.IsCancellationRequested ? 130 : bad > 0 ? 1 : 0;
    }

    static string Label(string? state) => state switch
    {
        "installing" => "…",
        "installed" => "✓",
        "present" => "=",
        "restart" => "↻",
        "skipped" => "-",
        "verified" => "✓",
        _ => "✗",
    };

    static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
