using Rehome.Scan;

namespace Rehome.Tests;

public class ScanTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\App\app.exe"",0", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe,0", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\App\icon,v2.ico", @"C:\App\icon,v2.ico")]
    [InlineData("", null)]
    [InlineData(@"""unterminated", null)]
    public void Icon_paths_parse(string icon, string? expected) =>
        Assert.Equal(expected, Inventory.ParseIconPath(icon));

    [Fact]
    public void Signed_binary_reports_its_leaf_signer()
    {
        // dotnet.exe carries an embedded Microsoft signature on every supported install.
        var dotnet = Environment.ProcessPath is { } p && Path.GetFileName(p).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? p
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

        var signer = Authenticode.Read(dotnet);

        Assert.NotNull(signer);
        Assert.True(signer.Valid);
        Assert.Contains("O=Microsoft Corporation", signer.Subject);
        Assert.DoesNotContain("Root", signer.Subject);
    }

    [Fact]
    public void Unsigned_file_has_no_signer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rehome-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0]);
        try
        {
            Assert.Null(Authenticode.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_file_has_no_signer() =>
        Assert.Null(Authenticode.Read(@"C:\does\not\exist.exe"));

    [Fact]
    public void Non_msi_product_codes_are_ignored()
    {
        Assert.Null(Msi.CachedPackage("Git_is1"));
        Assert.Null(Msi.CachedPackage(null));
        Assert.Null(Msi.CachedPackage("{00000000-0000-0000-0000-000000000000}"));
    }
}
