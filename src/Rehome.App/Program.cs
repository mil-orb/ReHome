using Microsoft.Web.WebView2.Core;

namespace Rehome.Shell;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // The elevated install helper. No window, no web view: it runs winget and reports back.
        if (args.Length > 0 && args[0] == Elevated.Flag)
            return Elevated.Serve(args);

        // One exe, run from anywhere: restarts from a private copy that has winget's metadata beside it.
        if (Portable.Restart(ref args))
            return 0;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write(e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();

        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "ReHome needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft and open ReHome again.",
                "ReHome", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        // Opening a scan file with Rehome (double-click, drag onto the exe) goes straight to restore.
        var file = args.Length == 1 && File.Exists(args[0]) ? Path.GetFullPath(args[0]) : null;
        Application.Run(new MainForm(file));
        return 0;
    }

    static void Crash(Exception? e)
    {
        if (e is not null)
            Log.Write(e);
        MessageBox.Show(
            $"ReHome ran into a problem and needs to close. Details are in {Log.FilePath}",
            "ReHome", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(1);
    }
}
