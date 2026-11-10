using System.Text;

namespace Rehome.Tests;

public class ScanSealTests
{
    static Snapshot Scan() => new()
    {
        Machine = "OLD-PC",
        Taken = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero),
        Winget = "1.29.380",
        Apps =
        [
            new App { Name = "Git", Status = Status.Ready, Package = new CatalogRef("Git.Git", "winget"), LocalId = @"ARP\Machine\X64\Git_is1" },
            new App { Name = "VLC media player", Status = Status.Ready, Package = new CatalogRef("VideoLAN.VLC", "winget"), LocalId = @"ARP\Machine\X64\VLC" },
        ],
    };

    static string? CheckOf(byte[] bytes) => Scanner.FromJson(Encoding.UTF8.GetString(bytes)).Check;

    static string Write(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rehome-test-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Sealed_scan_matches_and_reads_back()
    {
        var bytes = ScanSeal.Seal(Scan());
        Assert.True(ScanSeal.Matches(bytes, CheckOf(bytes)));
        Assert.Equal(2, Scanner.FromJson(Encoding.UTF8.GetString(bytes)).Apps.Count);
    }

    [Fact]
    public void Swapping_a_package_id_breaks_the_seal()
    {
        var text = Encoding.UTF8.GetString(ScanSeal.Seal(Scan())).Replace("VideoLAN.VLC", "VideoLAN.VLX");
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.False(ScanSeal.Matches(bytes, CheckOf(bytes)));
    }

    [Fact]
    public void Changing_one_byte_breaks_the_seal()
    {
        var bytes = ScanSeal.Seal(Scan());
        var check = CheckOf(bytes);
        bytes[Encoding.UTF8.GetString(bytes).IndexOf("OLD-PC", StringComparison.Ordinal)] = (byte)'X';
        Assert.False(ScanSeal.Matches(bytes, check));
    }

    [Fact]
    public void Missing_or_malformed_check_never_matches()
    {
        var plain = Encoding.UTF8.GetBytes(Scanner.ToJson(Scan()));
        Assert.False(ScanSeal.Matches(plain, null));
        Assert.False(ScanSeal.Matches(plain, "not-hex"));
    }

    [Fact]
    public void Scan_that_already_contains_the_placeholder_is_never_sealed() =>
        Assert.Throws<InvalidOperationException>(() => ScanSeal.Seal(Scan() with { Machine = new string('0', 64) }));

    [Fact]
    public void Plan_load_opens_a_sealed_scan()
    {
        var path = Write(ScanSeal.Seal(Scan()));
        try { Assert.Equal(2, Rehome.Restore.Plan.Load(path).Apps.Count); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Plan_load_refuses_a_changed_scan()
    {
        var text = Encoding.UTF8.GetString(ScanSeal.Seal(Scan())).Replace("VideoLAN.VLC", "VideoLAN.VLX");
        var path = Write(Encoding.UTF8.GetBytes(text));
        try
        {
            var e = Assert.Throws<RehomeException>(() => Rehome.Restore.Plan.Load(path));
            Assert.Contains("changed or damaged", e.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Plan_load_still_opens_scans_written_before_the_seal()
    {
        var path = Write(Encoding.UTF8.GetBytes(Scanner.ToJson(Scan())));
        try { Assert.Equal(2, Rehome.Restore.Plan.Load(path).Apps.Count); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_byte_order_mark_added_in_front_is_ignored()
    {
        // Some copy tools and editors add one without touching anything else.
        var bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(ScanSeal.Seal(Scan())).ToArray();
        var path = Write(bom);
        try { Assert.Equal(2, Rehome.Restore.Plan.Load(path).Apps.Count); }
        finally { File.Delete(path); }
    }
}
