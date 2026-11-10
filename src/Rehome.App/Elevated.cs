using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Rehome.Restore;

namespace Rehome.Shell;

/// <summary>
/// Installs need administrator rights, the window doesn't. So the window stays unelevated and starts a copy of
/// this exe once, elevated, in a headless mode that only runs winget. One approval for the whole run.
///
/// The two talk over a named pipe with a random name, usable only by this user. Each side checks the
/// other's process id before trusting a byte. The elevated side never parses installers: verification
/// already happened unelevated, and it re-validates every item it's handed.
/// </summary>
static partial class Elevated
{
    public const string Flag = "--install";

    // ---- Window side ----

    public static async Task Run(IReadOnlyList<InstallItem> items, Func<RunEvent, Task> report, CancellationToken stop)
    {
        var name = "rehome-" + Guid.NewGuid().ToString("N");
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        await using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);

        await Task.Run(Portable.CheckBeforeElevating);

        Process child;
        try
        {
            // The UAC prompt blocks the calling thread until it's answered. Keep it off the window's thread.
            child = await Task.Run(() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"{Flag} {name} {Environment.ProcessId}",
            })) ?? throw new RehomeException("Couldn't start the installer helper. Nothing was installed.");
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            throw new RehomeException("Installing needs administrator approval. Nothing was installed.");
        }
        catch (Win32Exception e)
        {
            Log.Write(e);
            throw new RehomeException("Couldn't start the installer helper. Nothing was installed.");
        }

        using (child)
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(60));
            var connected = server.WaitForConnectionAsync(connectTimeout.Token);
            var exited = child.WaitForExitAsync(connectTimeout.Token);
            try
            {
                if (await Task.WhenAny(connected, exited) != connected)
                {
                    if (stop.IsCancellationRequested)
                        return;
                    // Usually an admin account other than this user approved the prompt. The channel only admits
                    // this user, so the helper can't connect.
                    throw new RehomeException(child.HasExited
                        ? "The installer helper couldn't connect. Approve the prompt with this Windows account, then try again. Nothing was installed."
                        : "The installer helper didn't start. Nothing was installed.");
                }
                await connected;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return; // Stopped before anything ran. The caller marks the rest as stopped.
            }
            catch (OperationCanceledException)
            {
                throw new RehomeException("The installer helper didn't start. Nothing was installed.");
            }

            if (!GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(), out var clientPid) || clientPid != child.Id)
                throw new RehomeException("Something else answered the installer helper's channel. Nothing was installed.");

            var reader = new StreamReader(server);
            var writer = new StreamWriter(server) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new RunRequest { Items = items }, RunJson.Default.RunRequest));

            await using var onStop = stop.Register(() =>
            {
                try { writer.WriteLine("stop"); } catch (IOException) { } catch (ObjectDisposedException) { }
            });

            var finished = new HashSet<string>();
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync(CancellationToken.None)) is not null)
                {
                    RunEvent? e;
                    try { e = JsonSerializer.Deserialize(line, RunJson.Default.RunEvent); }
                    catch (JsonException) { Log.Write("helper sent a line that isn't an event"); continue; }
                    if (e is null) continue;
                    if (e.Done == true) return;
                    // The helper doesn't write files as admin. It sends its errors here to be logged.
                    if (e.State == "helper-error") { Log.Write($"installer helper: {e.Message}"); continue; }
                    if (e.LocalId is { } id && e.State is "installed" or "present" or "restart" or "failed" or "skipped")
                        finished.Add(id);
                    await report(e);
                }
            }
            catch (IOException e)
            {
                Log.Write(e); // A broken pipe throws here instead of ending the stream. Same meaning.
            }

            // The helper went away mid-run: closed, crashed, or killed.
            foreach (var item in items.Where(i => !finished.Contains(i.LocalId)))
                await report(new RunEvent { LocalId = item.LocalId, State = "failed", Message = "The installer helper stopped unexpectedly." });
        }
    }

    // ---- Elevated side ----

    public static int Serve(string[] args)
    {
        if (args.Length != 3 || !args[1].StartsWith("rehome-", StringComparison.Ordinal) || !int.TryParse(args[2], out var parentPid))
            return 2;

        // Outside the try so the pipe is still open when the catch reports an error over it.
        NamedPipeClientStream? client = null;
        StreamWriter? writer = null;
        try
        {
            client = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(15_000);

            // Only the window that started us may drive us.
            if (!GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var serverPid) || serverPid != parentPid || !IsSameExe(serverPid))
                return 3;

            var reader = new StreamReader(client);
            var send = writer = new StreamWriter(client) { AutoFlush = true };
            var request = JsonSerializer.Deserialize(reader.ReadLine() ?? "", RunJson.Default.RunRequest);
            if (request?.Items is not { Count: > 0 and <= 2000 } items)
                return 4;

            var stop = false;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await reader.ReadLineAsync() is { } line)
                        if (line == "stop") stop = true;
                }
                catch (IOException) { }
                stop = true; // window closed: finish the current install, start no more
            });

            Runner.Run(items, async e =>
            {
                try { await send.WriteLineAsync(JsonSerializer.Serialize(e, RunJson.Default.RunEvent)); }
                catch (IOException) { stop = true; }
            }, () => stop, CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception e)
        {
            // Headless and elevated: no window, and no crash handlers on this path. Writing the log from here
            // would be an admin file operation in a folder the user controls, so the window logs it instead.
            try { writer?.WriteLine(JsonSerializer.Serialize(new RunEvent { State = "helper-error", Message = e.ToString() }, RunJson.Default.RunEvent)); }
            catch (Exception) { }
            return 5;
        }
        finally
        {
            client?.Dispose();
        }
    }

    static bool IsSameExe(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return string.Equals(p.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(nint pipe, out int clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(nint pipe, out int serverProcessId);
}
