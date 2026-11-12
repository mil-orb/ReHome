using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Rehome.Shell.Native;
using static Rehome.Shell.Native.Win32;

namespace Rehome.Shell;

/// <summary>
/// One window, one web view, locked to the UI compiled into this exe.
/// The page can't navigate anywhere, open windows, download, ask for permissions, or reach the network.
/// The only thing it can do is send the messages <see cref="Bridge"/> understands.
/// </summary>
sealed class Window
{
    const string Origin = "https://app.rehome.internal/";
    const string ClassName = "ReHomeWindow";

    // Client size at 96 DPI, and the smallest the whole window may get. Both scale with the display.
    const int Width = 760, Height = 820, MinWidth = 600, MinHeight = 520;
    const uint Style = WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN;

    const uint WM_RUN = WM_APP + 1;

    static Window? _current;

    readonly Bridge _bridge;
    readonly UiContext _context;
    readonly uint _uiThread = GetCurrentThreadId();
    // WebView2 holds the handlers through COM, but keep them reachable from here too.
    readonly List<object> _handlers = [];
    nint _brush;
    ICoreWebView2Controller? _controller;
    ICoreWebView2? _core;

    public nint Handle { get; private set; }

    /// <summary>A scan file Rehome was opened with. Taken once by the page on boot.</summary>
    public string? StartFile { get; set; }

    public bool Alive => Handle != 0;

    public bool OnUiThread => GetCurrentThreadId() == _uiThread;

    Window(string? startFile)
    {
        StartFile = startFile;
        _context = new UiContext(this);
        _bridge = new Bridge(this, json => _core?.PostWebMessageAsJson(json));
    }

    /// <summary>Is the WebView2 Runtime installed? Every Windows 11 and current Windows 10 has it.</summary>
    public static bool RuntimeAvailable()
    {
        var hr = WebView2Loader.GetAvailableCoreWebView2BrowserVersionString(null, out var version);
        if (version != 0)
            CoTaskMemFree(version);
        return hr >= 0 && version != 0;
    }

