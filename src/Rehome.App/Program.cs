using Rehome.Shell.Native;
using static Rehome.Shell.Native.Win32;

namespace Rehome.Shell;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The elevated install helper. No window, no web view: it runs winget and reports back. DLLs it loads by
        // name come from System32 only, never from the folder it runs in.
        if (args.Length > 0 && args[0] == Elevated.Flag)
        {
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
            return Elevated.Serve(args);
        }

        // One exe, run from anywhere: restarts from a private copy that has winget's files beside it.
        if (Portable.Restart(ref args))
            return 0;

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write(e.Exception);
            e.SetObserved();
        };

        SetProcessDpiAwarenessContext(-4); // per monitor v2: sharp text on every display
        _ = OleInitialize(0);

        if (!Window.RuntimeAvailable())
        {
            Dialogs.Message(0, "ReHome needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft and open ReHome again.",
                "ReHome", MB_OK | MB_ICONINFORMATION);
            return 1;
        }

        // Opening a scan file with Rehome (double-click, drag onto the exe) goes straight to restore.
        var file = args.Length == 1 && File.Exists(args[0]) ? Path.GetFullPath(args[0]) : null;
        return Window.Run(file);
    }

    public static void Crash(Exception? e)
    {
        if (e is not null)
            Log.Write(e);
        Dialogs.Message(0, $"ReHome ran into a problem and needs to close. Details are in {Log.FilePath}", "ReHome", MB_OK | MB_ICONERROR);
        Environment.Exit(1);
    }
}
