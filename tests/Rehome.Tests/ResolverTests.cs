namespace Rehome.Tests;

public class ResolverTests
{
    sealed class StubCatalog(params CatalogEntry[] entries) : ICatalog
    {
        public List<(string Name, string Source)> Searches { get; } = [];

        public IReadOnlyList<CatalogEntry> Search(string name, string source)
        {
            lock (Searches)
                Searches.Add((name, source));

            return [.. entries.Where(e => e.Source == source && e.Name.Contains(name, StringComparison.OrdinalIgnoreCase))];
        }
    }

    static Installed Desktop(string name, string? publisher = null, CatalogRef? correlated = null, string? key = null) => new()
    {
        LocalId = $@"ARP\Machine\X64\{key ?? name}",
        Name = name,
        Publisher = publisher,
        Correlated = correlated,
    };

    static Installed Msix(string name, string family, string kind = "Developer", bool visible = true, bool framework = false, string? publisher = null) => new()
    {
        LocalId = $@"MSIX\{family}",
        Name = name,
        Publisher = publisher,
        FamilyNames = [family],
        Msix = new MsixFacts(kind, framework, visible, "CN=Test", publisher ?? "Test"),
    };

    static App Single(Installed item, ICatalog? catalog = null) =>
        Assert.Single(Resolver.Resolve([item], catalog ?? new StubCatalog()));

    [Fact]
    public void Correlated_install_is_ready_with_exact_id()
    {
        var app = Single(Desktop("Git", "The Git Development Community", new CatalogRef("Git.Git", "winget")));

        Assert.Equal(Status.Ready, app.Status);
        Assert.Equal(new CatalogRef("Git.Git", "winget"), app.Package);
    }

    [Fact]
    public void Sparse_shell_extension_folds_into_its_desktop_app()
    {
        // The real case from the first scan: Notepad++ registers an invisible MSIX for the Windows 11 context menu.
        var desktop = Desktop("Notepad++ (64-bit x64)", "Notepad++ Team", new CatalogRef("Notepad++.Notepad++", "winget"));
        var sparse = Msix("Notepad++", "NotepadPlusPlus_7njy0v32s6xk6", visible: false);

        var apps = Resolver.Resolve([desktop, sparse], new StubCatalog());

        var companion = Assert.Single(apps, a => a.Status == Status.Companion);
        Assert.Equal("Notepad++ (64-bit x64)", companion.Parent);
        Assert.Single(apps, a => a.Status == Status.Ready);
    }

    [Theory]
    [InlineData("System", false)]
    [InlineData("None", false)]
    [InlineData("Store", true)]
    public void Operating_system_packages_and_frameworks_are_left_out(string kind, bool framework)
    {
        var apps = Resolver.Resolve([Msix("Thing", "Thing_abc", kind, framework: framework)], new StubCatalog());

        Assert.Empty(apps);
    }

    [Fact]
    public void Same_name_and_publisher_is_a_guess_never_ready()
    {
        var catalog = new StubCatalog(new CatalogEntry("Brave.Brave", "Brave", "Brave Software, Inc.", "winget"));

        var app = Single(Desktop("Brave", "Brave Software Inc"), catalog);

        Assert.Equal(Status.Guess, app.Status);
        Assert.Equal("Brave.Brave", app.Package?.Id);
    }

    [Fact]
    public void Same_name_from_a_different_publisher_is_not_matched()
    {
        // A lookalike package must never stand in for the real one.
        var catalog = new StubCatalog(new CatalogEntry("Evil.Brave", "Brave", "Someone Else", "winget"));

        Assert.Equal(Status.Manual, Single(Desktop("Brave", "Brave Software Inc"), catalog).Status);
    }

    [Fact]
    public void Two_equally_good_matches_means_manual()
    {
        var catalog = new StubCatalog(
            new CatalogEntry("A.Tool", "Tool", "Acme", "winget"),
            new CatalogEntry("B.Tool", "Tool", "Acme Inc", "winget"));

        Assert.Equal(Status.Manual, Single(Desktop("Tool", "Acme"), catalog).Status);
    }

    [Fact]
    public void Beta_channel_does_not_compete_with_stable()
    {
        var catalog = new StubCatalog(
            new CatalogEntry("MullvadVPN.MullvadVPN", "Mullvad VPN", "Mullvad VPN AB", "winget"),
            new CatalogEntry("MullvadVPN.MullvadVPN.beta", "Mullvad VPN (Beta)", "Mullvad VPN AB", "winget"));

        var app = Single(Desktop("Mullvad VPN 2025.14.0", "Mullvad VPN"), catalog);

        Assert.Equal("MullvadVPN.MullvadVPN", app.Package?.Id);
    }

    [Fact]
    public void Missing_publisher_never_guesses()
    {
        var catalog = new StubCatalog(new CatalogEntry("Oven-sh.Bun", "Bun", "Oven", "winget"));

        Assert.Equal(Status.Manual, Single(Desktop("Bun"), catalog).Status);
    }

