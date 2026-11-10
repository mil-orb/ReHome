namespace Rehome.Tests;

public sealed class SettingsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"rehome-settings-{Guid.NewGuid():N}");
    readonly Dictionary<string, string> _roots;

    public SettingsTests()
    {
        Directory.CreateDirectory(_root);
        _roots = new() { ["{AppData}"] = _root };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    void Put(string relative, string contents)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    static App Ready(string id) => new() { Name = id, Status = Status.Ready, Package = new(id, "winget"), LocalId = "local-" + id };

    IReadOnlyList<Setting> Probe(params Recipe[] recipes) =>
        SettingsProbe.Attach([Ready("Vendor.App")], new Dictionary<string, Recipe[]> { ["Vendor.App"] = recipes }, _roots)[0].Settings ?? [];

    [Fact]
    public void Files_are_recorded_as_portable_templates()
    {
        Put(@"App\settings.json", """{ "theme": "dark" }""");

        var setting = Assert.Single(Probe(new Recipe("settings", "Settings", @"{AppData}\App\settings.json")));

        var file = Assert.Single(setting.Files);
        Assert.Equal(@"{AppData}\App\settings.json", file.Path);
        Assert.Equal(new FileInfo(Path.Combine(_root, @"App\settings.json")).Length, file.Size);
        Assert.DoesNotContain(Environment.UserName, file.Path);
    }

    [Fact]
    public void Missing_files_offer_nothing()
    {
        Assert.Empty(Probe(new Recipe("settings", "Settings", @"{AppData}\App\settings.json")));
    }

    [Fact]
    public void Wildcards_stay_inside_one_folder_and_name_exact_files()
    {
        Put(@"obs\profiles\Stream\basic.ini", "[Video]\nFPS=60");
        Put(@"obs\profiles\Stream\service.json", """{ "settings": { "key": "live_123" } }""");
        Put(@"obs\profiles\Record\basic.ini", "[Video]\nFPS=30");
        Put(@"obs\profiles\Record\deeper\basic.ini", "[Video]");

        var setting = Assert.Single(Probe(new Recipe("profiles", "Profiles", @"{AppData}\obs\profiles\*\basic.ini")));

        Assert.Equal(
            [@"{AppData}\obs\profiles\Record\basic.ini", @"{AppData}\obs\profiles\Stream\basic.ini"],
            setting.Files.Select(f => f.Path));
    }

    [Theory]
    [InlineData(@"{AppData}\App\**\x.json")]
    [InlineData(@"{AppData}\App\..\x.json")]
    [InlineData(@"{Unknown}\x.json")]
    public void Unsafe_templates_match_nothing(string template)
    {
        Put(@"App\x.json", "{}");
        Put(@"x.json", "{}");
        Assert.Empty(Probe(new Recipe("x", "X", template)));
    }

    [Fact]
    public void A_file_with_a_secret_is_left_behind()
    {
        Put(@"App\scenes\clean.json", """{ "name": "Main" }""");
        Put(@"App\scenes\overlay.json", """{ "url": "https://example.com/o", "apiToken": "abc123" }""");

        var setting = Assert.Single(Probe(new Recipe("scenes", "Scenes", @"{AppData}\App\scenes\*.json")));

        Assert.Equal([@"{AppData}\App\scenes\clean.json"], setting.Files.Select(f => f.Path));
    }

    [Fact]
    public void Only_ready_and_guess_apps_are_probed()
    {
        Put(@"App\settings.json", "{}");
        var catalog = new Dictionary<string, Recipe[]> { ["Vendor.App"] = [new("settings", "Settings", @"{AppData}\App\settings.json")] };
        var manual = Ready("Vendor.App") with { Status = Status.Manual };

        Assert.Null(SettingsProbe.Attach([manual], catalog, _roots)[0].Settings);
    }

    [Theory]
    [InlineData("""{ "github.token": "ghp_abcdefghijklmnopqrstuvwxyz0123456789" }""")]
    [InlineData("""{ "password": "hunter2" }""")]
    [InlineData("""{ "proxy": { "ProxyPassword": "x" } }""")]
    [InlineData("api_key = abc")]
    [InlineData("[auth]\nsecret: s3cr3t")]
    [InlineData("<Options><ProxyPassword>x</ProxyPassword></Options>")]
    [InlineData("""<Account token="abc" />""")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nabc")]
    [InlineData("auth=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.sig")]
    [InlineData("""{ "overlay": "https://x.example/widget/Qm9vbXNoYWthbGFrYTEyMzQ1Njc4OTBBQkNERUZHSElKS0w" }""")]
    public void Secrets_are_caught(string text) => Assert.NotNull(Secrets.Scan(text));

    [Theory]
    [InlineData("""{ "password": "" }""")]
    [InlineData("""{ "editor.tokenColorCustomizations": { "comments": "#888" } }""")]
    [InlineData("""{ "git.terminalAuthentication": true }""")]
    [InlineData("#http-password=\nkey-play=Space")]
    [InlineData("password=\"\"")]
    [InlineData("""{ "relativeLocation": "ms-python.python-2024.0.1-win32-x64", "uuid": "f1f59ae4-9318-4f3c-a9b5-81b2eaa5f8a5" }""")]
    [InlineData("""<GUIConfig name="ScintillaPrimaryView" lineNumberMargin="show" />""")]
    [InlineData("""{ "path": "C:/Users/someone/AppData/Roaming/Code/User/workspaceStorage/settings" }""")]
    public void Ordinary_settings_pass(string text) => Assert.Null(Secrets.Scan(text));

    [Fact]
    public void Catalog_names_exact_data_files_only()
    {
        string[] banned = ["service.json", "UploadersConfig.json", "prefs", ".ssh"];
        string[] code = [".ps1", ".psm1", ".bat", ".cmd", ".vbs", ".js", ".ahk", ".exe", ".dll", ".lnk", ".reg", ".py"];

        foreach (var (package, recipes) in SettingsCatalog.ByPackage)
        foreach (var recipe in recipes)
        {
            Assert.NotEmpty(recipe.Paths);
            Assert.Equal(recipes.Length, recipes.Select(r => r.Id).Distinct().Count());
            foreach (var path in recipe.Paths)
            {
                Assert.True(SettingsProbe.DefaultRoots.Keys.Any(path.StartsWith), $"{package}: {path} has no known root");
                Assert.DoesNotContain("**", path);
                Assert.DoesNotContain("..", path);
                var file = Path.GetFileName(path);
                Assert.False(string.IsNullOrEmpty(file) || file == "*", $"{package}: {path} names a folder");
                Assert.DoesNotContain(banned, b => path.Contains(b, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(code, ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}

public class SelectionTests
{
    static App Make(string id, Status status, params string[] settings) => new()
    {
        Name = id,
        Status = status,
        LocalId = id,
        Settings = settings.Length == 0 ? null : settings.Select(s => new Setting(s, s, [new SettingFile($@"{{AppData}}\{s}", 1)])).ToList(),
    };

    static Snapshot Snap(params App[] apps) => new() { Machine = "PC", Taken = DateTimeOffset.UnixEpoch, Winget = "1", Apps = apps };

    [Fact]
    public void Skipped_settings_are_dropped_and_the_rest_kept()
    {
        var result = new Selection(new HashSet<string>(), new HashSet<string>(), new HashSet<(string, string)> { ("code", "keybindings") })
            .Apply(Snap(Make("code", Status.Ready, "settings", "keybindings")));

        Assert.Equal(["settings"], Assert.Single(result.Apps).Settings!.Select(s => s.Id));
    }

    [Fact]
    public void Skipping_every_setting_leaves_none()
    {
        var result = new Selection(new HashSet<string>(), new HashSet<string>(), new HashSet<(string, string)> { ("code", "settings") })
            .Apply(Snap(Make("code", Status.Ready, "settings")));

        Assert.Null(Assert.Single(result.Apps).Settings);
    }

    [Fact]
    public void Settings_of_an_unconfirmed_guess_go_with_it()
    {
        var result = new Selection(new HashSet<string>(), new HashSet<string>(), new HashSet<(string, string)>())
            .Apply(Snap(Make("guess", Status.Guess, "settings"), Make("ready", Status.Ready)));

        Assert.Equal(["ready"], result.Apps.Select(a => a.LocalId));
    }

    [Fact]
    public void Confirmed_guesses_are_marked_and_skipped_apps_removed()
    {
        var result = new Selection(new HashSet<string> { "ready" }, new HashSet<string> { "guess" }, new HashSet<(string, string)>())
            .Apply(Snap(Make("guess", Status.Guess), Make("ready", Status.Ready), Make("manual", Status.Manual)));

        Assert.Equal(["guess", "manual"], result.Apps.Select(a => a.LocalId));
        Assert.True(result.Apps[0].Confirmed);
    }

    [Fact]
    public void Bridge_params_parse()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{ "skip": ["a"], "confirm": ["g"], "skipSettings": { "code": ["keybindings", "snippets"] } }""");
        var s = Selection.Parse(doc.RootElement);

        Assert.Equal(["a"], s.SkipApps);
        Assert.Equal(["g"], s.ConfirmGuesses);
        Assert.Equal([("code", "keybindings"), ("code", "snippets")], s.SkipSettings.Order());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "skip": "a", "confirm": {}, "skipSettings": [] }""")]
    [InlineData("""{ "skip": [1, null, {}], "skipSettings": { "code": "keybindings", "x": [2] } }""")]
    public void Malformed_params_select_nothing_extra(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var s = Selection.Parse(doc.RootElement);

        Assert.Empty(s.SkipApps);
        Assert.Empty(s.ConfirmGuesses);
        Assert.Empty(s.SkipSettings);
    }

    [Fact]
    public void Settings_survive_a_json_round_trip()
    {
        var snap = Snap(Make("code", Status.Ready, "settings"));
        var back = Scanner.FromJson(Scanner.ToJson(snap));
        Assert.Equal(@"{AppData}\settings", back.Apps[0].Settings![0].Files[0].Path);
    }
}