    /// <summary>Shows the window and runs its message loop until it closes.</summary>
    public static int Run(string? startFile)
    {
        var window = _current = new Window(startFile);
        SynchronizationContext.SetSynchronizationContext(window._context);
        window.Create();

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(msg);
            DispatchMessage(msg);
        }
        return 0;
    }

    unsafe void Create()
    {
        var instance = GetModuleHandle(0);
        fixed (char* name = ClassName)
        {
            var wc = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = &WndProc,
                Instance = instance,
                Cursor = LoadCursor(0, IDC_ARROW),
                // The icon compiled into the exe from Rehome.ico. The SDK gives it resource id 32512.
                Icon = LoadImage(instance, 32512, IMAGE_ICON, GetSystemMetricsForDpi(SM_CXICON, 96), GetSystemMetricsForDpi(SM_CXICON, 96), LR_DEFAULTCOLOR),
                IconSmall = LoadImage(instance, 32512, IMAGE_ICON, GetSystemMetricsForDpi(SM_CXSMICON, 96), GetSystemMetricsForDpi(SM_CXSMICON, 96), LR_DEFAULTCOLOR),
                ClassName = name,
            };
            if (RegisterClassEx(wc) == 0)
                throw new Win32Exception();
        }

        Handle = CreateWindowEx(0, ClassName, "ReHome", Style, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, 0, 0, instance, 0);
        if (Handle == 0)
            throw new Win32Exception();

        ApplyTheme();
        PlaceOnScreen();
        ShowWindow(Handle, SW_SHOWNORMAL);
        _ = StartAsync();
    }

    /// <summary>760×820 of page, centered on the screen it opens on, and never bigger than that screen.</summary>
    unsafe void PlaceOnScreen()
    {
        var dpi = GetDpiForWindow(Handle);
        var r = new Rect { Right = Scale(Width, dpi), Bottom = Scale(Height, dpi) };
        AdjustWindowRectExForDpi(ref r, Style, false, 0, dpi);
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfo(MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST), ref info);
        var work = info.Work;
        var w = Math.Min(r.Right - r.Left, work.Right - work.Left);
        var h = Math.Min(r.Bottom - r.Top, work.Bottom - work.Top);
        SetWindowPos(Handle, 0, work.Left + (work.Right - work.Left - w) / 2, work.Top + (work.Bottom - work.Top - h) / 2, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    static int Scale(int value, uint dpi) => (int)Math.Round(value * dpi / 96.0);

    async Task StartAsync()
    {
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rehome", "WebView2");
            var env = await CreateEnvironment(data);
            var controller = await CreateController(env);
            if (!Alive)
            {
                controller.Close();
                return;
            }
            _controller = controller;
            Resize();

            var core = _core = controller.get_CoreWebView2();
            Lockdown((ICoreWebView2Settings6)core.get_Settings());
            ApplyTheme();

            core.AddWebResourceRequestedFilter(Origin + "*", 0); // COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL
            core.add_WebResourceRequested(Keep(new WebResourceRequested(e => e.put_Response(Assets.Serve(env, e.get_Request().get_Uri())))));

            core.add_NavigationStarting(Keep(new NavigationStarting(e => e.put_Cancel(!IsOurs(e.get_Uri())))));
            core.add_FrameNavigationStarting(Keep(new NavigationStarting(e => e.put_Cancel(true))));
            core.add_NewWindowRequested(Keep(new NewWindowRequested(e => e.put_Handled(true))));
            ((ICoreWebView2_13)core).add_DownloadStarting(Keep(new DownloadStarting(e => e.put_Cancel(true))));
            core.add_PermissionRequested(Keep(new PermissionRequested(e => e.put_State(2)))); // DENY
            core.add_WebMessageReceived(Keep(new WebMessageReceived(OnMessage)));

            // If the page's renderer dies, log it and load the page again. The host keeps its state.
            core.add_ProcessFailed(Keep(new ProcessFailed(e =>
            {
                var failed = (ICoreWebView2ProcessFailedEventArgs2)e;
                var kind = failed.get_ProcessFailedKind();
                Log.Write($"web view process failed: {Name(FailedKinds, kind)} {Name(FailedReasons, failed.get_Reason())} exit {failed.get_ExitCode()}");
                if (kind is 1 or 2) // render process exited, render process unresponsive
                    core.Reload();
            })));

            core.Navigate(Origin + "index.html");
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException or InvalidCastException)
        {
            Log.Write(e);
            Dialogs.Message(Handle, "ReHome couldn't start its window. Reinstall the Microsoft Edge WebView2 Runtime and try again.",
                "ReHome", MB_OK | MB_ICONERROR);
            if (Alive)
                DestroyWindow(Handle);
        }
    }

    T Keep<T>(T handler) where T : class
    {
        _handlers.Add(handler);
        return handler;
    }

    unsafe Task<ICoreWebView2Environment> CreateEnvironment(string userDataFolder)
    {
        var done = new TaskCompletionSource<ICoreWebView2Environment>();
        var handler = Keep(new EnvironmentCompleted((hr, env) =>
        {
            if (hr < 0 || env is null) done.TrySetException(Marshal.GetExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005))!);
            else done.TrySetResult(env);
        }));

        var unknown = ComInterfaceMarshaller<IEnvironmentCompletedHandler>.ConvertToUnmanaged(handler);
        try
        {
            var hr = WebView2Loader.CreateCoreWebView2EnvironmentWithOptions(null, userDataFolder, 0, (nint)unknown);
            if (hr < 0)
                done.TrySetException(Marshal.GetExceptionForHR(hr)!);
        }
        finally
        {
            ComInterfaceMarshaller<IEnvironmentCompletedHandler>.Free(unknown);
        }
        return done.Task;
    }

    Task<ICoreWebView2Controller> CreateController(ICoreWebView2Environment env)
    {
        var done = new TaskCompletionSource<ICoreWebView2Controller>();
        env.CreateCoreWebView2Controller(Handle, Keep(new ControllerCompleted((hr, controller) =>
        {
            if (hr < 0 || controller is null) done.TrySetException(Marshal.GetExceptionForHR(hr < 0 ? hr : unchecked((int)0x80004005))!);
            else done.TrySetResult(controller);
        })));
        return done.Task;
    }

    void OnMessage(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        var e = (ICoreWebView2WebMessageReceivedEventArgs2)args;
        if (!IsOurs(e.get_Source()))
            return;
        // Files dropped on the page arrive as WebView2 File objects. Their paths come from Windows, not the page.
        _bridge.Handle(e.get_WebMessageAsJson(), DroppedFiles(e.get_AdditionalObjects()));
    }

    static List<string>? DroppedFiles(ICoreWebView2ObjectCollectionView? objects)
    {
        if (objects is null)
            return null;
        var files = new List<string>();
        var wrappers = new StrategyBasedComWrappers();
        for (uint i = 0, n = objects.get_Count(); i < n; i++)
        {
            var unknown = objects.GetValueAtIndex(i);
            try
            {
                if (wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None) is ICoreWebView2File file)
                    files.Add(file.get_Path());
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }
        return files;
    }

    static bool IsOurs(string uri) => uri.StartsWith(Origin, StringComparison.Ordinal);

    static void Lockdown(ICoreWebView2Settings6 s)
    {
        s.put_IsWebMessageEnabled(true);
        s.put_IsScriptEnabled(true);
        s.put_AreHostObjectsAllowed(false);
        s.put_AreDefaultContextMenusEnabled(false);
        s.put_AreDefaultScriptDialogsEnabled(false);
        s.put_IsStatusBarEnabled(false);
        s.put_IsZoomControlEnabled(false);
        s.put_IsPinchZoomEnabled(false);
        s.put_IsSwipeNavigationEnabled(false);
        s.put_IsGeneralAutofillEnabled(false);
        s.put_IsPasswordAutosaveEnabled(false);
        s.put_IsBuiltInErrorPageEnabled(false);
#if DEBUG
        s.put_AreDevToolsEnabled(true);
        s.put_AreBrowserAcceleratorKeysEnabled(true);
#else
        s.put_AreDevToolsEnabled(false);
        s.put_AreBrowserAcceleratorKeysEnabled(false);
#endif
    }

    /// <summary>Title bar, window and page all follow the theme preference.</summary>
    public void ApplyTheme()
    {
        if (!Alive)
            return;
        var bg = Theme.Background;
        var old = _brush;
        _brush = CreateSolidBrush((uint)(bg.R | (bg.G << 8) | (bg.B << 16)));
        if (old != 0)
            DeleteObject(old);
        Theme.ApplyTitleBar(Handle);

        if (_controller is ICoreWebView2Controller2 controller)
            controller.put_DefaultBackgroundColor(new WebViewColor { A = 255, R = bg.R, G = bg.G, B = bg.B });
        if (_core is ICoreWebView2_13 core)
            core.get_Profile().put_PreferredColorScheme(Preferences.Current.Theme switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0, // follow Windows
            });
    }

    void Resize()
    {
        if (_controller is null || !GetClientRect(Handle, out var r))
            return;
        _controller.put_Bounds(r);
    }

    /// <summary>Closing mid-run can't cancel an installer that's already going, so say what will happen and let them choose.</summary>
    bool ConfirmClose()
    {
        if (!_bridge.Installing)
            return true;
        var answer = Dialogs.Message(Handle,
            "ReHome is installing apps. If you close it, the app installing now finishes and the rest are skipped.",
            "Close ReHome?", MB_OKCANCEL | MB_ICONWARNING | MB_DEFBUTTON2);
        if (answer != IDOK)
            return false;
        _bridge.StopInstalling();
        return true;
    }

    /// <summary>Queues work for the window's thread. False if the window is gone.</summary>
    public bool Post(Action work)
    {
        if (!Alive)
            return false;
        _context.Enqueue(work);
        return PostMessage(Handle, WM_RUN, 0, 0);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            var w = _current;
            if (w is null || (w.Handle != 0 && w.Handle != hwnd))
                return DefWindowProc(hwnd, msg, wParam, lParam);
            return w.Message(hwnd, msg, wParam, lParam);
        }
        catch (Exception e)
        {
            Program.Crash(e);
            return 0;
        }
    }

    unsafe nint Message(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_RUN:
                _context.Drain();
                return 0;

            case WM_GETMINMAXINFO:
            {
                var dpi = GetDpiForWindow(hwnd);
                var info = (MinMaxInfo*)lParam;
                info->MinTrackSize = new Point { X = Scale(MinWidth, dpi), Y = Scale(MinHeight, dpi) };
                return 0;
            }

            case WM_DPICHANGED:
            {
                var r = (Rect*)lParam;
                SetWindowPos(hwnd, 0, r->Left, r->Top, r->Right - r->Left, r->Bottom - r->Top, SWP_NOZORDER | SWP_NOACTIVATE);
                return 0;
            }

            case WM_SIZE:
                if (wParam != SIZE_MINIMIZED)
                    Resize();
                return 0;

            case WM_MOVE:
                _controller?.NotifyParentWindowPositionChanged();
                return 0;

            case WM_SETFOCUS:
                _controller?.MoveFocus(0); // programmatic
                return 0;

            case WM_ERASEBKGND:
                // The window's own background shows only until the page paints. Same color, so no white flash.
                if (GetClientRect(hwnd, out var client))
                    FillRect(wParam, client, _brush);
                return 1;

            case WM_SETTINGCHANGE:
                // Windows' light/dark switch. The page follows on its own; the title bar and background follow here.
                if (lParam != 0 && new string((char*)lParam) == "ImmersiveColorSet")
                    ApplyTheme();
                break;

            case WM_CLOSE:
                if (ConfirmClose())
                    DestroyWindow(hwnd);
                return 0;

            case WM_DESTROY:
                Handle = 0;
                _controller?.Close();
                _controller = null;
                _core = null;
                if (_brush != 0)
                    DeleteObject(_brush);
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    static readonly string[] FailedKinds =
    [
        "BrowserProcessExited", "RenderProcessExited", "RenderProcessUnresponsive", "FrameRenderProcessExited",
        "UtilityProcessExited", "SandboxHelperProcessExited", "GpuProcessExited", "PpapiPluginProcessExited",
        "PpapiBrokerProcessExited", "UnknownProcessExited",
    ];

    static readonly string[] FailedReasons = ["Unexpected", "Unresponsive", "Terminated", "Crashed", "LaunchFailed", "OutOfMemory", "ProfileDeleted"];

    static string Name(string[] names, int value) => value >= 0 && value < names.Length ? names[value] : value.ToString();

    /// <summary>
    /// Brings every await in the window's code back to the window's thread, as WinForms did. The web view only
    /// takes calls on that thread.
    /// </summary>
    sealed class UiContext(Window window) : SynchronizationContext
    {
        readonly ConcurrentQueue<Action> _queue = new();

        public void Enqueue(Action work) => _queue.Enqueue(work);

        public override void Post(SendOrPostCallback d, object? state) => window.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (window.OnUiThread)
            {
                d(state);
                return;
            }
            using var done = new ManualResetEventSlim();
            Exception? error = null;
            if (!window.Post(() =>
                {
                    try { d(state); }
                    catch (Exception e) { error = e; }
                    finally { done.Set(); }
                }))
                throw new InvalidOperationException("The window is closed.");
            done.Wait();
            if (error is not null)
                throw new InvalidOperationException("Work sent to the window failed.", error);
        }

        public override SynchronizationContext CreateCopy() => this;

        public void Drain()
        {
            while (_queue.TryDequeue(out var work))
                work();
        }
    }
}
