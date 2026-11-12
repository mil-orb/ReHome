using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Rehome.Restore;
using Rehome.Scan;
using Rehome.Shell.Native;

namespace Rehome.Shell;

/// <summary>
/// The page's only way to reach the machine. A fixed set of methods with fixed shapes; nothing the page sends
/// reaches a shell, a path or a URL. The page refers to apps by id, and the host acts on its own copy of the
/// scan or plan. Long-running work reports progress as pushed events (messages with no id).
/// </summary>
sealed class Bridge(Window owner, Action<string> post)
{
    Snapshot? _snapshot;
    Task<Snapshot>? _scanning;
    IReadOnlyDictionary<string, string> _icons = new Dictionary<string, string>();

    Snapshot? _plan;
    string? _exported;
    bool _bootedOnce;
    HashSet<string> _present = [];
    Task<Inventory>? _inventory;
    CancellationTokenSource? _stop;
    TaskCompletionSource<HashSet<string>>? _decision;
    readonly Dictionary<string, string> _states = [];

    /// <param name="files">Paths of files dropped on the page, read by the host from WebView2's own File objects.
    /// Only "openDropped" uses them; the page can't name a path itself.</param>
    public async void Handle(string message, IReadOnlyList<string>? files = null)
    {
        int id;
        string method;
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt32();
            method = root.GetProperty("method").GetString() ?? "";
            args = root.TryGetProperty("params", out var p) ? p.Clone() : default;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return; // Not one of ours. Say nothing.
        }

