using System.Text.Json.Serialization;

namespace Rehome.Restore;

/// <summary>
/// One line of restore progress. The elevated helper sends these to the app, and `rehome install` prints them
/// as JSON lines when piped. States: verifying, review, installing, installed, present, restart, failed, skipped.
/// </summary>
internal sealed record RunEvent
{
    public string? LocalId { get; init; }
    public string? State { get; init; }
    public string? Message { get; init; }
    public double? Progress { get; init; }
    public bool? Done { get; init; }
}

/// <summary>What the app hands the elevated helper. Validated again on arrival.</summary>
internal sealed record RunRequest
{
    public required IReadOnlyList<InstallItem> Items { get; init; }
}

/// <summary>Installs one at a time. Windows installers share a lock, and parallel installs mostly wait on each other.</summary>
internal static class Runner
{
    public static readonly TimeSpan PerInstall = TimeSpan.FromMinutes(45);

    public static async Task Run(IReadOnlyList<InstallItem> items, Func<RunEvent, Task> report, Func<bool> stopRequested, CancellationToken cancel)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (stopRequested() || cancel.IsCancellationRequested)
            {
                foreach (var rest in items.Skip(i))
                    await report(new RunEvent { LocalId = rest.LocalId, State = "skipped", Message = "Stopped" });
                break;
            }

            if (WingetCli.Problem(item) is { } problem)
            {
                await report(new RunEvent { LocalId = item.LocalId, State = "failed", Message = problem });
                continue;
            }

            await report(new RunEvent { LocalId = item.LocalId, State = "installing" });
            int code;
            try
            {
                code = await WingetCli.Run(item, PerInstall, cancel);
            }
            catch (Exception e) when (e is RehomeException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                await report(new RunEvent { LocalId = item.LocalId, State = "failed", Message = e is RehomeException ? e.Message : "Couldn't start winget." });
                continue;
            }

            var state = Errors.Classify(code) switch
            {
                Errors.Outcome.Installed => "installed",
                Errors.Outcome.AlreadyThere => "present",
                Errors.Outcome.NeedsRestart => "restart",
                _ => "failed",
            };
            await report(new RunEvent { LocalId = item.LocalId, State = state, Message = state == "installed" ? null : Errors.Describe(code) });
        }
        await report(new RunEvent { Done = true });
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RunEvent))]
[JsonSerializable(typeof(RunRequest))]
internal sealed partial class RunJson : JsonSerializerContext;
