using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Rehome.Shell;

/// <summary>
/// One window, one web view, locked to the UI compiled into this exe.
/// The page can't navigate anywhere, open windows, download, ask for permissions, or reach the network.
/// The only thing it can do is send the messages <see cref="Bridge"/> understands.
/// </summary>
sealed class MainForm : Form
{
    const string Origin = "https://app.rehome.internal/";

    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly Bridge _bridge;

    /// <summary>A scan file Rehome was opened with. Taken once by the page on boot.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? StartFile { get; set; }

    public MainForm(string? startFile)
    {
        StartFile = startFile;
        Text = "Rehome";
        ClientSize = new Size(760, 820);
        MinimumSize = new Size(600, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BackColor = Theme.Background;
        _web.DefaultBackgroundColor = Theme.Background;
        Controls.Add(_web);

        _bridge = new Bridge(this, (json) => _web.CoreWebView2?.PostWebMessageAsJson(json));
        Load += async (_, _) =>
        {
            try
            {
                await StartAsync();
            }
            catch (Exception e) when (e is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
            {
                Log.Write(e);
                MessageBox.Show(this, "Rehome couldn't start its window. Reinstall the Microsoft Edge WebView2 Runtime and try again.",
                    "Rehome", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnThemeChanged;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing mid-run can't cancel an installer that's already going, so say what will happen and let them choose.
        if (_bridge.Installing && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(this,
                "Rehome is installing apps. If you close it, the app installing now finishes and the rest are skipped.",
                "Close Rehome?", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }
            _bridge.StopInstalling();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        base.OnFormClosed(e);
    }

    void OnThemeChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || IsDisposed)
            return;

        BeginInvoke(ApplyTheme);
    }

    /// <summary>Title bar, window and page all follow the theme preference.</summary>
    public void ApplyTheme()
    {
        BackColor = Theme.Background;
        _web.DefaultBackgroundColor = Theme.Background;
        Theme.ApplyTitleBar(Handle);
        if (_web.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = Preferences.Current.Theme switch
            {
                "light" => CoreWebView2PreferredColorScheme.Light,
                "dark" => CoreWebView2PreferredColorScheme.Dark,
                _ => CoreWebView2PreferredColorScheme.Auto,
            };
    }

    async Task StartAsync()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rehome", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        Lockdown(core.Settings);
        ApplyTheme();

        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => e.Response = Assets.Serve(core.Environment, e.Request.Uri, Origin);

        core.NavigationStarting += (_, e) => e.Cancel = !IsOurs(e.Uri);
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.WebMessageReceived += (_, e) =>
        {
            if (IsOurs(e.Source))
                _bridge.Handle(e.WebMessageAsJson);
        };

        // If the page's renderer dies, log it and load the page again. The host keeps its state.
        core.ProcessFailed += (_, e) =>
        {
            Log.Write($"web view process failed: {e.ProcessFailedKind} {e.Reason} exit {e.ExitCode}");
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                core.Reload();
        };

        core.Navigate(Origin + "index.html");
    }

    static bool IsOurs(string uri) => uri.StartsWith(Origin, StringComparison.Ordinal);

    static void Lockdown(CoreWebView2Settings s)
    {
        s.IsWebMessageEnabled = true;
        s.IsScriptEnabled = true;
        s.AreHostObjectsAllowed = false;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsBuiltInErrorPageEnabled = false;
#if DEBUG
        s.AreDevToolsEnabled = true;
        s.AreBrowserAcceleratorKeysEnabled = true;
#else
        s.AreDevToolsEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
#endif
    }
}
