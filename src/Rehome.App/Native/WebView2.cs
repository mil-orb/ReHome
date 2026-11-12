using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Rehome.Shell.Native;

// WebView2's COM API, only the parts ReHome uses, typed from WebView2.idl in the Microsoft.Web.WebView2 package.
// The package's own .NET wrapper relies on built-in COM interop, which NativeAOT doesn't have, so these are
// source-generated instead. Slot order matters: a method ReHome never calls is still declared (as _N) to keep
// every later method at its place in the vtable. The loader is linked into the exe from WebView2LoaderStatic.lib.

[StructLayout(LayoutKind.Sequential)]
struct Rect { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
struct WebViewColor { public byte A, R, G, B; }

static partial class WebView2Loader
{
    const string Dll = "WebView2Loader.dll";

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CreateCoreWebView2EnvironmentWithOptions(string? browserExecutableFolder, string? userDataFolder, nint options, nint handler);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetAvailableCoreWebView2BrowserVersionString(string? browserExecutableFolder, out nint versionInfo);
}

[GeneratedComInterface, Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
partial interface IEnvironmentCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2Environment? result);
}

[GeneratedComInterface, Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
partial interface IControllerCompletedHandler
{
    void Invoke(int errorCode, ICoreWebView2Controller? result);
}

[GeneratedComInterface, Guid("b96d755e-0319-4e92-a296-23436f46a1fc")]
partial interface ICoreWebView2Environment
{
    void CreateCoreWebView2Controller(nint parentWindow, IControllerCompletedHandler handler);
    ICoreWebView2WebResourceResponse CreateWebResourceResponse(nint content, int statusCode,
        [MarshalAs(UnmanagedType.LPWStr)] string reasonPhrase, [MarshalAs(UnmanagedType.LPWStr)] string headers);
}

[GeneratedComInterface, Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
partial interface ICoreWebView2Controller
{
    void _1();
    void put_IsVisible([MarshalAs(UnmanagedType.Bool)] bool isVisible);
    void _3();
    void put_Bounds(Rect bounds);
    void _5(); void _6(); void _7(); void _8(); void _9();
    void MoveFocus(int reason);
    void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19(); void _20();
    void NotifyParentWindowPositionChanged();
    void Close();
    ICoreWebView2 get_CoreWebView2();
}

[GeneratedComInterface, Guid("c979903e-d4ca-4228-92eb-47ee3fa96eab")]
partial interface ICoreWebView2Controller2 : ICoreWebView2Controller
{
    void _c2_1();
    void put_DefaultBackgroundColor(WebViewColor value);
}

[GeneratedComInterface, Guid("76eceacb-0462-4d94-ac83-423a6793775e")]
partial interface ICoreWebView2
{
    ICoreWebView2Settings get_Settings();
    void _2();
    void Navigate([MarshalAs(UnmanagedType.LPWStr)] string uri);
    void _4();
    long add_NavigationStarting(INavigationStartingHandler handler);
    void _6(); void _7(); void _8(); void _9(); void _10(); void _11(); void _12(); void _13(); void _14();
    long add_FrameNavigationStarting(INavigationStartingHandler handler);
    void _16(); void _17(); void _18(); void _19(); void _20();
    long add_PermissionRequested(IPermissionRequestedHandler handler);
    void _22();
    long add_ProcessFailed(IProcessFailedHandler handler);
    void _24(); void _25(); void _26(); void _27(); void _28();
    void Reload();
    void PostWebMessageAsJson([MarshalAs(UnmanagedType.LPWStr)] string webMessageAsJson);
    void _31();
    long add_WebMessageReceived(IWebMessageReceivedHandler handler);
    void _33(); void _34(); void _35(); void _36(); void _37(); void _38(); void _39(); void _40(); void _41();
    long add_NewWindowRequested(INewWindowRequestedHandler handler);
    void _43(); void _44(); void _45(); void _46(); void _47(); void _48(); void _49(); void _50(); void _51(); void _52();
    long add_WebResourceRequested(IWebResourceRequestedHandler handler);
    void _54();
    void AddWebResourceRequestedFilter([MarshalAs(UnmanagedType.LPWStr)] string uri, int resourceContext);
    void _56(); void _57(); void _58();
}

/// <summary>ICoreWebView2_2 through _13 flattened: DownloadStarting comes from _4, Profile from _13.</summary>
[GeneratedComInterface, Guid("f75f09a8-667e-4983-88d6-c8773f315e84")]
partial interface ICoreWebView2_13 : ICoreWebView2
{
    // _2: 7 methods
    void _a1(); void _a2(); void _a3(); void _a4(); void _a5(); void _a6(); void _a7();
    // _3: 5 methods
    void _b1(); void _b2(); void _b3(); void _b4(); void _b5();
    // _4: FrameCreated, then DownloadStarting
    void _c1(); void _c2();
    long add_DownloadStarting(IDownloadStartingHandler handler);
    void _c4();
    // _5 to _12: 2 + 1 + 1 + 7 + 9 + 2 + 3 + 3 = 28 methods
    void _d01(); void _d02(); void _d03(); void _d04(); void _d05(); void _d06(); void _d07(); void _d08(); void _d09(); void _d10();
    void _d11(); void _d12(); void _d13(); void _d14(); void _d15(); void _d16(); void _d17(); void _d18(); void _d19(); void _d20();
    void _d21(); void _d22(); void _d23(); void _d24(); void _d25(); void _d26(); void _d27(); void _d28();
    // _13
    ICoreWebView2Profile get_Profile();
}

[GeneratedComInterface, Guid("79110ad3-cd5d-4373-8bc3-c60658f17a5f")]
partial interface ICoreWebView2Profile
{
    void _1(); void _2(); void _3(); void _4(); void _5(); void _6();
    void put_PreferredColorScheme(int value);
}

/// <summary>ICoreWebView2Settings through Settings6 flattened. Every setting ReHome sets is a BOOL.</summary>
[GeneratedComInterface, Guid("11cb3acd-9bc8-43b8-83bf-f40753714f87")]
partial interface ICoreWebView2Settings6
{
    void _1(); void put_IsScriptEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _3(); void put_IsWebMessageEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _5(); void put_AreDefaultScriptDialogsEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _7(); void put_IsStatusBarEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _9(); void put_AreDevToolsEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _11(); void put_AreDefaultContextMenusEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _13(); void put_AreHostObjectsAllowed([MarshalAs(UnmanagedType.Bool)] bool value);
    void _15(); void put_IsZoomControlEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _17(); void put_IsBuiltInErrorPageEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    // Settings2: UserAgent
    void _19(); void _20();
    // Settings3
    void _21(); void put_AreBrowserAcceleratorKeysEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    // Settings4
    void _23(); void put_IsPasswordAutosaveEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    void _25(); void put_IsGeneralAutofillEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    // Settings5
    void _27(); void put_IsPinchZoomEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
    // Settings6
    void _29(); void put_IsSwipeNavigationEnabled([MarshalAs(UnmanagedType.Bool)] bool value);
}

[GeneratedComInterface, Guid("e562e4f0-d7fa-43ac-8d71-c05150499f00")]
partial interface ICoreWebView2Settings;

[GeneratedComInterface, Guid("aafcc94f-fa27-48fd-97df-830ef75aaec9")]
partial interface ICoreWebView2WebResourceResponse;

// ---- Event arguments ----

[GeneratedComInterface, Guid("5b495469-e119-438a-9b18-7604f25f2e49")]
partial interface ICoreWebView2NavigationStartingEventArgs
{
    [return: MarshalAs(UnmanagedType.LPWStr)] string get_Uri();
    void _2(); void _3(); void _4(); void _5();
    void put_Cancel([MarshalAs(UnmanagedType.Bool)] bool cancel);
}

[GeneratedComInterface, Guid("34acb11c-fc37-4418-9132-f9c21d1eafb9")]
partial interface ICoreWebView2NewWindowRequestedEventArgs
{
    void _1(); void _2(); void _3();
    void put_Handled([MarshalAs(UnmanagedType.Bool)] bool handled);
}

[GeneratedComInterface, Guid("973ae2ef-ff18-4894-8fb2-3c758f046810")]
partial interface ICoreWebView2PermissionRequestedEventArgs
{
    void _1(); void _2(); void _3(); void _4();
    void put_State(int state);
}

[GeneratedComInterface, Guid("06fc7ab7-c90c-4297-9389-33ca01cf6d5e")]
partial interface ICoreWebView2WebMessageReceivedEventArgs2
{
    [return: MarshalAs(UnmanagedType.LPWStr)] string get_Source();
    [return: MarshalAs(UnmanagedType.LPWStr)] string get_WebMessageAsJson();
    void _3();
    ICoreWebView2ObjectCollectionView? get_AdditionalObjects();
}

[GeneratedComInterface, Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
partial interface ICoreWebView2WebMessageReceivedEventArgs;

[GeneratedComInterface, Guid("0f36fd87-4f69-4415-98da-888f89fb9a33")]
partial interface ICoreWebView2ObjectCollectionView
{
    uint get_Count();
    nint GetValueAtIndex(uint index);
}

[GeneratedComInterface, Guid("f2c19559-6bc1-4583-a757-90021be9afec")]
partial interface ICoreWebView2File
{
    [return: MarshalAs(UnmanagedType.LPWStr)] string get_Path();
}

[GeneratedComInterface, Guid("4dab9422-46fa-4c3e-a5d2-41d2071d3680")]
partial interface ICoreWebView2ProcessFailedEventArgs2
{
    int get_ProcessFailedKind();
    int get_Reason();
    int get_ExitCode();
}

[GeneratedComInterface, Guid("8155a9a4-1474-4a86-8cae-151b0fa6b8ca")]
partial interface ICoreWebView2ProcessFailedEventArgs;

[GeneratedComInterface, Guid("453e667f-12c7-49d4-be6d-ddbe7956f57a")]
partial interface ICoreWebView2WebResourceRequestedEventArgs
{
    ICoreWebView2WebResourceRequest get_Request();
    void _2();
    void put_Response(ICoreWebView2WebResourceResponse response);
}

[GeneratedComInterface, Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5")]
partial interface ICoreWebView2WebResourceRequest
{
    [return: MarshalAs(UnmanagedType.LPWStr)] string get_Uri();
}

[GeneratedComInterface, Guid("e99bbe21-43e9-4544-a732-282764eafa60")]
partial interface ICoreWebView2DownloadStartingEventArgs
{
    void _1(); void _2();
    void put_Cancel([MarshalAs(UnmanagedType.Bool)] bool cancel);
}

// ---- Event handlers (implemented here, called by WebView2) ----

[GeneratedComInterface, Guid("9adbe429-f36d-432b-9ddc-f8881fbd76e3")]
partial interface INavigationStartingHandler { void Invoke(ICoreWebView2 sender, ICoreWebView2NavigationStartingEventArgs args); }

[GeneratedComInterface, Guid("d4c185fe-c81c-4989-97af-2d3fa7ab5651")]
partial interface INewWindowRequestedHandler { void Invoke(ICoreWebView2 sender, ICoreWebView2NewWindowRequestedEventArgs args); }

[GeneratedComInterface, Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd")]
partial interface IPermissionRequestedHandler { void Invoke(ICoreWebView2 sender, ICoreWebView2PermissionRequestedEventArgs args); }

[GeneratedComInterface, Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
partial interface IWebMessageReceivedHandler { void Invoke(ICoreWebView2 sender, ICoreWebView2WebMessageReceivedEventArgs args); }

[GeneratedComInterface, Guid("79e0aea4-990b-42d9-aa1d-0fcc2e5bc7f1")]
partial interface IProcessFailedHandler { void Invoke(ICoreWebView2 sender, ICoreWebView2ProcessFailedEventArgs args); }

[GeneratedComInterface, Guid("ab00b74c-15f1-4646-80e8-e76341d25d71")]
partial interface IWebResourceRequestedHandler { void Invoke(ICoreWebView2 sender, ICoreWebView2WebResourceRequestedEventArgs args); }

[GeneratedComInterface, Guid("efedc989-c396-41ca-83f7-07f845a55724")]
partial interface IDownloadStartingHandler { void Invoke(ICoreWebView2 sender, ICoreWebView2DownloadStartingEventArgs args); }

[GeneratedComClass]
sealed partial class EnvironmentCompleted(Action<int, ICoreWebView2Environment?> done) : IEnvironmentCompletedHandler
{
    public void Invoke(int errorCode, ICoreWebView2Environment? result) => done(errorCode, result);
}

[GeneratedComClass]
sealed partial class ControllerCompleted(Action<int, ICoreWebView2Controller?> done) : IControllerCompletedHandler
{
    public void Invoke(int errorCode, ICoreWebView2Controller? result) => done(errorCode, result);
}

[GeneratedComClass]
sealed partial class NavigationStarting(Action<ICoreWebView2NavigationStartingEventArgs> on) : INavigationStartingHandler
{
    public void Invoke(ICoreWebView2 sender, ICoreWebView2NavigationStartingEventArgs args) => on(args);
}

[GeneratedComClass]
sealed partial class NewWindowRequested(Action<ICoreWebView2NewWindowRequestedEventArgs> on) : INewWindowRequestedHandler
{
    public void Invoke(ICoreWebView2 sender, ICoreWebView2NewWindowRequestedEventArgs args) => on(args);
}

[GeneratedComClass]
sealed partial class PermissionRequested(Action<ICoreWebView2PermissionRequestedEventArgs> on) : IPermissionRequestedHandler
{
    public void Invoke(ICoreWebView2 sender, ICoreWebView2PermissionRequestedEventArgs args) => on(args);
}

[GeneratedComClass]
sealed partial class WebMessageReceived(Action<ICoreWebView2WebMessageReceivedEventArgs> on) : IWebMessageReceivedHandler
{
    public void Invoke(ICoreWebView2 sender, ICoreWebView2WebMessageReceivedEventArgs args) => on(args);
}

[GeneratedComClass]
sealed partial class ProcessFailed(Action<ICoreWebView2ProcessFailedEventArgs> on) : IProcessFailedHandler
{
    public void Invoke(ICoreWebView2 sender, ICoreWebView2ProcessFailedEventArgs args) => on(args);
}

[GeneratedComClass]
sealed partial class WebResourceRequested(Action<ICoreWebView2WebResourceRequestedEventArgs> on) : IWebResourceRequestedHandler
{
    public void Invoke(ICoreWebView2 sender, ICoreWebView2WebResourceRequestedEventArgs args) => on(args);
}

[GeneratedComClass]
sealed partial class DownloadStarting(Action<ICoreWebView2DownloadStartingEventArgs> on) : IDownloadStartingHandler
{
    public void Invoke(ICoreWebView2 sender, ICoreWebView2DownloadStartingEventArgs args) => on(args);
}