    [Fact]
    public void Store_signed_packages_are_looked_up_in_the_store_only()
    {
        var catalog = new StubCatalog(
            new CatalogEntry("9WZDNCRFHVN5", "Windows Calculator", null, "msstore"),
            new CatalogEntry("Fake.Calculator", "Windows Calculator", "Microsoft Corporation", "winget"));

        var app = Single(Msix("Windows Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe", "Store", publisher: "Microsoft Corporation"), catalog);

        Assert.Equal(new CatalogRef("9WZDNCRFHVN5", "msstore"), app.Package);
        Assert.All(catalog.Searches, s => Assert.Equal("msstore", s.Source));
    }

    [Fact]
    public void Store_hit_with_the_same_package_family_is_ready()
    {
        var family = "Microsoft.WindowsCalculator_8wekyb3d8bbwe";
        var catalog = new StubCatalog(new CatalogEntry("9WZDNCRFHVN5", "Windows Calculator", null, "msstore", [family]));

        var app = Single(Msix("Windows Calculator", family, "Store", publisher: "Microsoft Corporation"), catalog);

        Assert.Equal(Status.Ready, app.Status);
    }

    [Fact]
    public void Store_hit_with_a_different_package_family_stays_a_guess()
    {
        var catalog = new StubCatalog(new CatalogEntry("9XXXXXXXXXXX", "Windows Calculator", null, "msstore", ["Someone.Calculator_abc123"]));

        var app = Single(Msix("Windows Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe", "Store", publisher: "Microsoft Corporation"), catalog);

        Assert.Equal(Status.Guess, app.Status);
    }

    [Fact]
    public void Store_listing_with_another_name_but_the_same_family_is_ready()
    {
        // StubCatalog matches on substring, the real Store on relevance. Either way the family decides.
        var family = "Microsoft.Todos_8wekyb3d8bbwe";
        var catalog = new StubCatalog(new CatalogEntry("9NBLGGH5R558", "Microsoft To Do: Lists, Tasks & Reminders", null, "msstore", [family]));

        var app = Single(Msix("Microsoft To Do", family, "Store", publisher: "Microsoft Corporation"), catalog);

        Assert.Equal(Status.Ready, app.Status);
        Assert.Equal("9NBLGGH5R558", app.Package?.Id);
        Assert.Null(app.System);
    }

    [Fact]
    public void Steam_games_belong_to_steam()
    {
        var app = Single(Desktop("Wallpaper Engine", "Wallpaper Engine Team", key: "Steam App 431960"));

        Assert.Equal(Status.Launcher, app.Status);
        Assert.Equal("Steam", app.Parent);
    }

    [Fact]
    public void Output_order_is_stable()
    {
        Installed[] items =
        [
            Desktop("zeta", correlated: new CatalogRef("Z.Z", "winget")),
            Desktop("Alpha"),
            Desktop("alpha", correlated: new CatalogRef("A.A", "winget")),
        ];

        var first = Resolver.Resolve(items, new StubCatalog()).Select(a => a.LocalId);
        var second = Resolver.Resolve([.. items.Reverse()], new StubCatalog()).Select(a => a.LocalId);

        Assert.Equal(first, second);
        Assert.Equal(Status.Ready, Resolver.Resolve(items, new StubCatalog())[0].Status);
    }

    [Theory]
    [InlineData("Notepad++ (64-bit x64)", "notepadplusplus")]
    [InlineData("7-Zip 24.09 (x64)", "7zip")]
    [InlineData("Mullvad VPN 2025.14.0", "mullvadvpn")]
    [InlineData("Go Programming Language amd64 go1.27.0", "goprogramminglanguagego")]
    [InlineData("Python 3.13.1 (64-bit)", "python")]
    [InlineData("", "")]
    public void Names_normalize(string raw, string expected) =>
        Assert.Equal(expected, Resolver.NormalizeName(raw));

    [Theory]
    [InlineData("Microsoft Corporation", "microsoft")]
    [InlineData("Brave Software, Inc.", "brave")]
    [InlineData("The Git Development Community", "git")]
    [InlineData(null, "")]
    public void Publishers_normalize(string? raw, string expected) =>
        Assert.Equal(expected, Resolver.NormalizePublisher(raw));
}

/// <summary>The real Manual list from the first full scan of the dev PC, sorted by hand.</summary>
public class CategoryTests
{
    static App Resolve(string name, string? publisher, bool msix = false, CatalogRef? correlated = null) =>
        Assert.Single(Resolver.Resolve([msix
            ? new Installed
            {
                LocalId = $@"MSIX\{name.Replace(' ', '.')}_1.0.0.0_x64__8wekyb3d8bbwe",
                Name = name,
                Publisher = publisher,
                FamilyNames = [$"{name.Replace(' ', '.')}_8wekyb3d8bbwe"],
                Msix = new MsixFacts("Store", false, true, "CN=Microsoft Corporation", publisher ?? ""),
            }
            : new Installed { LocalId = $@"ARP\Machine\X64\{name}", Name = name, Publisher = publisher, Correlated = correlated }], new NoCatalog()));

