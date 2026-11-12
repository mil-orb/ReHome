using Rehome.Restore;

namespace Rehome.Tests;

public sealed class PlanTests : IDisposable
{
    readonly string _file = Path.Combine(Path.GetTempPath(), $"rehome-plan-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_file);

    Snapshot Load(string json)
    {
        File.WriteAllText(_file, json);
        return Plan.Load(_file);
    }

    static string Doc(string apps, int format = 1) =>
        $$"""{ "format": {{format}}, "machine": "OLD", "taken": "2026-11-10T00:00:00Z", "winget": "1.29", "apps": [ {{apps}} ] }""";

    [Fact]
    public void A_clean_scan_loads()
    {
        var s = Load(Doc("""{ "name": "7-Zip", "status": "Ready", "package": { "id": "7zip.7zip", "source": "winget" }, "localId": "a" }"""));
        Assert.Equal("7zip.7zip", Assert.Single(Plan.Installable(s)).Package!.Id);
    }

    [Fact]
    public void Each_local_id_names_one_app()
    {
        // JSON's \n escape: the id decodes to "a", newline, "b".
        var s = Load(Doc(
            """{ "name": "Git", "status": "Ready", "package": { "id": "Git.Git", "source": "winget" }, "localId": "same" },""" +
            """{ "name": "Not Git", "status": "Ready", "package": { "id": "Evil.Thing", "source": "winget" }, "localId": "same" },""" +
            """{ "name": "Bad id", "status": "Ready", "package": { "id": "Foo.Bar", "source": "winget" }, "localId": "a\nb" }"""));
        var app = Assert.Single(s.Apps);
        Assert.Equal("Git", app.Name);
    }

    [Fact]
    public void An_icon_with_a_trailing_newline_is_dropped()
    {
        var s = Load(Doc("""{ "name": "X", "status": "Manual", "localId": "a", "icon": "data:image/png;base64,AAAA\n" }"""));
        Assert.Null(Assert.Single(s.Apps).Icon);
    }

    [Theory]
    [InlineData("Foo.Bar;calc", "winget")]
    [InlineData("Foo.Bar --override x", "winget")]
    [InlineData("-Foo", "winget")]
    [InlineData("Foo.Bar", "https://evil.example/source")]
    [InlineData("Foo.Bar", "custom")]
    public void Unsafe_packages_are_demoted_to_manual(string id, string source)
    {
        var s = Load(Doc($$"""{ "name": "X", "status": "Ready", "package": { "id": "{{id.Replace("\"", "\\\"")}}", "source": "{{source}}" }, "localId": "a" }"""));
        var app = Assert.Single(s.Apps);
        Assert.Equal(Status.Manual, app.Status);
        Assert.Null(app.Package);
        Assert.Empty(Plan.Installable(s));
    }

    [Fact]
    public void Only_confirmed_guesses_install()
    {
        var s = Load(Doc("""
            { "name": "A", "status": "Guess", "package": { "id": "A.A", "source": "winget" }, "localId": "a" },
            { "name": "B", "status": "Guess", "confirmed": true, "package": { "id": "B.B", "source": "winget" }, "localId": "b" },
            { "name": "C", "status": "Manual", "localId": "c" }
            """));
        Assert.Equal(["b"], Plan.Installable(s).Select(a => a.LocalId));
    }

    [Fact]
    public void Links_are_https_only_and_the_catalog_page_is_rebuilt_from_the_id()
    {
        var s = Load(Doc("""
            { "name": "A", "status": "Ready", "package": { "id": "Vendor.App", "source": "winget" }, "localId": "a",
              "links": { "homepage": "http://vendor.example", "support": "https://vendor.example/help", "privacy": "javascript:alert(1)",
                         "source": "https://evil.example/manifests" } }
            """));
        var links = Assert.Single(s.Apps).Links!;
        Assert.Null(links.Homepage);
        Assert.Null(links.Privacy);
        Assert.Equal("https://vendor.example/help", links.Support);
        Assert.Equal("https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/Vendor/App", links.Source);
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", true)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=", false)]
    [InlineData("https://evil.example/x.png", false)]
    [InlineData("data:image/png;base64,iVBO\"><script>", false)]
    public void Only_png_data_icons_survive(string icon, bool kept)
    {
        var s = Load(Doc($$"""{ "name": "A", "status": "Manual", "icon": {{System.Text.Json.JsonSerializer.Serialize(icon)}}, "localId": "a" }"""));
        Assert.Equal(kept, Assert.Single(s.Apps).Icon is not null);
    }

