using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rehome.Scan;

namespace Rehome;

/// <summary>An expected failure with a message fit to show a person as-is.</summary>
internal sealed class RehomeException(string message) : Exception(message);

/// <summary>The one entry point the CLI and the app share. Same call, same JSON.</summary>
internal static class Scanner
{
    public static Snapshot Run()
    {
        try
        {
            var inventory = Inventory.Open();
            var installed = inventory.ReadInstalled();
            var apps = Timing.Time("resolve", () => Resolver.Resolve(installed, inventory));
            apps = Timing.Time("settings", () => SettingsProbe.Attach(apps));
            apps = Timing.Time("links", () => AttachLinks(apps, inventory));

            return new Snapshot
            {
                Machine = Environment.MachineName,
                Taken = DateTimeOffset.Now,
                Winget = inventory.WingetVersion,
                Apps = apps,
            };
        }
        catch (COMException e)
        {
            throw new RehomeException($"winget isn't available ({e.HResult:X8}). Install App Installer from the Microsoft Store.");
        }
    }

    static IReadOnlyList<App> AttachLinks(IReadOnlyList<App> apps, Inventory inventory) =>
        apps.AsParallel().AsOrdered().WithDegreeOfParallelism(8).Select(app =>
        {
            var links = app.Status switch
            {
                Status.Ready or Status.Guess when app.Package is not null => inventory.LinksFor(app.Package),
                Status.Manual => Inventory.LinksFromUninstallEntry(app.LocalId),
                _ => null,
            };
            return links is null ? app : app with { Links = links };
        }).ToList();

    public static string ToJson(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, Json.Default.Snapshot);

    public static Snapshot FromJson(string json) =>
        JsonSerializer.Deserialize(json, Json.Default.Snapshot) ?? throw new RehomeException("That file isn't a Rehome scan.");
}

internal static class Files
{
    /// <summary>Write next to the target, then rename. A crash never leaves half a file behind.</summary>
    public static void WriteAtomic(string path, string contents) => WriteAtomic(path, System.Text.Encoding.UTF8.GetBytes(contents));

    /// <summary>Exactly these bytes. A sealed scan's check covers every byte, so nothing may re-encode it.</summary>
    public static void WriteAtomic(string path, byte[] contents)
    {
        var full = Path.GetFullPath(path);
        var temp = full + ".tmp";
        File.WriteAllBytes(temp, contents);
        File.Move(temp, full, overwrite: true);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Snapshot))]
internal sealed partial class Json : JsonSerializerContext;