    sealed class NoCatalog : ICatalog
    {
        public IReadOnlyList<CatalogEntry> Search(string name, string source) => [];
    }

    [Theory]
    [InlineData("AMD Chipset Software", "Advanced Micro Devices, Inc.")]
    [InlineData("AMD Install Manager", "Advanced Micro Devices, Inc.")]
    [InlineData("AMD Ryzen Master SDK", "Advanced Micro Devices, Inc.")]
    [InlineData("AMD Software", "Advanced Micro Devices, Inc.")]
    [InlineData("Corsair iCUE 5 VHID Drivers v3.0.88.9", "Corsair Memory")]
    [InlineData("Corsair USBXp Drivers v3.3.0.15 for iCUE 5", "Corsair Memory")]
    [InlineData("Elytra Anti-Cheat Framework", "Elytra")]
    [InlineData("EPSON XP-4200 Series Printer Uninstall", "Seiko Epson Corporation")]
    [InlineData("Intel(R) Network Connections 26.2.0.1", "Intel")]
    [InlineData("Intel(R) Wireless Bluetooth(R)", "Intel Corporation")]
    [InlineData("Microsoft Teams Meeting Add-in for Microsoft Office", "Microsoft")]
    [InlineData("Microsoft Visual C++ 2005 Redistributable (x64)", "Microsoft Corporation")]
    [InlineData("Microsoft Visual Studio Installer", "Microsoft Corporation")]
    [InlineData("Realtek Audio Driver", "Realtek Semiconductor Corp.")]
    [InlineData("vs_CoreEditorFonts", "Microsoft Corporation")]
    [InlineData("Windows SDK AddOn", "Microsoft Corporation")]
    [InlineData("Xbox Service", "Microsoft Corporation")]
    public void Drivers_and_runtimes_are_system(string name, string? publisher) =>
        Assert.True(Resolve(name, publisher).System);

    [Theory]
    [InlineData("Game Bar")]
    [InlineData("Get Help")]
    [InlineData("Windows Security")]
    [InlineData("Phone Link")]
    // Provisioned ones (Game Bar, Get Help) never get here: Inventory flags them and the resolver drops them.
    // This is the fallback for a Microsoft package Windows didn't provision and no catalog knows.
    public void Unprovisioned_Microsoft_packages_no_catalog_knows_are_system(string name) =>
        Assert.True(Resolve(name, "Microsoft Corporation", msix: true).System);

    [Theory]
    [InlineData("RustDesk", "Purslane Tech Pte. Ltd.")]
    [InlineData("VMware Workstation", "VMware, Inc.")]
    [InlineData("Razer Cortex", "Razer Inc.")]
    [InlineData("Razer Chroma", "Razer Inc.")]
    [InlineData("Razer Synapse", "Razer Inc.")]
    [InlineData("Mister Download Manager", "Ahmed Nefzaoui")]
    [InlineData("Electric Sheep 3.0.2", "Electricsheep")]
    [InlineData("FreeToken Desktop", "freetoken")]
    [InlineData("Bun", null)]
    public void Apps_without_a_catalog_entry_stay_apps(string name, string? publisher)
    {
        var app = Resolve(name, publisher);
        Assert.Equal(Status.Manual, app.Status);
        Assert.Null(app.System);
    }

    [Fact]
    public void Catalog_runtime_is_installable_but_system() // shows unchecked in its own group
    {
        var app = Resolve("Microsoft .NET Runtime - 8.0.11 (x64)", "Microsoft Corporation", correlated: new CatalogRef("Microsoft.DotNet.Runtime.8", "winget"));
        Assert.Equal(Status.Ready, app.Status);
        Assert.True(app.System);
    }

    [Fact]
    public void Microsoft_app_in_a_catalog_is_an_app() =>
        Assert.Null(Resolve("Microsoft Visual Studio Code", "Microsoft Corporation", correlated: new CatalogRef("Microsoft.VisualStudioCode", "winget")).System);
}

public class CategoryEdgeTests
{
    sealed class NoCatalog : ICatalog
    {
        public IReadOnlyList<CatalogEntry> Search(string name, string source) => [];
    }

    [Theory]
    [InlineData("Rustup: the Rust toolchain installer", "Rustlang", "Rustlang.Rustup")]
    [InlineData("Microsoft .NET SDK 10.0.300 (x64)", "Microsoft Corporation", "Microsoft.DotNet.SDK.10")]
    public void Tools_in_a_catalog_stay_apps_even_with_part_words(string name, string publisher, string id)
    {
        var item = new Installed { LocalId = $@"ARP\Machine\X64\{name}", Name = name, Publisher = publisher, Correlated = new CatalogRef(id, "winget") };
        Assert.Null(Assert.Single(Resolver.Resolve([item], new NoCatalog())).System);
    }
}
