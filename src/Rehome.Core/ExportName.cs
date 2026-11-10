using System.Text.RegularExpressions;

namespace Rehome;

/// <summary>
/// The exported exe is named after what it installs, so a USB drive with a few of them stays readable:
/// "Rehome 7-Zip Brave VLC Installer.exe", or "Rehome 7-Zip Brave Discord +47 more Installer.exe" for a long list.
/// </summary>
internal static partial class ExportName
{
    const int MaxList = 60;

    public static string For(IEnumerable<App> installing)
    {
        var names = installing
            .Select(a => Short(a.Name))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var shown = new List<string>();
        var length = 0;
        foreach (var name in names)
        {
            var rest = names.Count - shown.Count - 1;
            // Room for this name, plus " +N more" if anything is left after it.
            var need = length + (shown.Count > 0 ? 1 : 0) + name.Length + (rest > 0 ? $" +{rest} more".Length : 0);
            if (need > MaxList && shown.Count > 0)
                break;
            shown.Add(name);
            length += (shown.Count > 1 ? 1 : 0) + name.Length;
        }

        var more = names.Count - shown.Count;
        var list = string.Join(' ', shown) + (more > 0 ? $" +{more} more" : "");
        return list.Length > 0 ? $"Rehome {list} Installer.exe" : "Rehome Installer.exe";
    }

    /// <summary>"7-Zip 19.00 (x64)" → "7-Zip", "Mullvad VPN 2025.14.0" → "Mullvad VPN". Safe in a file name.</summary>
    internal static string Short(string name)
    {
        var s = Parenthetical().Replace(name, " ");
        s = TrailingVersion().Replace(s, "");
        s = string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        s = Spaces().Replace(s, " ").Trim(' ', '.', '-');
        return s.Length > 24 ? s[..24].TrimEnd(' ', '.', '-') : s;
    }

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex Parenthetical();

    // Version-looking words at the end: 19.00, v2.3, 2025.14.0, 1.2.3-beta, x64.
    [GeneratedRegex(@"(\s+(v?\d[\w.+-]*|x64|x86|arm64|64-bit|32-bit))+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVersion();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
