using System.Text.RegularExpressions;

namespace Rehome;

/// <summary>One part of an app's configuration a person can choose to carry over, like VS Code's keybindings.</summary>
internal sealed record Setting(string Id, string Label, IReadOnlyList<SettingFile> Files);

/// <summary>
/// A file as a portable template (<c>{AppData}\Code\User\settings.json</c>), never an expanded path.
/// Expanded paths carry the old username, and restore needs the template anyway.
/// </summary>
internal sealed record SettingFile(string Path, long Size);

/// <summary>Where to find one setting. Paths are exact, or use <c>*</c> inside a single segment. No <c>**</c>, no folders.</summary>
internal sealed record Recipe(string Id, string Label, params string[] Paths);

/// <summary>
/// The only settings Rehome will ever read, keyed by winget or Store package id.
/// Compiled in, so nothing on disk can widen it. Rules for adding an entry:
/// name files, never folders (OBS keeps stream keys in service.json beside the profile; ShareX keeps
/// OAuth tokens in UploadersConfig.json beside its settings). Data only: no scripts or profiles that
/// would run code on the new machine. Skip anything that is mostly login state.
/// </summary>
internal static class SettingsCatalog
{
    public static readonly IReadOnlyDictionary<string, Recipe[]> ByPackage = new Dictionary<string, Recipe[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.VisualStudioCode"] =
        [
            new("settings", "Settings", @"{AppData}\Code\User\settings.json"),
            new("keybindings", "Keybindings", @"{AppData}\Code\User\keybindings.json"),
            new("snippets", "Snippets", @"{AppData}\Code\User\snippets\*.json", @"{AppData}\Code\User\snippets\*.code-snippets"),
            // Restored as a list of extension ids to install from the Marketplace, never as copied code.
            new("extensions", "Extensions", @"{Home}\.vscode\extensions\extensions.json"),
        ],
        ["Microsoft.WindowsTerminal"] =
        [
            new("settings", "Settings",
                @"{LocalAppData}\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json",
                @"{LocalAppData}\Microsoft\Windows Terminal\settings.json"),
        ],
        ["Notepad++.Notepad++"] =
        [
            new("settings", "Settings", @"{AppData}\Notepad++\config.xml"),
            new("shortcuts", "Shortcuts", @"{AppData}\Notepad++\shortcuts.xml"),
            new("theme", "Theme", @"{AppData}\Notepad++\stylers.xml"),
        ],
        ["OBSProject.OBSStudio"] =
        [
            new("settings", "Settings", @"{AppData}\obs-studio\user.ini", @"{AppData}\obs-studio\global.ini"),
            new("profiles", "Profiles", @"{AppData}\obs-studio\basic\profiles\*\basic.ini"),
            new("scenes", "Scenes", @"{AppData}\obs-studio\basic\scenes\*.json"),
        ],
        ["ShareX.ShareX"] =
        [
            new("settings", "Settings", @"{Documents}\ShareX\ApplicationConfig.json"),
            new("hotkeys", "Hotkeys", @"{Documents}\ShareX\HotkeysConfig.json"),
        ],
        ["VideoLAN.VLC"] =
        [
            new("settings", "Settings", @"{AppData}\vlc\vlcrc"),
        ],
        ["Git.Git"] =
        [
            new("config", "Config", @"{Home}\.gitconfig"),
        ],
        ["Alacritty.Alacritty"] =
        [
            new("settings", "Settings", @"{AppData}\alacritty\alacritty.toml"),
        ],
    };
}

/// <summary>Finds which catalog settings exist on this machine and attaches the ones that pass <see cref="Secrets"/>.</summary>
internal static class SettingsProbe
{
    const int MaxFilesPerSetting = 64;

