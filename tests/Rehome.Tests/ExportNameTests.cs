namespace Rehome.Tests;

public class ExportNameTests
{
    static App A(string name) => new() { Name = name, Status = Status.Ready, LocalId = name };

    [Theory]
    [InlineData("7-Zip 19.00 (x64)", "7-Zip")]
    [InlineData("Mullvad VPN 2025.14.0", "Mullvad VPN")]
    [InlineData("Eclipse Temurin JDK with Hotspot 25.0.3+9", "Eclipse Temurin JDK with")]
    [InlineData("VLC media player", "VLC media player")]
    [InlineData("Python 3.12.4 (64-bit)", "Python")]
    [InlineData("Notepad++ v8.6", "Notepad++")]
    [InlineData("AC/DC: Tool?", "ACDC Tool")]
    public void Short_names_drop_versions_and_unsafe_characters(string name, string expected) =>
        Assert.Equal(expected, ExportName.Short(name));

    [Fact]
    public void A_short_list_names_every_app_in_order() =>
        Assert.Equal("Rehome 7-Zip Brave VLC media player Installer.exe",
            ExportName.For([A("VLC media player"), A("Brave"), A("7-Zip 19.00 (x64)")]));

    [Fact]
    public void A_long_list_names_the_first_few_and_counts_the_rest()
    {
        var apps = Enumerable.Range(0, 50).Select(i => A($"App {(char)('A' + i / 26)}{(char)('A' + i % 26)}")).ToList();
        var name = ExportName.For(apps);
        Assert.StartsWith("Rehome App AA App AB", name);
        Assert.Matches(@" \+\d+ more Installer\.exe$", name);
        Assert.True(name.Length <= 6 + 1 + 60 + " Installer.exe".Length, name);
    }

    [Fact]
    public void Duplicates_collapse_and_nothing_still_has_a_name()
    {
        Assert.Equal("Rehome Git Installer.exe", ExportName.For([A("Git 2.45"), A("Git 2.46")]));
        Assert.Equal("Rehome Installer.exe", ExportName.For([]));
    }
}
