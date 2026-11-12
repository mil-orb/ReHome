using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Rehome.Shell;

/// <summary>
/// Rehome ships as one exe. winget's API needs Microsoft.Management.Deployment.winmd and its small native factory,
/// Microsoft.Management.Deployment.dll, in the same folder as the running exe (for an app that isn't packaged,
/// that's the only place Windows looks). So when they aren't there Rehome copies its own program bytes and the two
/// files it carries into %LOCALAPPDATA%\Rehome\App\&lt;hash&gt; and restarts from there. Nothing is written next to
/// the exe the person started, so a USB drive stays one file.
/// </summary>
static class Portable
{
    const string OriginFlag = "--origin";
    const string Winmd = "Microsoft.Management.Deployment.winmd";
    static readonly string[] Carried = [Winmd, "Microsoft.Management.Deployment.dll"];

    static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rehome", "App");

    /// <summary>The exe the person started. Differs from the running exe after a restart from the cache.</summary>
    public static string Origin { get; private set; } = Environment.ProcessPath ?? "";

    static bool Relocated => !string.Equals(Origin, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>The published one-file exe. A developer build is an exe beside loose DLLs and runs where it is.</summary>
    static bool Bundled => !File.Exists(Path.Combine(AppContext.BaseDirectory, "ReHome.dll"));

    static bool InCache(string exe) => Path.GetFullPath(exe).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the cached copy was started and this process should exit.</summary>
    public static bool Restart(ref string[] args)
    {
        var at = Array.IndexOf(args, OriginFlag);
        var exe = Environment.ProcessPath;
        if (at >= 0 && at == args.Length - 2)
        {
            var origin = args[at + 1];
            args = args[..at];
            // Only the cached copy is started with --origin. Anywhere else, the flag is ignored.
            if (exe is not null && InCache(exe))
            {
                Origin = Path.GetFullPath(origin);
                return false;
            }
        }

        // A developer build has the winmd beside it already. The published exe always moves to the cache, even if a
        // winmd sits next to it: that file could have been put there to keep ReHome running from a folder anyone can
        // write to (Downloads, a FAT-formatted USB drive), and the elevated helper starts from the running exe's folder.
        if (exe is null || (!Bundled && File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, Winmd))))
            return false;

        try
        {
            var target = Prepare(exe);
            var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
            foreach (var a in args)
                start.ArgumentList.Add(a);
            start.ArgumentList.Add(OriginFlag);
            start.ArgumentList.Add(exe);
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or CryptographicException)
        {
            // Carry on from here. Scanning and installing will say winget isn't responding, which the log explains.
            Log.Write(e);
            return false;
        }
    }

    /// <summary>
    /// Before an elevated helper starts from the cache, check the exe there is still the one this process was started
    /// from. A running exe can't be overwritten, but it can be renamed and another file put in its place.
    /// </summary>
    public static void CheckBeforeElevating()
    {
        if (Environment.ProcessPath is not { } exe)
            return;
        // The published exe only elevates from its cache copy, a folder only this user can write to.
        if (Bundled && !(Relocated && InCache(exe)))
            throw new RehomeException("ReHome couldn't set up its copy in your app data folder, so it won't ask for admin rights. Close ReHome, check the drive has about 100 MB free, and start it again. Nothing was installed.");
        if (!Relocated)
            return;
        var expected = Path.GetFileName(Path.GetDirectoryName(exe));
        using var file = File.OpenRead(exe);
        if (!string.Equals(Key(file, file.Length), expected, StringComparison.Ordinal))
            throw new RehomeException("ReHome's copy in your app data folder changed while it was open. Close ReHome and start it again. Nothing was installed.");
    }

    /// <summary>The cached exe for these program bytes, written if it's missing or doesn't match.</summary>
    static string Prepare(string exe)
    {
        string key;
        long program;
        using (var source = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            // A damaged trailer still restarts; Boot reports the damage in the window.
            try { program = ScanInExe.Read(source).Program; }
            catch (RehomeException) { program = source.Length; }
            key = Key(source, program);
        }

        var dir = Path.Combine(Root, key);
        var target = Path.Combine(dir, "ReHome.exe");
        Directory.CreateDirectory(Root);
        Sweep(keep: key);

        if (Matches(target, key) && Carried.All(name => File.Exists(Path.Combine(dir, name))))
            return target;

        var staging = Path.Combine(Root, $"{key}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(staging);
        try
        {
            using (var source = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var copy = new FileStream(Path.Combine(staging, "ReHome.exe"), FileMode.CreateNew))
                ScanInExe.CopyExactly(source, copy, program);
            foreach (var name in Carried)
            {
                using var carried = typeof(Portable).Assembly.GetManifestResourceStream(name)
                    ?? throw new IOException($"{name} isn't inside this build.");
                using var copy = new FileStream(Path.Combine(staging, name), FileMode.CreateNew);
                carried.CopyTo(copy);
            }

            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            Directory.Move(staging, dir);
        }
        catch (IOException) when (Matches(target, key))
        {
            // Another Rehome window got there first with the same bytes.
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }

        if (!Matches(target, key))
            throw new IOException("The copy in the app data folder doesn't match the exe it came from.");
        return target;
    }

    static bool Matches(string path, string key)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Key(file, file.Length) == key;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>SHA-256 of the first <paramref name="count"/> bytes, as 32 hex characters.</summary>
    static string Key(Stream file, long count)
    {
        file.Seek(0, SeekOrigin.Begin);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        while (count > 0)
        {
            var n = file.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (n == 0)
                throw new EndOfStreamException();
            hash.AppendData(buffer, 0, n);
            count -= n;
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..32];
    }

    /// <summary>Old versions go, so the cache holds one copy. One that's still running stays until next time.</summary>
    static void Sweep(string keep)
    {
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(dir);
            if (name == keep)
                continue;
            try
            {
                // Another window may be copying into this right now.
                if (name.EndsWith(".tmp", StringComparison.Ordinal) && Directory.GetCreationTimeUtc(dir) > DateTime.UtcNow.AddMinutes(-5))
                    continue;
                // A running exe can't be deleted, so this fails for a version that's still open and its files stay.
                var exe = Path.Combine(dir, "ReHome.exe");
                if (File.Exists(exe))
                    File.Delete(exe);
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
