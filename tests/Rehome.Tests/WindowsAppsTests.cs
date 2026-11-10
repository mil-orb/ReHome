namespace Rehome.Tests;

/// <summary>Apps Windows installs itself are left out. Real entries from the dev PC.</summary>
public class WindowsAppsTests
{
    sealed class NoCatalog : ICatalog
    {
        public IReadOnlyList<CatalogEntry> Search(string name, string source) => [];
    }

    static readonly HashSet<string> Provisioned = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        "Microsoft.WindowsStore_8wekyb3d8bbwe",
        "MicrosoftWindows.CrossDevice_cw5n1h2txyewy",
        "MicrosoftCorporationII.WindowsSubsystemForLinux_8wekyb3d8bbwe",
        "RealtekSemiconductorCorp.RealtekAudioControl_dt26b99r8h8gj",
    };

    [Theory]
    [InlineData("Microsoft.XboxGamingOverlay_8wekyb3d8bbwe", true)] // Game Bar
    [InlineData("Microsoft.WindowsStore_8wekyb3d8bbwe", true)]
    [InlineData("MicrosoftWindows.CrossDevice_cw5n1h2txyewy", true)]
    [InlineData("MicrosoftCorporationII.WindowsSubsystemForLinux_8wekyb3d8bbwe", false)] // provisioned by installing it
    [InlineData("RealtekSemiconductorCorp.RealtekAudioControl_dt26b99r8h8gj", false)] // the PC maker's, not Windows'
    [InlineData("Microsoft.Todos_8wekyb3d8bbwe", true)] // To Do: a default app even when not provisioned
    [InlineData("Microsoft.WindowsAlarms_8wekyb3d8bbwe", true)] // Clock
    [InlineData("Clipchamp.Clipchamp_yxz26nhyzhsrt", true)]
    [InlineData("Microsoft.Todos_abcdefghijklm", false)] // same name, someone else's publisher id
    [InlineData("Microsoft.PowerToys_8wekyb3d8bbwe", false)] // Microsoft, but someone chose it
    [InlineData(null, false)]
    public void Only_Microsoft_packages_Windows_provisioned_ship_with_it(string? family, bool expected) =>
        Assert.Equal(expected, WindowsApps.ShipsWithWindows(family, Provisioned));

    [Theory]
    [InlineData("Microsoft.XboxGamingOverlay_7.326.10141.0_neutral_~_8wekyb3d8bbwe", "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe")]
    [InlineData("Microsoft.WindowsTerminal_3001.24.12741.0_x64__8wekyb3d8bbwe", "Microsoft.WindowsTerminal_8wekyb3d8bbwe")]
    [InlineData("NoUnderscores", null)]
    [InlineData("_1_x64__8wekyb3d8bbwe", null)]
    public void Registry_key_names_map_to_families(string fullName, string? family) =>
        Assert.Equal(family, WindowsApps.FamilyOf(fullName));

    [Fact]
    public void Package_Windows_provisioned_is_left_out_of_the_scan()
    {
        var gameBar = new Installed
        {
            LocalId = @"MSIX\Microsoft.XboxGamingOverlay_7.326.10141.0_x64__8wekyb3d8bbwe",
            Name = "Game Bar",
            Publisher = "Microsoft Corporation",
            FamilyNames = ["Microsoft.XboxGamingOverlay_8wekyb3d8bbwe"],
            Msix = new MsixFacts("Store", false, true, "CN=Microsoft Corporation", "Microsoft Corporation"),
            ShipsWithWindows = true,
        };
        Assert.Empty(Resolver.Resolve([gameBar], new NoCatalog()));
    }

    [Theory]
    [InlineData("Microsoft Edge", "Microsoft.Edge")]
    [InlineData("Microsoft Edge WebView2 Runtime", "Microsoft.EdgeWebView2Runtime")]
    [InlineData("Microsoft OneDrive", "Microsoft.OneDrive")]
    [InlineData("Microsoft Windows Application Compatibility Fix Database", null)]
    public void Desktop_apps_Windows_installs_are_left_out(string name, string? id)
    {
        var item = new Installed
        {
            LocalId = $@"ARP\Machine\X64\{name}",
            Name = name,
            Publisher = "Microsoft Corporation",
            Correlated = id is null ? null : new CatalogRef(id, "winget"),
        };
        Assert.Empty(Resolver.Resolve([item], new NoCatalog()));
    }

    [Fact]
    public void Microsoft_apps_someone_chose_stay()
    {
        var code = new Installed
        {
            LocalId = @"ARP\User\X64\{771FD6B0-FA20-440A-A002-3B3BAC16DC50}_is1",
            Name = "Microsoft Visual Studio Code (User)",
            Publisher = "Microsoft Corporation",
            Correlated = new CatalogRef("Microsoft.VisualStudioCode", "winget"),
        };
        Assert.Single(Resolver.Resolve([code], new NoCatalog()));
    }
}

