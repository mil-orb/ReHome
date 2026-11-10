using System.Text.RegularExpressions;

namespace Rehome;

/// <summary>
/// Sorts drivers, runtimes and parts of Windows away from the apps a person chose. A new PC gets these from Windows
/// Update, the PC maker or the app that depends on them, so they start unchecked.
/// Biased toward showing: a driver shown as an app is one box to untick, a real app hidden is lost. Every rule needs
/// a strong signal, and publisher alone is never enough except for chip and driver makers with no catalog entry.
/// </summary>
internal static partial class Categories
{
    public static bool IsSystem(Installed item, CatalogRef? package) =>
        IsSystem(item.Name, item.Publisher, item.IsMsix, package is not null);

    /// <summary>Same rules from a saved entry, so a scan opened on the new PC is sorted by the current rules.</summary>
    public static bool IsSystem(App app) =>
        IsSystem(app.Name, app.Publisher, app.LocalId.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase), app.Package is not null);

    static bool IsSystem(string name, string? publisherName, bool isMsix, bool inCatalog)
    {
        if (SystemWords().IsMatch(name))
            return true;

        var publisher = Resolver.NormalizePublisher(publisherName);
        var part = PartWords().IsMatch(name) || name.StartsWith("vs_", StringComparison.OrdinalIgnoreCase);

        // A hardware maker's SDK, service or installer is plumbing for its control panel, catalog or not
        // ("MSI Center SDK", "AMD Install Manager").
        if (part && HardwareMakers.Contains(publisher))
            return true;

        // Weaker words. "Rustup: the Rust toolchain installer" and ".NET SDK" are tools someone chose, and the
        // catalog knows them. Without a catalog entry, "Installer", "SDK" and "Service" mean a part of something else.
        if (inCatalog)
            return false;
        if (part)
            return true;

        // A Microsoft package no catalog knows is part of Windows: Xbox Service, Windows SDK AddOn.
        if (isMsix && publisher == "microsoft")
            return true;

        // Chip and peripheral makers' own software without a catalog entry is a driver or a driver's control panel.
        return HardwareMakers.Contains(publisher);
    }

    static readonly HashSet<string> HardwareMakers =
    [
        "advanced", "amd", "intel", "realtek", "nvidia", "synaptics", "qualcomm", "mediatek", "seiko", "epson", "elan", "conexant",
        "msi", "micro", "asus", "asustek", "gigabyte", "asrock",
    ];

    // Runtimes go by product name as often as by the word: PhysX, OpenAL, GameInput, DirectX.
    // "Updater" and "SystemInfo" are helpers another app installed (Epson Software Updater, Futuremark SystemInfo).
    [GeneratedRegex(
        @"\b(drivers?|chipset|redistributables?|runtimes?|firmware|printer|add-?ins?|fonts?|compatibility|anti-?cheat|" +
        @"bluetooth|network connections|audio control|webview2|vhid|usbxp|physx|openal|gameinput|directx|updater|systeminfo|" +
        @"connection checker)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SystemWords();

    [GeneratedRegex(@"\b(sdk|add-?ons?|uninstall|services?|install(ation)? manager|installer|frameworks?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PartWords();
}
