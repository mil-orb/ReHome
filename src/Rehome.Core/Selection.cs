using System.Text.Json;

namespace Rehome;

/// <summary>
/// What a person chose, applied to the scan. The app and any future CLI flags share this, so a saved file
/// means the same thing however it was made.
/// </summary>
internal sealed record Selection(
    IReadOnlySet<string> SkipApps,
    IReadOnlySet<string> ConfirmGuesses,
    IReadOnlySet<(string App, string Setting)> SkipSettings)
{
    public Snapshot Apply(Snapshot snapshot) => snapshot with
    {
        Apps = snapshot.Apps
            // Manual apps are choosable too: a kept one becomes a link to the vendor's site on the new PC.
            .Where(a => !(a.Status is Status.Ready or Status.Guess or Status.Manual && SkipApps.Contains(a.LocalId)))
            // Unconfirmed guesses never make it into the file.
            .Where(a => a.Status != Status.Guess || ConfirmGuesses.Contains(a.LocalId))
            .Select(a => a.Status == Status.Guess ? a with { Confirmed = true } : a)
            .Select(WithoutSkippedSettings)
            .ToList(),
    };

    /// <summary>
    /// Reads <c>{ skip: [ids], confirm: [ids], skipSettings: { localId: [ids] } }</c>.
    /// Anything malformed is ignored, so a bad message selects less, never more.
    /// </summary>
    public static Selection Parse(JsonElement args) => new(Ids(args, "skip"), Ids(args, "confirm"), SettingIds(args));

    static HashSet<string> Ids(JsonElement args, string name)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
            return ids;

        foreach (var item in list.EnumerateArray().Take(10_000))
            if (item.ValueKind == JsonValueKind.String)
                ids.Add(item.GetString()!);
        return ids;
    }

    static HashSet<(string, string)> SettingIds(JsonElement args)
    {
        var ids = new HashSet<(string, string)>();
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("skipSettings", out var map) || map.ValueKind != JsonValueKind.Object)
            return ids;

        foreach (var app in map.EnumerateObject().Take(10_000))
            if (app.Value.ValueKind == JsonValueKind.Array)
                foreach (var item in app.Value.EnumerateArray().Take(64))
                    if (item.ValueKind == JsonValueKind.String)
                        ids.Add((app.Name, item.GetString()!));
        return ids;
    }

    App WithoutSkippedSettings(App app)
    {
        if (app.Settings is null)
            return app;
        var kept = app.Settings.Where(s => !SkipSettings.Contains((app.LocalId, s.Id))).ToList();
        return app with { Settings = kept.Count == 0 ? null : kept };
    }
}