/// <summary>Helpers other apps install, from the dev PC's Apps list. They start unchecked.</summary>
public class HelperTests
{
    sealed class NoCatalog : ICatalog
    {
        public IReadOnlyList<CatalogEntry> Search(string name, string source) => [];
    }

    static App Resolve(string name, string publisher, string id) =>
        Assert.Single(Resolver.Resolve(
            [new Installed { LocalId = $@"ARP\Machine\X64\{name}", Name = name, Publisher = publisher, Correlated = new CatalogRef(id, "winget") }],
            new NoCatalog()));

    [Theory]
    [InlineData("NVIDIA PhysX", "NVIDIA Corporation", "Nvidia.PhysX")]
    [InlineData("OpenAL", "", "OpenAL.OpenAL")]
    [InlineData("Microsoft GameInput", "Microsoft Corporation", "Microsoft.GameInput")]
    [InlineData("MSI Center SDK", "MSI", "MSI.CenterSDK")]
    [InlineData("Futuremark SystemInfo", "Futuremark", "Futuremark.SystemInfo")]
    [InlineData("Epson Software Updater", "Seiko Epson Corporation", "Epson.SoftwareUpdater")]
    [InlineData("Epson Printer Connection Checker", "Seiko Epson Corporation", "Epson.PrinterConnectionChecker")]
    public void Catalog_helpers_are_system(string name, string publisher, string id) =>
        Assert.True(Resolve(name, publisher, id).System);

    [Theory]
    [InlineData("Corsair iCUE5 Software", "Corsair", "Corsair.iCUE.5")]
    [InlineData("Epson Scan 2", "Seiko Epson Corporation", "Epson.EpsonScan2")]
    [InlineData("Logitech G HUB", "Logitech", "Logitech.GHUB")]
    [InlineData("CPUID CPU-Z MSI 2.18", "CPUID, Inc.", "CPUID.CPU-Z.MSI")]
    [InlineData("MSI Center", "MICRO-STAR INTERNATIONAL CO., LTD", "9NVMNJCR03XV")]
    public void Hardware_apps_people_use_stay_apps(string name, string publisher, string id) =>
        Assert.Null(Resolve(name, publisher, id).System);
}

public class EvidenceTests
{
    [Fact]
    public void Passed_download_says_what_was_proven() =>
        Assert.Equal(
            "SHA-256 matches the winget catalog. Signed by Mozilla Corporation. From download.mozilla.org.",
            Rehome.Restore.Verification.Evidence(new Rehome.Restore.Checked("x", Rehome.Restore.Verdict.Trusted, null,
                Host: "download.mozilla.org", Sha256: "ab", SignedBy: "Mozilla Corporation")));

    [Fact]
    public void Store_app_names_who_checks_it() =>
        Assert.StartsWith("From the Microsoft Store.",
            Rehome.Restore.Verification.Evidence(new Rehome.Restore.Checked("x", Rehome.Restore.Verdict.Trusted, null)));
}

public class EvidencePunctuationTests
{
    [Fact]
    public void Signer_ending_in_a_period_gets_one_period() =>
        Assert.Contains("Signed by Docker Inc. From",
            Rehome.Restore.Verification.Evidence(new Rehome.Restore.Checked("x", Rehome.Restore.Verdict.Trusted, null,
                Host: "github.com", Sha256: "ab", SignedBy: "Docker Inc.")));
}