    public static IReadOnlyDictionary<string, string> DefaultRoots => new Dictionary<string, string>
    {
        ["{AppData}"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ["{LocalAppData}"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ["{Home}"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ["{Documents}"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    };

    public static IReadOnlyList<App> Attach(IReadOnlyList<App> apps) =>
        Attach(apps, SettingsCatalog.ByPackage, DefaultRoots);

    public static IReadOnlyList<App> Attach(IReadOnlyList<App> apps, IReadOnlyDictionary<string, Recipe[]> catalog, IReadOnlyDictionary<string, string> roots) =>
        apps.Select(app =>
        {
            if (app.Status is not (Status.Ready or Status.Guess) || app.Package is null || !catalog.TryGetValue(app.Package.Id, out var recipes))
                return app;

            var found = recipes.Select(r => Probe(r, roots)).OfType<Setting>().ToList();
            return found.Count == 0 ? app : app with { Settings = found };
        }).ToList();

    static Setting? Probe(Recipe recipe, IReadOnlyDictionary<string, string> roots)
    {
        var files = new List<SettingFile>();
        foreach (var template in recipe.Paths)
        {
            var (token, root) = RootOf(template, roots);
            if (root is null)
                continue;

            foreach (var path in Match(root + template[token.Length..]))
            {
                if (files.Count == MaxFilesPerSetting)
                    break;
                if (Secrets.Found(path) is { } reason)
                {
                    Trace($"skipped {path}: {reason}");
                    continue;
                }
                files.Add(new SettingFile(token + path[root.Length..], new FileInfo(path).Length));
            }
        }
        return files.Count == 0 ? null : new Setting(recipe.Id, recipe.Label, files);
    }

    static (string Token, string? Root) RootOf(string template, IReadOnlyDictionary<string, string> roots)
    {
        foreach (var (token, root) in roots)
            if (template.StartsWith(token, StringComparison.Ordinal) && !string.IsNullOrEmpty(root))
                return (token, root.TrimEnd('\\'));
        return ("", null);
    }

    static readonly EnumerationOptions Plain = new()
    {
        MatchType = MatchType.Simple, // no 8.3 or DOS wildcard quirks
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // never follow a link out of the folder
        RecurseSubdirectories = false,
    };

    /// <summary>Walks a path one segment at a time. A segment with <c>*</c> matches within that folder only.</summary>
    internal static IEnumerable<string> Match(string pattern)
    {
        var parts = pattern.Split('\\');
        IEnumerable<string> dirs = [parts[0] + "\\"];

        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            var last = i == parts.Length - 1;
            if (part.Length == 0 || part is "." or ".." || part.Contains("**"))
                return [];

            dirs = (last, part.Contains('*')) switch
            {
                (true, true) => dirs.SelectMany(d => Directory.EnumerateFiles(d, part, Plain)).Order(StringComparer.OrdinalIgnoreCase),
                (true, false) => dirs.Select(d => Path.Combine(d, part)).Where(IsPlainFile),
                (false, true) => dirs.SelectMany(d => Directory.EnumerateDirectories(d, part, Plain)).Order(StringComparer.OrdinalIgnoreCase),
                (false, false) => dirs.Select(d => Path.Combine(d, part)).Where(IsPlainDirectory),
            };
        }
        return dirs.ToList();
    }

    static bool IsPlainFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    static bool IsPlainDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    static void Trace(string line)
    {
        if (Environment.GetEnvironmentVariable("REHOME_TRACE") == "1")
            Console.Error.WriteLine($"          {line}");
    }
}

/// <summary>
/// The last gate before a settings file is offered. If a file looks like it holds a credential, Rehome leaves it
/// behind. A false positive costs a setting the person can copy by hand; a false negative leaks a key.
/// </summary>
internal static partial class Secrets
{
    public const long MaxBytes = 1 << 20;

    /// <summary>Why the file can't be taken, or null if it's clean.</summary>
    public static string? Found(string path)
    {
        string text;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes)
                return "too large";
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }

        return Scan(text);
    }

    public static string? Scan(string text)
    {
        if (PrivateKey().IsMatch(text)) return "private key";
        if (KnownToken().IsMatch(text)) return "token";
        if (JsonSecret().IsMatch(text)) return "secret field";
        if (IniSecret().IsMatch(text)) return "secret field";
        if (XmlSecret().IsMatch(text)) return "secret field";

        foreach (Match m in LongRun().Matches(text))
            if (LooksRandom(m.Value))
                return "random string";

        return null;
    }

    // Base64 or hex keys look like noise: mixed case and digits with high per-character entropy.
    // Long identifiers and paths don't: they're mostly lowercase letters from a small alphabet.
    static bool LooksRandom(string s)
    {
        if (!s.Any(char.IsDigit) || !s.Any(char.IsUpper) || !s.Any(char.IsLower))
            return false;

        var counts = new Dictionary<char, int>();
        foreach (var c in s)
            counts[c] = counts.GetValueOrDefault(c) + 1;

        var entropy = counts.Values.Sum(n => -(double)n / s.Length * Math.Log2((double)n / s.Length));
        return entropy >= 4.3;
    }

    const string Words = "password|passwd|secret|token|apikey|api_key|api-key|accesskey|access_key|privatekey|private_key|credential";

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    // JWTs and vendor tokens with fixed prefixes: GitHub, OpenAI-style, Slack, AWS, Google.
    [GeneratedRegex(@"eyJ[\w-]{8,}\.eyJ[\w-]{8,}\.|\bgh[pousr]_[A-Za-z0-9]{30,}|\bgithub_pat_\w{20,}|\bsk-[\w-]{20,}|\bxox[abpr]-[\w-]{10,}|\bAKIA[0-9A-Z]{16}\b|\bAIza[\w-]{35}")]
    private static partial Regex KnownToken();

    // "apiToken": "abc"  (an empty string or a non-string value is fine)
    [GeneratedRegex(@"""[^""\r\n]*(?:" + Words + @")[^""\r\n]*""\s*:\s*""[^""\r\n]+""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecret();

    // api_token = abc   (commented-out lines and empty values are fine)
    [GeneratedRegex(@"^[ \t]*[\w.-]*(?:" + Words + @")[\w.-]*[ \t]*[=:][ \t]*(?!""""|'')[^\s#;]", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex IniSecret();

    // <ProxyPassword>abc</ProxyPassword>  or  password="abc"
    [GeneratedRegex(@"<(\w*(?:" + Words + @")\w*)>[^<\s][^<]*</\1>|\b\w*(?:" + Words + @")\w*\s*=\s*""[^""]+""", RegexOptions.IgnoreCase)]
    private static partial Regex XmlSecret();

    [GeneratedRegex(@"[A-Za-z0-9+/_=-]{40,}")]
    private static partial Regex LongRun();
}
