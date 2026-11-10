using System.Diagnostics;
using Rehome;

return Cli.Run(args);

internal static class Cli
{
    const string Usage = """
        rehome scan                  List installed apps and how each one can be reinstalled
        rehome scan -o FILE          Write the scan as JSON to FILE
        rehome install FILE          Verify and install the apps in a scan, asking about any that don't check out
        rehome install FILE --yes    Same, without asking: installs only apps that pass every check
        rehome verify FILE           Download and check every installer without installing anything
        """;

    public static int Run(string[] args)
    {
        try
        {
            return args switch
            {
                ["scan"] => Scan(output: null),
                ["scan", "-o" or "--out", var file] => Scan(output: file),
                ["install", var plan, "--yes"] => Install.Run(plan, yes: true).GetAwaiter().GetResult(),
                ["install", "--yes", var plan] => Install.Run(plan, yes: true).GetAwaiter().GetResult(),
                ["install", var plan] => Install.Run(plan, yes: false).GetAwaiter().GetResult(),
                ["verify", var plan] => Install.Run(plan, yes: true, verifyOnly: true).GetAwaiter().GetResult(),
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
            return Fail($"winget isn't responding ({e.HResult:X8}). Update App Installer from the Microsoft Store.", 1);
        }
        catch (UnauthorizedAccessException e)
        {
            return Fail($"Windows refused access: {e.Message}", 1);
        }
        catch (IOException e)
        {
            return Fail($"Couldn't read or write a file: {e.Message}", 1);
        }
    }

    static int Scan(string? output)
    {
        var clock = Stopwatch.StartNew();
        var snapshot = Scanner.Run();
        var json = Scanner.ToJson(snapshot);

        if (output is not null)
        {
            Files.WriteAtomic(output, ScanSeal.Seal(snapshot));
        }
        else if (Console.IsOutputRedirected)
        {
            // Piped: JSON on stdout for scripts and agents, summary on stderr for people.
            Console.Out.WriteLine(json);
        }
        else
        {
            Human.Print(snapshot.Apps);
        }

        Console.Error.WriteLine(Human.Summary(snapshot.Apps, clock.Elapsed));
        return 0;
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
    // What needs you goes last, right above the summary, where the terminal leaves you.
    static readonly Status[] Order = [Status.Ready, Status.Launcher, Status.Manual, Status.Guess];

    const int MaxName = 44;

    public static void Print(IReadOnlyList<App> apps)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var width = Math.Min(MaxName, apps.Select(a => a.Name.Length).DefaultIfEmpty(0).Max());

        foreach (var status in Order)
        {
            var group = apps.Where(a => a.Status == status).ToList();
            if (group.Count == 0)
                continue;

            Console.WriteLine();
            Console.WriteLine(Label(status));
            foreach (var app in group)
            {
                Console.WriteLine($"  {Fit(app.Name, width)}  {app.Package?.Id ?? app.Parent ?? ""}".TrimEnd());
                foreach (var setting in app.Settings ?? [])
                    Console.WriteLine($"    {setting.Label}");
            }
        }
        Console.WriteLine();
    }

    static string Fit(string s, int width) =>
        s.Length <= width ? s.PadRight(width) : s[..(width - 1)] + "…";

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
