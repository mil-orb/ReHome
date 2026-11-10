namespace Rehome.Tests;

public class ScanInExeTests
{
    static readonly byte[] Program = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();

    static Snapshot Scan() => new()
    {
        Machine = "OLD-PC",
        Taken = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero),
        Winget = "1.29.380",
        Apps = [new App { Name = "Git", Status = Status.Ready, Package = new CatalogRef("Git.Git", "winget"), LocalId = @"ARP\Machine\X64\Git_is1" }],
    };

    static byte[] Export(byte[] exe, byte[] scan)
    {
        using var from = new MemoryStream(exe);
        using var to = new MemoryStream();
        ScanInExe.Write(from, ScanInExe.Read(from).Program, scan, to);
        return to.ToArray();
    }

    [Fact]
    public void A_plain_exe_has_no_scan()
    {
        var (program, scan) = ScanInExe.Read(new MemoryStream(Program));
        Assert.Equal(Program.Length, program);
        Assert.Null(scan);
    }

    [Fact]
    public void Exported_exe_keeps_the_program_bytes_and_carries_the_scan()
    {
        var sealedScan = ScanSeal.Seal(Scan());
        var exe = Export(Program, sealedScan);

        Assert.Equal(Program, exe[..Program.Length]);
        var (program, scan) = ScanInExe.Read(new MemoryStream(exe));
        Assert.Equal(Program.Length, program);
        Assert.Equal(sealedScan, scan);
        Assert.Equal("Git.Git", Rehome.Restore.Plan.Parse(scan!).Apps[0].Package!.Id);
    }

    [Fact]
    public void Exporting_from_an_exported_exe_replaces_the_scan()
    {
        var first = Export(Program, ScanSeal.Seal(Scan()));
        var second = Export(first, ScanSeal.Seal(Scan() with { Machine = "OTHER-PC" }));

        var (program, scan) = ScanInExe.Read(new MemoryStream(second));
        Assert.Equal(Program.Length, program);
        Assert.Equal("OTHER-PC", Rehome.Restore.Plan.Parse(scan!).Machine);
    }

    [Fact]
    public void A_changed_byte_in_the_embedded_scan_is_refused()
    {
        var exe = Export(Program, ScanSeal.Seal(Scan()));
        var at = Program.Length + System.Text.Encoding.UTF8.GetString(exe[Program.Length..]).IndexOf("Git.Git", StringComparison.Ordinal);
        exe[at] = (byte)'J';
        var (_, scan) = ScanInExe.Read(new MemoryStream(exe));
        Assert.Throws<RehomeException>(() => Rehome.Restore.Plan.Parse(scan!));
    }

    [Fact]
    public void A_length_that_points_outside_the_file_is_refused()
    {
        var exe = Export(Program, ScanSeal.Seal(Scan()));
        BitConverter.GetBytes(long.MaxValue).CopyTo(exe, exe.Length - 16);
        Assert.Throws<RehomeException>(() => ScanInExe.Read(new MemoryStream(exe)));
    }
}
