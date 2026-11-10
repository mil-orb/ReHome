using Microsoft.Win32;

namespace Rehome;

/// <summary>
/// Apps that come with Windows: Game Bar, Get Help, Microsoft Store, Paint, Photos, Notepad, Edge. A fresh install
/// already has them, so they're left out of the scan and out of a restore.
///
/// The evidence is Windows' own list, never a name: Windows provisions its built-in packages for every new user and
/// records them under AppxAllUserStore, readable without admin rights. Only Microsoft-published ones count. A PC
/// maker's provisioned driver panel (Realtek Audio Control) is hardware software, which Categories handles.
/// </summary>
internal static class WindowsApps
{
    const string Store = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore";

    /// <summary>Publisher ids Microsoft signs Windows' own packages with.</summary>
    static readonly string[] MicrosoftPublisherIds = ["8wekyb3d8bbwe", "cw5n1h2txyewy"];

    /// <summary>
    /// Provisioned once someone installs them, but a fresh Windows doesn't have them. Without this, a PC where WSL
    /// was installed would drop it from the scan.
    /// </summary>
    static readonly HashSet<string> InstalledByChoice = new(StringComparer.OrdinalIgnoreCase)
    {
        "MicrosoftCorporationII.WindowsSubsystemForLinux_8wekyb3d8bbwe",
    };

    /// <summary>Desktop apps Windows itself installs and keeps updated. Matched by their exact winget id.</summary>
    static readonly HashSet<string> BuiltInDesktop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Edge",
        "Microsoft.EdgeWebView2Runtime",
        "Microsoft.OneDrive",
    };

    /// <summary>
    /// Windows' default apps by package name, for when the provisioned list misses one: someone removed To Do and
    /// later reinstalled it from the Store, or a PC was set up from an image that dropped it. Only counts under
    /// Microsoft's publisher ids, so a lookalike name from another publisher stays.
    /// </summary>
    static readonly HashSet<string> DefaultApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Todos", "Microsoft.WindowsAlarms", "Microsoft.WindowsCalculator", "Microsoft.WindowsCamera",
        "Microsoft.WindowsNotepad", "Microsoft.Paint", "Microsoft.Windows.Photos", "Microsoft.ScreenSketch",
        "Microsoft.WindowsSoundRecorder", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo", "Microsoft.BingNews",
        "Microsoft.BingWeather", "Microsoft.BingSearch", "Microsoft.GetHelp", "Microsoft.Getstarted",
        "Microsoft.MicrosoftStickyNotes", "Microsoft.XboxGamingOverlay", "Microsoft.GamingApp", "Microsoft.WindowsStore",
        "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.WindowsTerminal", "Microsoft.YourPhone",
        "Microsoft.WindowsFeedbackHub", "Microsoft.MicrosoftSolitaireCollection", "Microsoft.PowerAutomateDesktop",
        "Microsoft.OutlookForWindows", "Microsoft.Windows.DevHome", "MicrosoftCorporationII.QuickAssist",
        "MicrosoftCorporationII.MicrosoftFamily", "Microsoft.MicrosoftOfficeHub", "Microsoft.WindowsMaps", "Microsoft.People",
        "microsoft.windowscommunicationsapps", "Microsoft.SecHealthUI", "Microsoft.Xbox.TCUI", "Microsoft.XboxIdentityProvider",
        "Microsoft.XboxSpeechToTextOverlay", "Microsoft.MicrosoftEdge.Stable", "MSTeams", "Microsoft.Copilot",
        "MicrosoftWindows.CrossDevice", "MicrosoftWindows.Client.WebExperience",
    };

    /// <summary>Default apps from other publishers that Windows installs anyway.</summary>
    static readonly HashSet<string> DefaultFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Clipchamp.Clipchamp_yxz26nhyzhsrt",
    };

    static readonly Lazy<HashSet<string>> Provisioned = new(ReadProvisioned);

    /// <summary>True when this package family is one Windows put there itself.</summary>
    public static bool ShipsWithWindows(string? family) => ShipsWithWindows(family, Provisioned.Value);

    internal static bool ShipsWithWindows(string? family, IReadOnlySet<string> provisioned)
    {
        if (family is null || InstalledByChoice.Contains(family))
            return false;
        if (DefaultFamilies.Contains(family))
            return true;

        var cut = family.LastIndexOf('_');
        if (cut <= 0 || !MicrosoftPublisherIds.Contains(family[(cut + 1)..], StringComparer.OrdinalIgnoreCase))
            return false;
        return provisioned.Contains(family) || DefaultApps.Contains(family[..cut]);
    }

    /// <summary>A desktop install Windows owns: Edge, WebView2, OneDrive, or an unlisted "Microsoft Windows …" part.</summary>
    public static bool IsBuiltInDesktop(string name, CatalogRef? package) =>
        package is { Source: "winget" } p
            ? BuiltInDesktop.Contains(p.Id)
            : package is null && name.StartsWith("Microsoft Windows ", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Name_Version_Arch_Resource_PublisherId" → "Name_PublisherId". Null for anything else.</summary>
    public static string? FamilyOf(string fullName)
    {
        var parts = fullName.Split('_');
        return parts.Length == 5 && parts[0].Length > 0 && parts[4].Length > 0 ? $"{parts[0]}_{parts[4]}" : null;
    }

    static HashSet<string> ReadProvisioned()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var store = Registry.LocalMachine.OpenSubKey(Store);
            foreach (var list in new[] { "Applications", "InboxApplications" })
            {
                using var key = store?.OpenSubKey(list);
                foreach (var fullName in key?.GetSubKeyNames() ?? [])
                    if (FamilyOf(fullName) is { } family)
                        families.Add(family);
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Unreadable: nothing is treated as built in, so everything stays visible. Showing too much is the safe side.
        }
        return families;
    }
}
