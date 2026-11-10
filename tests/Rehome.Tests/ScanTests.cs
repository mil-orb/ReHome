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

        var signer = Authenticode.Read(dotnet, "exe");

        Assert.NotNull(signer);
        Assert.True(signer.Valid);
        Assert.Equal("Microsoft Corporation", signer.Organization);
        Assert.Equal("exe", signer.From);
        Assert.DoesNotContain("Root", signer.Subject);
    }

    [Fact]
    public void Unsigned_file_has_no_signer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rehome-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0]);
        try
        {
            Assert.Null(Authenticode.Read(path, "exe"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_file_has_no_signer() =>
        Assert.Null(Authenticode.Read(@"C:\does\not\exist.exe", "exe"));

    [Fact]
    public void EV_subject_parses_to_organization()
    {
        // Real subject from Core Temp: EV certificates lead with jurisdiction OIDs.
        var s = Signer.Parse("OID.1.3.6.1.4.1.311.60.2.1.3=IL, OID.2.5.4.15=Business Entity, CN=ALCPU, SERIALNUMBER=307609677, O=ALCPU, L=Ramat Gan, C=IL", "exe", true);

        Assert.Equal("ALCPU", s.Organization);
        Assert.Equal("ALCPU", s.Name);
    }

    [Fact]
    public void Quoted_subject_parses()
    {
        var s = Signer.Parse("CN=\"Notepad++\", O=\"Notepad++\", L=Saint Cloud, C=FR", "msix", true);

        Assert.Equal("Notepad++", s.Organization);
    }

    [Fact]
    public void Garbage_subject_keeps_raw_text()
    {
        var s = Signer.Parse("not a distinguished name", "exe", false);

        Assert.Null(s.Organization);
        Assert.Equal("not a distinguished name", s.Subject);
    }

    [Fact]
    public void Non_msi_product_codes_are_ignored()
    {
        Assert.Null(Msi.CachedPackage("Git_is1"));
        Assert.Null(Msi.CachedPackage(null));
        Assert.Null(Msi.CachedPackage("{00000000-0000-0000-0000-000000000000}"));
    }
}
