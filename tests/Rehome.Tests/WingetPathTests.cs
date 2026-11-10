using Rehome.Restore;

namespace Rehome.Tests;

/// <summary>
/// The elevated helper starts whatever <see cref="WingetCli.Path"/> returns. It must be App Installer's own
/// SYSTEM-owned package folder, never the alias in %LOCALAPPDATA%, which the user can replace.
/// </summary>
public sealed class WingetPathTests
{
    [Fact]
    public void ResolvesInsideTheProtectedPackageFolder()
    {
        var exe = WingetCli.Path();
        // No App Installer on this machine (some CI images): nothing to resolve, and null is the safe answer.
        if (exe is null) return;

        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        Assert.StartsWith(windowsApps + Path.DirectorySeparatorChar, exe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.DesktopAppInstaller_", exe, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"\winget.exe", exe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NeverTheUserWritableAlias()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        Assert.False(WingetCli.Path()?.StartsWith(alias, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
