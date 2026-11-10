using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rehome;
using Rehome.Scan;

return Cli.Run(args);

internal sealed class RehomeException(string message) : Exception(message);

internal static class Cli
{
    const string Usage = """
        rehome scan            List installed apps and how each one can be reinstalled
        rehome scan -o FILE    Write the scan as JSON to FILE
        """;

    public static int Run(string[] args)
    {
        try
        {
            return args switch
            {
                ["scan"] => Scan(output: null),
                ["scan", "-o" or "--out", var file] => Scan(output: file),
                ["--version"] => Print(typeof(Cli).Assembly.GetName().Version!.ToString(3)),
                _ => Fail(Usage, 2),
            };
        }
        catch (RehomeException e)
        {
            return Fail(e.Message, 1);
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            return Fail($"winget isn't available ({e.HResult:X8}). Install App Installer from the Microsoft Store.", 1);
        }
    }

    static int Scan(string? output)
    {
        var clock = Stopwatch.StartNew();
        var inventory = Inventory.Open();
        var installed = inventory.ReadInstalled();
        var apps = Timing.Time("resolve", () => Resolver.Resolve(installed, inventory));

        var scan = new Snapshot
        {
            Machine = Environment.MachineName,
            Taken = DateTimeOffset.Now,
            Winget = inventory.WingetVersion,
            Apps = apps,
        };

        var json = JsonSerializer.Serialize(scan, Json.Default.Snapshot);

        if (output is not null)
        {
            WriteAtomic(output, json);
        }
        else if (Console.IsOutputRedirected)
        {
            // Piped: JSON on stdout for scripts and agents, summary on stderr for people.
            Console.Out.WriteLine(json);
        }
        else
        {
            Human.Print(apps);
        }

        Console.Error.WriteLine(Human.Summary(apps, clock.Elapsed));
        return 0;
    }

    /// <summary>Write next to the target, then rename. A crash never leaves half a file behind.</summary>
    static void WriteAtomic(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        var temp = full + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, full, overwrite: true);
    }

    static int Print(string text)
    {
        Console.Out.WriteLine(text);
        return 0;
    }

    static int Fail(string text, int code)
    {
        Console.Error.WriteLine(text);
        return code;
    }
}

internal static class Human
{
    public static void Print(IReadOnlyList<App> apps)
    {
        foreach (var group in apps.Where(a => a.Status != Status.Companion).GroupBy(a => a.Status))
        {
            Console.WriteLine();
            Console.WriteLine(Label(group.Key));
            foreach (var app in group)
                Console.WriteLine($"  {app.Name,-44} {app.Package?.Id ?? app.Parent ?? ""}");
        }
        Console.WriteLine();
    }

    public static string Summary(IReadOnlyList<App> apps, TimeSpan elapsed)
    {
        int Count(Status s) => apps.Count(a => a.Status == s);
        return $"{Count(Status.Ready)} ready, {Count(Status.Guess)} to confirm, {Count(Status.Launcher)} in launchers, {Count(Status.Manual)} manual  ({elapsed.TotalSeconds:0.0}s)";
    }

    static string Label(Status s) => s switch
    {
        Status.Ready => "Ready",
        Status.Guess => "Confirm",
        Status.Manual => "Manual",
        Status.Launcher => "Launchers",
        _ => s.ToString(),
    };
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Snapshot))]
internal sealed partial class Json : JsonSerializerContext;
