# How ReHome works

What a scan picks up, what it leaves out, how a scan travels inside an exported exe, and how one exe runs anywhere. For what's checked before anything installs, see [security-model.md](security-model.md). For the code, see [architecture.md](architecture.md).

## What goes where

| Group | What's in it | On the new PC |
|---|---|---|
| Apps | Exact winget match (winget correlated it) | Installed, latest version |
| Microsoft Store | Exact Store match (package family) | Installed from the Store |
| Confirm | Same name and publisher in winget, nothing exact | Only if you tick it |
| From vendor sites | Real apps no official catalog carries (RustDesk, VMware Workstation) | ReHome opens the vendor's page; it never downloads these itself |
| Drivers and runtimes | Drivers, redistributables, runtimes, Windows parts | Unticked. Windows Update, the PC maker or the app that needs it brings these back |
| Steam | Games owned by a launcher | Reinstall from the launcher |

Left out entirely:

- Apps Windows installs itself: Game Bar, Get Help, Microsoft Store, Paint, Photos, Notepad, Calculator, Edge, OneDrive and the rest. ReHome reads Windows' own list of packages it provisions for every new user (`AppxAllUserStore`, readable without admin rights) and drops the Microsoft-published ones; Edge, WebView2 and OneDrive go by winget id. A fixed list of Microsoft package names (Clock, To Do, Calculator, Camera, Snipping Tool, Terminal, Phone Link, Clipchamp and about 40 more) covers PCs where an app was removed from that provisioned list but is still installed for the user. The publisher id has to be Microsoft's, so a lookalike package with the same name stays. WSL is kept, since installing it provisions it but a fresh Windows doesn't have it. Opening a scan runs the same check against the new PC, so older scan files clean up too.
- Shell extensions and components another app installs (`Companion`). They come back with their parent.
- Frameworks and packages signed as part of the OS.

Drivers and runtimes are sorted by name words (driver, chipset, redistributable, runtime, firmware, PhysX, OpenAL, GameInput, updater), Microsoft packages no catalog knows (Xbox Service, Windows SDK AddOn), a hardware maker's SDK, service or installer even when it's in a catalog (MSI Center SDK), and chip makers' software with no catalog entry. The rules lean toward showing: a runtime listed as an app costs one untick, a real app hidden is lost. The flag is display only, worked out again when a scan is opened, and never changes what can install or how it's checked.

Restore installs the latest version in the catalog, not the version the old PC had.

## Settings

Each app in a catalog can carry settings, listed under it and ticked by default. ReHome only reads files named in `SettingsCatalog` (`src/Rehome.Core/Settings.cs`), compiled into the exe:

- Exact files, or `*` inside one folder level. No folders, no `**`, no links followed. OBS keeps stream keys in `service.json` and ShareX keeps OAuth tokens in `UploadersConfig.json`, right beside files worth taking, so whole folders are never copied.
- Data only. No PowerShell profiles, scripts or anything that would run code on the new PC. VS Code extensions come back as a list of ids to install from the Marketplace.
- Every file passes a secret check first: private keys, known token formats (GitHub, Slack, AWS, Google, JWT), non-empty `password`/`token`/`secret`/`api_key` fields in JSON, INI and XML, and long random-looking strings. A file that trips it is left behind.
- Paths are stored as templates like `{AppData}\Code\User\settings.json`, so the scan carries no username.

The scan file holds file names only. Putting settings back needs the encrypted package, which isn't built yet.

## Sealed scans

Every scan ReHome writes (an export, or `rehome scan -o`) carries SHA-256 of its own bytes in the `check` field (computed with that field zeroed). Opening a scan recomputes it. If it doesn't match, ReHome refuses the file before anything is listed or installed: "This scan was changed or damaged after it was saved". Restore from a scan still opens `.json` files from the CLI; the app itself only exports installers. Scans from before the seal have no `check` and still open.

An exported exe carries the same sealed bytes inside it, so the check is identical. It catches a half-copied USB drive, a bad sector, or someone editing the JSON by hand. It doesn't stop a deliberate edit that recomputes the hash, since there's no key on the new PC to check against. Nothing in the file could stop that: ReHome isn't code-signed yet, so someone who can edit the exe can also remove the check. Copying it from a drive you control is the protection there. A copy downloaded from the internet gets SmartScreen's "unrecognized app" warning; one copied from a USB drive doesn't, since it carries no Mark of the Web.

A changed scan still can't reach past the rules in [security-model.md](security-model.md): only ids from Microsoft's two catalogs install, and every download is hash- and signer-checked on its own.

## Portable

`dotnet publish src/Rehome.App -c Release` gives one `ReHome.exe` of about 8 MB. It's compiled to native code with NativeAOT, so there's no .NET runtime to install or unpack, and the window is plain Win32 hosting WebView2.

winget's API needs two files in the same folder as the running exe: `Microsoft.Management.Deployment.winmd` (64 KB of type metadata, which WinRT reads to talk to winget's out-of-process server) and `Microsoft.Management.Deployment.dll` (winget's 130 KB native factory). For an app that isn't packaged Windows only looks beside the exe; without them every winget call fails with "Interface not registered" or "Class not registered". So both are embedded in the exe, and on start the published exe copies its own program bytes and the two files to `%LOCALAPPDATA%\Rehome\App\<hash>\` and restarts from there. The cost is a hidden 8 MB copy on each PC that runs ReHome. Nothing is written next to the exe you started, so the USB drive stays one file. The folder is named by SHA-256 of the program bytes, re-checked before reuse and again right before the admin prompt; other versions in `App` are deleted on start. The published exe always makes this move, and only ever asks for admin rights from the copy, since the folder you started it from may be one anyone can write to.

An exported exe is the program's bytes followed by the sealed scan, its length and an 8-byte marker. Windows ignores bytes past the end of the program image, so it runs like any other copy. Two things to know:

- Appending data to an exe can look odd to antivirus heuristics. Not seen here with Defender, but not tested widely either.
- Once ReHome is code-signed, appended bytes would break the signature. The usual fix is to carry the scan inside the signature's unauthenticated attributes, the way some vendors tag signed installers. Not built yet.

Nothing is unpacked anywhere else. The WebView2 loader is linked into the exe from the package's static library. The exe's own imports resolve from System32 only (`/DEPENDENTLOADFLAG`), so a DLL dropped beside the cached copy isn't picked up at start. The elevated install helper also limits DLLs it loads by name to System32 and never loads winget's factory, since it only starts `winget.exe`.

A fresh Windows needs nothing else installed, with two exceptions it already has:

- the WebView2 Runtime (ships with Windows 11 and current Windows 10)
- App Installer (winget). On a brand-new install it can take a few minutes to register; if ReHome says winget isn't responding, update App Installer from the Store and try again.

Pass a scan file as the first argument to open straight into restore. With no argument, an exported exe opens the scan inside it.

Export copies the running program's bytes, so it only works from a published build. A developer build (an exe plus loose DLLs) says so instead of writing a file that can't start.