    [Fact]
    public void A_newer_format_is_refused_with_a_reason()
    {
        var e = Assert.Throws<RehomeException>(() => Load(Doc("", format: 2)));
        Assert.Contains("newer ReHome", e.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "format": 1 }""")]
    [InlineData("[]")]
    public void Garbage_is_refused(string json) =>
        Assert.Throws<RehomeException>(() => Load(json));

    [Fact]
    public void Missing_file_is_refused() =>
        Assert.Throws<RehomeException>(() => Plan.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")));
}

public class WingetArgsTests
{
    static InstallItem Item(string id = "7zip.7zip", string source = "winget") => new()
    {
        LocalId = "a", Name = "7-Zip", Id = id, Source = source,
        Version = "26.06", Scope = "machine", Architecture = "x64", InstallerType = "wix",
    };

    [Fact]
    public void Winget_installs_are_pinned_to_the_verified_installer()
    {
        Assert.Equal(
            ["install", "--id", "7zip.7zip", "--exact", "--source", "winget", "--version", "26.06", "--scope", "machine",
             "--architecture", "x64", "--installer-type", "wix", "--silent", "--disable-interactivity", "--no-upgrade",
             "--accept-package-agreements", "--accept-source-agreements"],
            WingetCli.InstallArgs(Item()));
    }

    [Fact]
    public void Store_installs_carry_only_the_id()
    {
        var args = WingetCli.InstallArgs(Item("9WZDNCRFHVN5", "msstore"));
        Assert.DoesNotContain("--version", args);
        Assert.Equal(["install", "--id", "9WZDNCRFHVN5", "--exact", "--source", "msstore"], args.Take(6));
    }

    [Fact]
    public void No_override_flags_ever()
    {
        var args = WingetCli.InstallArgs(Item());
        Assert.DoesNotContain("--ignore-security-hash", args);
        Assert.DoesNotContain("--force", args);
        Assert.DoesNotContain("--override", args);
    }

    [Theory]
    [InlineData("Foo;calc", "winget", null)]
    [InlineData("Foo", "evil", null)]
    [InlineData("Foo", "winget", "1.0 --override")]
    public void Bad_items_are_rejected_before_winget_runs(string id, string source, string? version)
    {
        var item = Item(id, source) with { Version = version };
        Assert.NotNull(WingetCli.Problem(item));
        Assert.Throws<ArgumentException>(() => WingetCli.InstallArgs(item));
    }

    [Fact]
    public void Unknown_scope_or_type_is_rejected()
    {
        Assert.NotNull(WingetCli.Problem(Item() with { Scope = "system" }));
        Assert.NotNull(WingetCli.Problem(Item() with { InstallerType = "script" }));
        Assert.NotNull(WingetCli.Problem(Item() with { Architecture = "riscv" }));
    }
}

public class TrustTests
{
    [Theory]
    [InlineData(unchecked((int)0x800B010C), true)]  // revoked
    [InlineData(unchecked((int)0x800B0111), true)]  // explicitly distrusted
    [InlineData(unchecked((int)0x800B0109), true)]  // untrusted root
    [InlineData(unchecked((int)0x800B010E), false)] // revocation server unreachable: not a verdict
    [InlineData(unchecked((int)0x800B0100), false)] // not signed: the review question handles it
    [InlineData(0, false)]
    public void Only_a_definite_no_from_Windows_fails_an_install(int result, bool distrust) =>
        Assert.Equal(distrust, Rehome.Scan.Authenticode.IsDistrust(result));

    static Signer S(string org, bool valid = true) => new(org, org, $"O={org}", "exe", valid);

    [Fact] public void Same_org_is_trusted() => Assert.Equal(Verdict.Trusted, Trust.Decide(S("Microsoft Corporation"), S("Microsoft Corporation."), "exe"));
    [Fact] public void Different_org_needs_a_person() => Assert.Equal(Verdict.SignerChanged, Trust.Decide(S("Discord Inc."), S("Totally Discord Ltd"), "exe"));
    [Fact] public void Unsigned_needs_a_person() => Assert.Equal(Verdict.Unsigned, Trust.Decide(S("Igor Pavlov"), null, "wix"));
    [Fact] public void Invalid_signature_counts_as_unsigned() => Assert.Equal(Verdict.Unsigned, Trust.Decide(null, S("Vendor", valid: false), "exe"));
    [Fact] public void Nothing_on_record_and_validly_signed_is_trusted() => Assert.Equal(Verdict.Trusted, Trust.Decide(null, S("Vendor"), "inno"));
    [Fact] public void Msix_is_checked_by_windows() => Assert.Equal(Verdict.Trusted, Trust.Decide(S("Vendor"), null, "msix"));
    [Fact] public void Msix_with_another_signer_still_needs_a_person() => Assert.Equal(Verdict.SignerChanged, Trust.Decide(S("Vendor"), S("Other"), "msix"));

    [Fact]
    public void The_downloaded_manifest_gives_the_exact_installer()
    {
        const string yaml = """
            # Created with WinGet
            PackageIdentifier: 7zip.7zip
            PackageVersion: 26.06
            InstallerType: exe
            Installers:
            - Architecture: x64
              InstallerType: wix
              InstallerSha256: 6dc822569e777d9a3ecd5ffcbc22b55600af87e6c5d9810a64e8566cf5a4af9f
              InstallerUrl: https://github.com/ip7z/7zip/releases/download/26.06/7z2606-x64.msi
              Scope: machine
            ManifestType: merged
            """;
        var m = Trust.ReadManifest(yaml);
        Assert.Equal("26.06", m.Version);
        Assert.Equal("wix", m.Type);
        Assert.Equal("machine", m.Scope);
        Assert.Equal("x64", m.Architecture);
        Assert.Equal("6dc822569e777d9a3ecd5ffcbc22b55600af87e6c5d9810a64e8566cf5a4af9f", m.Sha256);
        Assert.StartsWith("https://github.com/ip7z/", m.Url);
    }

    [Fact]
    public void Root_defaults_apply_when_the_installer_omits_them()
    {
        var m = Trust.ReadManifest("PackageVersion: '1.2'\nInstallerType: inno\nScope: user\nInstallers:\n- Architecture: x64\n  InstallerSha256: AB\n");
        Assert.Equal("1.2", m.Version);
        Assert.Equal("inno", m.Type);
        Assert.Equal("user", m.Scope);
    }
}

public class ErrorTests
{
    [Theory]
    [InlineData(0, "Installed")]
    [InlineData(unchecked((int)0x8A150061), "AlreadyThere")]
    [InlineData(unchecked((int)0x8A15010D), "AlreadyThere")]
    [InlineData(unchecked((int)0x8A150109), "NeedsRestart")]
    [InlineData(3010, "NeedsRestart")]
    [InlineData(unchecked((int)0x8A150011), "Failed")]
    [InlineData(1603, "Failed")]
    public void Results_are_classified(int code, string outcome) => Assert.Equal(outcome, Errors.Classify(code).ToString());

    [Fact]
    public void Hash_mismatch_says_it_wont_install() => Assert.Contains("won't install", Errors.Describe(Errors.HashMismatch));

    [Fact]
    public void Unknown_codes_keep_their_hex() => Assert.Contains("0x8A15FFFF", Errors.Describe(unchecked((int)0x8A15FFFF)));
}

public class LinkTests
{
    [Theory]
    [InlineData("https://7-zip.org/", "https://7-zip.org/")]
    [InlineData("  https://example.com/a b ", "https://example.com/a%20b")]
    [InlineData("http://example.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows", null)]
    [InlineData("https://user:pass@example.com", null)]
    [InlineData("example.com", null)]
    [InlineData(null, null)]
    public void Only_https_survives(string? url, string? expected) => Assert.Equal(expected, Links.Https(url));

    [Fact]
    public void Store_page_uses_the_store_id() =>
        Assert.Equal("https://apps.microsoft.com/detail/9WZDNCRFHVN5", Links.CatalogPage(new CatalogRef("9WZDNCRFHVN5", "msstore")));

    [Fact]
    public void Manifest_page_follows_the_winget_pkgs_layout() =>
        Assert.Equal("https://github.com/microsoft/winget-pkgs/tree/master/manifests/n/Notepad%2B%2B/Notepad%2B%2B",
            Links.CatalogPage(new CatalogRef("Notepad++.Notepad++", "winget")));
}

public class PresentTests
{
    static App Store(string localId, string id) =>
        new() { Name = id, Status = Status.Ready, Package = new CatalogRef(id, "msstore"), LocalId = localId };

    [Theory]
    [InlineData(@"MSIX\Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe", "Microsoft.WindowsCalculator_8wekyb3d8bbwe")]
    [InlineData(@"MSIX\NotepadPlusPlus_1.0.0.0_neutral__7njy0v32s6xk6", "NotepadPlusPlus_7njy0v32s6xk6")]
    [InlineData(@"ARP\Machine\X64\Notepad++", null)]
    [InlineData(@"MSIX\Broken_1.0", null)]
    public void Family_comes_from_the_package_full_name(string localId, string? family) =>
        Assert.Equal(family, Verifier.FamilyOf(localId));

    [Fact]
    public void Store_app_counts_as_present_when_its_family_is_installed_at_any_version()
    {
        var calc = Store(@"MSIX\Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe", "9WZDNCRFHVN5");
        var photos = Store(@"MSIX\Microsoft.Windows.Photos_2025.1.0.0_x64__8wekyb3d8bbwe", "9WZDNCRFJBH4");
        var here = new Installed
        {
            LocalId = @"MSIX\Microsoft.WindowsCalculator_11.2700.0.0_x64__8wekyb3d8bbwe",
            Name = "Calculator",
            FamilyNames = ["Microsoft.WindowsCalculator_8wekyb3d8bbwe"],
        };

        Assert.Equal([calc.LocalId], Verifier.Present([calc, photos], [here]));
    }

    [Fact]
    public void Winget_app_counts_as_present_by_correlated_id()
    {
        var git = new App { Name = "Git", Status = Status.Ready, Package = new CatalogRef("Git.Git", "winget"), LocalId = @"ARP\Machine\X64\Git_is1" };
        var here = new Installed { LocalId = "other", Name = "Git", Correlated = new CatalogRef("git.git", "winget") };

        Assert.Equal([git.LocalId], Verifier.Present([git], [here]));
    }
}