        try
        {
            var result = method switch
            {
                "boot" => Boot(),
                "scan" => await Scan(),
                "icons" => await Icons(),
                "export" => await Export(args),
                "showExport" => ShowExport(),
                "open" => Open(),
                "openDropped" => OpenDropped(files ?? []),
                "present" => await Present(),
                "install" => await Install(args),
                "decide" => Decide(args),
                "stop" => Stop(),
                "link" => Link(args),
                "about" => await About(),
                "setPreferences" => SetPreferences(args),
                "showLog" => ShowLog(),
                "log" => LogFromPage(args),
                _ => throw new RehomeException("ReHome doesn't know that request."),
            };
            Reply(id, result, null);
        }
        catch (RehomeException e)
        {
            Reply(id, null, e.Message);
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            Log.Write(e);
            Reply(id, null, $"winget isn't responding (0x{e.HResult:X8}). Update App Installer from the Microsoft Store and try again.");
        }
        catch (Exception e)
        {
            Log.Write(e);
            Reply(id, null, "Something went wrong. Try again.");
        }
    }

    // ---- Start ----

    /// <summary>What the page needs on first paint: version, theme, and a plan if Rehome was opened with a file.</summary>
    string Boot()
    {
        string? plan = null, error = null;
        // A file Rehome was opened with, else the scan an exported Rehome.exe carries inside it.
        try
        {
            if (owner.StartFile is { } file)
            {
                owner.StartFile = null;
                plan = UsePlan(Plan.Open(file));
            }
            else if (!_bootedOnce && ScanInExe.Read(Portable.Origin).Scan is { } inside)
            {
                plan = UsePlan(Plan.Parse(inside));
            }
        }
        catch (RehomeException e) { error = e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write(e);
            error = "Couldn't read the scan inside this ReHome.exe. Copy it again from the old PC.";
        }
        _bootedOnce = true;

        return Json(w =>
        {
            w.WriteString("version", Version);
            w.WriteString("theme", Preferences.Current.Theme);
            if (plan is not null) { w.WritePropertyName("plan"); w.WriteRawValue(plan, skipInputValidation: true); }
            if (error is not null) w.WriteString("error", error);
        });
    }

    // ---- Scan this PC ----

    async Task<string> Scan()
    {
        // One scan at a time. A second request while one runs gets the same result.
        _scanning ??= Task.Run(Scanner.Run);
        try
        {
            _snapshot = await _scanning;
        }
        finally
        {
            _scanning = null;
        }
        return Scanner.ToJson(_snapshot);
    }

    async Task<string> Icons()
    {
        var apps = _snapshot?.Apps ?? [];
        _icons = await Task.Run(() => Timing.Time("icons", () => AppIcons.For(apps)));
        return Json(w =>
        {
            foreach (var (localId, uri) in _icons)
                w.WriteString(localId, uri);
        });
    }

    // Icons travel with the file so the new PC can show them before anything is installed.
    Snapshot WithIcons(Snapshot s) =>
        s with { Apps = s.Apps.Select(a => _icons.TryGetValue(a.LocalId, out var icon) ? a with { Icon = icon } : a).ToList() };

    /// <summary>
    /// One exe to carry to the new PC: this program's bytes with the sealed scan after them. Started there, it opens
    /// the scan by itself. Nothing else is written.
    /// </summary>
    async Task<string> Export(JsonElement args)
    {
        var snapshot = _snapshot ?? throw new RehomeException("Scan first, then export.");
        var exe = Environment.ProcessPath;
        // A developer build is an exe plus loose DLLs. Copying the exe alone would give a file that can't start.
        var looseDll = Path.Combine(AppContext.BaseDirectory, typeof(Bridge).Assembly.GetName().Name + ".dll");
        if (exe is null || File.Exists(looseDll))
            throw new RehomeException("Export works from the published ReHome.exe. This copy is a developer build.");

        var chosen = WithIcons(Selection.Parse(args).Apply(snapshot));
        var to = Dialogs.Save(owner.Handle,
            title: "Save ReHome for your new PC, for example on a USB drive",
            filter: "Program (*.exe)|*.exe",
            defaultExtension: "exe",
            fileName: ExportName.For(Plan.Installable(chosen)),
            initialDir: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (to is null)
            return """{"exported":false}""";

        var scan = ScanSeal.Seal(chosen);
        var staging = to + ".part";
        try
        {
            await Task.Run(() =>
            {
                using (var source = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    // Read before the staging file exists, so a damaged copy of Rehome leaves nothing behind.
                    var program = ScanInExe.Read(source).Program;
                    File.Delete(staging);
                    using var copy = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    ScanInExe.Write(source, program, scan, copy);
                }
                File.Move(staging, to, overwrite: true);
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write(e);
            throw new RehomeException("Couldn't write there. Pick another place, and check it has about 10 MB free.");
        }
        finally
        {
            try { File.Delete(staging); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }

        _exported = to;
        Log.Write($"exported {chosen.Apps.Count} apps to {to}");
        return Json(w =>
        {
            w.WriteBoolean("exported", true);
            w.WriteString("file", to);
        });
    }

    /// <summary>Shows the file Export just wrote. The path is the host's own, never from the page.</summary>
    string ShowExport()
    {
        if (_exported is not { } file || !File.Exists(file))
            throw new RehomeException("That file isn't there anymore.");
        Process.Start(new ProcessStartInfo(Log.Explorer) { UseShellExecute = false, Arguments = $"/select,\"{file}\"" })?.Dispose();
        return "{}";
    }

    // ---- Restore on this PC ----

    string Open()
    {
        var file = Dialogs.Open(owner.Handle,
            filter: "ReHome installer or scan (*.exe;*.json)|*.exe;*.json",
            initialDir: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        return file is null ? "null" : UsePlan(Plan.Open(file));
    }

    /// <summary>A file dropped on the window. One file only, an exported installer or a scan; Plan.Open checks which.</summary>
    string OpenDropped(IReadOnlyList<string> files)
    {
        if (files.Count != 1)
            throw new RehomeException(files.Count == 0
                ? "Drop the ReHome installer you exported on the old PC."
                : "Drop one file at a time.");
        Log.Write($"opened by drop: {Path.GetFileName(files[0])}");
        return UsePlan(Plan.Open(files[0]));
    }

    string UsePlan(Snapshot plan)
    {
        if (_stop is not null)
            throw new RehomeException("Wait for the current install to finish.");
        _plan = plan;
        _present = [];
        lock (_states) _states.Clear();
        return Scanner.ToJson(plan);
    }

    // A failed open isn't cached: winget may have been mid-update, and "try again" has to mean it.
    Task<Inventory> OpenInventory()
    {
        if (_inventory is { IsFaulted: true } or { IsCanceled: true })
            _inventory = null;
        return _inventory ??= Task.Run(Inventory.Open);
    }

    /// <summary>Apps in the plan that are already on this PC. Restore skips them.</summary>
    async Task<string> Present()
    {
        var plan = _plan ?? throw new RehomeException("Open a scan first.");
        var inventory = await OpenInventory();
        _present = await Task.Run(() => Verifier.Present(Plan.Installable(plan), inventory.ReadInstalled()));
        return Json(w =>
        {
            w.WriteStartArray("ids");
            foreach (var localId in _present) w.WriteStringValue(localId);
            w.WriteEndArray();
        });
    }

    async Task<string> Install(JsonElement args)
    {
        var plan = _plan ?? throw new RehomeException("Open a scan first.");
        if (_stop is not null)
            throw new RehomeException("Already installing.");

        var skip = Selection.Parse(args).SkipApps;
        var apps = Plan.Installable(plan).Where(a => !skip.Contains(a.LocalId) && !_present.Contains(a.LocalId)).ToList();
        if (apps.Count == 0)
            throw new RehomeException("Pick at least one app to install.");

        lock (_states) _states.Clear();
        var stop = _stop = new CancellationTokenSource();
        try
        {
            var inventory = await OpenInventory();
            IReadOnlyList<Checked> checks;
            try
            {
                checks = await Verification.Run(apps, inventory, Push, stop.Token);
            }
            catch (OperationCanceledException)
            {
                await SkipUnfinished(apps.Select(a => a.LocalId), "Stopped");
                return Summary();
            }

            var approved = checks.Where(c => c.Verdict == Verdict.Trusted).ToList();
            var questions = checks.Where(c => c.Verdict is Verdict.Unsigned or Verdict.SignerChanged).ToList();

            if (questions.Count > 0 && !stop.IsCancellationRequested)
            {
                var yes = await AskAbout(questions, stop.Token);
                approved.AddRange(questions.Where(q => yes.Contains(q.LocalId)));
                await SkipUnfinished(questions.Where(q => !yes.Contains(q.LocalId)).Select(q => q.LocalId), "Skipped");
            }

            if (approved.Count > 0 && !stop.IsCancellationRequested)
            {
                try
                {
                    await Elevated.Run(approved.Select(c => c.Item!).ToList(), Push, stop.Token);
                }
                catch (Exception)
                {
                    // Whatever went wrong, no row may stay "Installing" while the window says it's done.
                    // Unfinished apps are marked "Not installed", so a retry is one click.
                    await SkipUnfinished(apps.Select(a => a.LocalId), "Not installed");
                    throw;
                }
            }

            await SkipUnfinished(apps.Select(a => a.LocalId), "Stopped");
            return Summary();
        }
        finally
        {
            _decision?.TrySetResult([]);
            _decision = null;
            _stop = null;
            stop.Dispose();
        }
    }

    async Task<HashSet<string>> AskAbout(IReadOnlyList<Checked> questions, CancellationToken stop)
    {
        var decision = _decision = new TaskCompletionSource<HashSet<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostEvent("review", w =>
        {
            w.WriteStartArray("items");
            foreach (var q in questions)
            {
                w.WriteStartObject();
                w.WriteString("localId", q.LocalId);
                w.WriteString("verdict", q.Verdict == Verdict.Unsigned ? "unsigned" : "signer");
                if (q.Host is not null) w.WriteString("host", q.Host);
                if (q.Sha256 is not null) w.WriteString("sha256", q.Sha256);
                if (q.SignedBy is not null) w.WriteString("signedBy", q.SignedBy);
                if (q.Expected is not null) w.WriteString("expected", q.Expected);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });

        using var onStop = stop.Register(() => decision.TrySetResult([]));
        var asked = questions.Select(q => q.LocalId).ToHashSet();
        var answer = await decision.Task;
        answer.IntersectWith(asked); // only apps that were actually in question
        return answer;
    }

    string Decide(JsonElement args)
    {
        _decision?.TrySetResult([.. Selection.Parse(args).ConfirmGuesses]); // { confirm: [ids] }
        return "{}";
    }

    string Stop()
    {
        StopInstalling();
        return "{}";
    }

    public bool Installing => _stop is not null;

    public void StopInstalling()
    {
        try { _stop?.Cancel(); }
        catch (ObjectDisposedException) { } // the run finished between the check and the call
    }

    async Task SkipUnfinished(IEnumerable<string> ids, string why)
    {
        foreach (var localId in ids)
        {
            string? state;
            lock (_states) _states.TryGetValue(localId, out state);
            if (state is null or "verifying" or "verified" or "review" or "installing")
                await Push(new RunEvent { LocalId = localId, State = "skipped", Message = why });
        }
    }

    Task Push(RunEvent e)
    {
        if (e.LocalId is { } localId && e.State is { } state)
        {
            lock (_states) _states[localId] = state;
            // One line per outcome, so "View log" answers what happened to each app. No progress ticks.
            if (state is "installed" or "present" or "restart" or "failed" or "skipped")
                Log.Write($"restore {state,-9} {Log.Field(localId)}{(e.Message is { } m ? "  " + Log.Field(m) : "")}");
        }

        PostEvent("run", w =>
        {
            if (e.LocalId is not null) w.WriteString("localId", e.LocalId);
            if (e.State is not null) w.WriteString("state", e.State);
            if (e.Message is not null) w.WriteString("message", e.Message);
            if (e.Progress is { } p) w.WriteNumber("progress", Math.Round(p, 2));
        });
        return Task.CompletedTask;
    }

    string Summary()
    {
        int Count(string s) { lock (_states) return _states.Values.Count(v => v == s); }
        return Json(w =>
        {
            w.WriteNumber("installed", Count("installed"));
            w.WriteNumber("present", Count("present"));
            w.WriteNumber("restart", Count("restart"));
            w.WriteNumber("failed", Count("failed"));
            w.WriteNumber("skipped", Count("skipped"));
        });
    }

    // ---- Links, menu, preferences ----

    /// <summary>Opens one of an app's official pages. The page names the app and the kind; the URL comes from the host.</summary>
    string Link(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("localId", out var idEl) || idEl.ValueKind != JsonValueKind.String
            || !args.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
            throw new RehomeException("That link isn't available.");

        var localId = idEl.GetString();
        var kind = kindEl.GetString()!;
        var app = (_plan?.Apps ?? []).Concat(_snapshot?.Apps ?? []).FirstOrDefault(a => a.LocalId == localId);
        var url = Links.Https(app?.Links?.Get(kind) ?? (kind == "source" ? Links.CatalogPage(app?.Package) : null))
            ?? throw new RehomeException("That link isn't available.");

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            throw new RehomeException($"Windows has no browser set to open links. The address is {url}");
        }
        return "{}";
    }

    async Task<string> About()
    {
        var winget = _snapshot?.Winget;
        if (winget is null)
        {
            try { winget = await Task.Run(() => new Microsoft.Management.Deployment.PackageManager().Version); }
            catch (System.Runtime.InteropServices.COMException) { winget = null; }
        }
        return Json(w =>
        {
            w.WriteString("version", Version);
            if (winget is not null) w.WriteString("winget", winget);
            w.WriteString("log", Log.FilePath);
        });
    }

    string SetPreferences(JsonElement args)
    {
        var theme = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()! : Preferences.Current.Theme;
        Preferences.Save(Preferences.Current with { Theme = theme });
        owner.ApplyTheme();
        return Json(w => w.WriteString("theme", Preferences.Current.Theme));
    }

    static string ShowLog()
    {
        var folder = Path.GetDirectoryName(Log.FilePath)!;
        Directory.CreateDirectory(folder);
        // Explorer parses /select itself and wants the path quoted after the comma, not the whole argument quoted.
        // Both paths are Rehome's own, never from a page or a file.
        var start = new ProcessStartInfo(Log.Explorer)
        {
            UseShellExecute = false,
            Arguments = File.Exists(Log.FilePath) ? $"/select,\"{Log.FilePath}\"" : $"\"{folder}\"",
        };
        Process.Start(start)?.Dispose();
        return "{}";
    }

    /// <summary>Errors the page caught itself. Capped, and written as plain text.</summary>
    static string LogFromPage(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            // Continuation lines are indented, so nothing the page sends can pass for a log line of its own.
            Log.Write("page: " + new string(m.GetString()!.Take(2000).Where(c => !char.IsControl(c) || c == '\n').ToArray()).Replace("\n", "\n    "));
        return "{}";
    }

    // ---- Plumbing ----

    static string Version => typeof(Bridge).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    void PostEvent(string name, Action<Utf8JsonWriter> data)
    {
        var json = Json(w =>
        {
            w.WriteString("event", name);
            w.WriteStartObject("data");
            data(w);
            w.WriteEndObject();
        });
        Send(json);
    }

    void Reply(int id, string? result, string? error)
    {
        Send(Json(w =>
        {
            w.WriteNumber("id", id);
            if (error is not null)
                w.WriteString("error", error);
            else
            {
                w.WritePropertyName("result");
                w.WriteRawValue(result!, skipInputValidation: true);
            }
        }));
    }

    /// <summary>
    /// Every message to the page goes through here. Progress arrives on worker threads and the web view only takes
    /// messages on the UI thread. A window that's closing, or a page that's reloading, must never turn a reply into
    /// a crash: an install may be running in the elevated helper.
    /// </summary>
    void Send(string json)
    {
        void Deliver()
        {
            try { post(json); }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
            {
                Log.Write(e);
            }
        }

        // A window that's gone drops the message. Posting can't throw.
        if (owner.OnUiThread)
        {
            if (owner.Alive)
                Deliver();
        }
        else
        {
            owner.Post(Deliver);
        }
    }

    static string Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            write(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

static class Log
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rehome", "rehome.log");

    const long MaxBytes = 1 << 20;

    public static void Write(Exception e) => Write(e.ToString());

    /// <summary>A value from a scan or from winget, kept to one line: control characters become spaces.</summary>
    public static string Field(string value) => string.Create(value.Length, value, (span, v) =>
    {
        for (var i = 0; i < v.Length; i++)
            span[i] = char.IsControl(v[i]) ? ' ' : v[i];
    });

    /// <summary>Explorer by full path. A bare name is looked up beside ReHome.exe first.</summary>
    public static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    static readonly Lock Gate = new();

    public static void Write(string text)
    {
        // Verification reports from several threads at once. Appends from two threads can collide and drop a line.
        lock (Gate)
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Keep one previous log, so the file never grows past a megabyte or two.
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Move(FilePath, FilePath + ".1", overwrite: true);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
