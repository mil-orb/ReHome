# Changelog

Notable changes, newest first. Versions follow `<Version>` in `Directory.Build.props`.

## 0.1.1 (2026-11-11)

- `ReHome.exe` is about 8 MB, down from 59 MB, and so is every exported installer. It's compiled to native code with NativeAOT, and the window is plain Win32 hosting WebView2 instead of Windows Forms. `rehome.exe` is about 7 MB, down from 101 MB.
- Security: the .NET runtime inside both exes moves from 10.0.8 to 10.0.14, which fixes CVE-2026-111497. No `DOTNET_` or `CORECLR_` environment variable can load code into the elevated install helper anymore (no profiler API, diagnostics port or startup hooks in a native build). DLLs load from System32 only. Nothing is unpacked to `%TEMP%\.net`.

## 0.1.0 (2026-11-11)

First release, under the MIT license.

- Scan an old PC's apps and match each to its exact winget or Microsoft Store package. Drivers, runtimes, launcher games and apps Windows ships with are sorted out of the way.
- Export one portable `ReHome … Installer.exe` with the scan sealed inside (SHA-256), named after the apps it carries.
- Restore on the new PC: every installer downloaded and checked unelevated (catalog hash, signer against the old PC's) before one elevated helper installs the exact checked version.
- Settings for some apps are listed, with a secret check on every file. Carrying them over isn't built yet.
- `rehome` CLI with `scan`, `verify` and `install`, JSON lines when piped.
- Restore screen shows where it is (Choose, Check, Install, Done), a progress bar on the app being worked on, and a summary at the end with what to do about anything that didn't install.
- Every message appears in one place, as a card that says what to do next. Errors stay and offer the log; the rest close themselves.
- Drop an exported installer (or a scan .json) on the window to restore from it. The Open dialog takes exported installers too.
- Logo and app icon.
- Security: the published exe only asks for admin rights from its own copy in `%LOCALAPPDATA%`; installers whose certificate is revoked or distrusted fail; duplicate app ids in a scan are dropped; scan text can't forge log lines; releases build without write access.
