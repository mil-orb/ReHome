# Rehome

Set up a fresh Windows install, a new laptop or a new VM with the apps you already use. On the old PC, Rehome lists your apps and settings and saves them to one file. On the new one, it reinstalls every app it can from winget or the Microsoft Store, checks each installer before it runs, and asks one administrator question for the whole run.

Rehome is one exe on both sides, with nothing to install. On the old PC, **Export for new PC** writes a single installer named after what it carries, like `Rehome 7-Zip Brave VLC Installer.exe` (about 59 MB; a long list ends in `+47 more`), to a USB drive or wherever you pick, with the scan inside it. On the new PC, open that exe. It reads the scan inside, checks it wasn't damaged on the way, and lists your apps ready to install.

```
rehome scan                    # readable list
rehome scan -o scan.json       # JSON for scripts and agents
rehome verify scan.json        # download and check every installer, install nothing
rehome install scan.json       # check, then install; asks about anything unusual
rehome install scan.json --yes # unattended: installs only what passes every check
```

Piped output is JSON lines; the human summary goes to stderr. Exit codes: `0` all good, `1` something failed, `2` bad arguments, `130` stopped with Ctrl+C.

## What goes where

| Group | What's in it | On the new PC |
|---|---|---|
| Apps | Exact winget match (winget correlated it) | Installed, latest version |
| Microsoft Store | Exact Store match (package family) | Installed from the Store |
| Confirm | Same name and publisher in winget, nothing exact | Only if you tick it |
| From vendor sites | Real apps no official catalog carries (RustDesk, VMware Workstation) | Rehome opens the vendor's page; it never downloads these itself |
| Drivers and runtimes | Drivers, redistributables, runtimes, Windows parts | Unticked. Windows Update, the PC maker or the app that needs it brings these back |
| Steam | Games owned by a launcher | Reinstall from the launcher |

Left out entirely:

- Apps Windows installs itself: Game Bar, Get Help, Microsoft Store, Paint, Photos, Notepad, Calculator, Edge, OneDrive and the rest. Rehome reads Windows' own list of packages it provisions for every new user (`AppxAllUserStore`, readable without admin rights) and drops the Microsoft-published ones; Edge, WebView2 and OneDrive go by winget id. A fixed list of Microsoft package names (Clock, To Do, Calculator, Camera, Snipping Tool, Terminal, Phone Link, Clipchamp and about 40 more) covers PCs where an app was removed from that provisioned list but is still installed for the user. The publisher id has to be Microsoft's, so a lookalike package with the same name stays. WSL is kept, since installing it provisions it but a fresh Windows doesn't have it. Opening a scan runs the same check against the new PC, so older scan files clean up too.
- Shell extensions and components another app installs (`Companion`). They come back with their parent.
- Frameworks and packages signed as part of the OS.

Drivers and runtimes are sorted by name words (driver, chipset, redistributable, runtime, firmware, PhysX, OpenAL, GameInput, updater), Microsoft packages no catalog knows (Xbox Service, Windows SDK AddOn), a hardware maker's SDK, service or installer even when it's in a catalog (MSI Center SDK), and chip makers' software with no catalog entry. The rules lean toward showing: a runtime listed as an app costs one untick, a real app hidden is lost. The flag is display only, worked out again when a scan is opened, and never changes what can install or how it's checked.

Restore installs the latest version in the catalog, not the version the old PC had.

## Settings

Each app in a catalog can carry settings, listed under it and ticked by default. Rehome only reads files named in `SettingsCatalog` (`src/Rehome.Core/Settings.cs`), compiled into the exe:

- Exact files, or `*` inside one folder level. No folders, no `**`, no links followed. OBS keeps stream keys in `service.json` and ShareX keeps OAuth tokens in `UploadersConfig.json`, right beside files worth taking, so whole folders are never copied.
- Data only. No PowerShell profiles, scripts or anything that would run code on the new PC. VS Code extensions come back as a list of ids to install from the Marketplace.
- Every file passes a secret check first: private keys, known token formats (GitHub, Slack, AWS, Google, JWT), non-empty `password`/`token`/`secret`/`api_key` fields in JSON, INI and XML, and long random-looking strings. A file that trips it is left behind.
- Paths are stored as templates like `{AppData}\Code\User\settings.json`, so the scan carries no username.

The scan file holds file names only. Putting settings back needs the encrypted package, which isn't built yet.

## Sealed scans

Every scan Rehome writes (an export, or `rehome scan -o`) carries SHA-256 of its own bytes in the `check` field (computed with that field zeroed). Opening a scan recomputes it. If it doesn't match, Rehome refuses the file before anything is listed or installed: "This scan was changed or damaged after it was saved". Restore from a scan still opens `.json` files from the CLI; the app itself only exports installers. Scans from before the seal have no `check` and still open.

An exported exe carries the same sealed bytes inside it, so the check is identical. It catches a half-copied USB drive, a bad sector, or someone editing the JSON by hand. It doesn't stop a deliberate edit that recomputes the hash, since there's no key on the new PC to check against. Nothing in the file could stop that: Rehome isn't code-signed yet, so someone who can edit the exe can also remove the check. Copying it from a drive you control is the protection there. A copy downloaded from the internet gets SmartScreen's "unrecognized app" warning; one copied from a USB drive doesn't, since it carries no Mark of the Web.

A changed scan still can't reach past the rules below: only ids from Microsoft's two catalogs install, and every download is hash- and signer-checked on its own.

## Trust model

Sources:

- Only Microsoft's two catalogs: `winget` (winget-pkgs) and `msstore`. Sources added with `winget source add` are ignored, since anyone can host one.
- Opening a scan re-validates every entry. Package ids must match `^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$` and the source must be one of the two; anything else stops being installable. A guess installs only if it was confirmed when the scan was saved.

Every installer, before it runs (unelevated):

1. winget downloads it.
2. Rehome hashes it and compares SHA-256 with the manifest. A mismatch never installs and can't be overridden. The manifest's download URL has to be https; winget-pkgs only accepts https, so anything else didn't come through its review and fails.
3. Rehome reads the Authenticode signer and compares the organization with the one recorded on the old PC. Vendors renew certificates but keep their legal name, so the organization is what's compared.

| Result | When | What happens |
|---|---|---|
| Trusted | Same signer, or the old PC recorded none and the new one is validly signed, or MSIX/APPX | Installs |
| Unsigned | No valid signature | Asks. Nothing preselected; shows the download host and hash |
| Signer changed | Signed by a different organization | Asks, showing old and new signer |
| Failed | Hash mismatch, not https, download failed, manifest unreadable | Never installs |

Each passed check is logged with what it proved, for example "SHA-256 matches the winget catalog. Signed by Mozilla Corporation. From download.mozilla.org." The restore screen's log and `rehome verify` show the same line.

Store apps skip the download: the Store signs them and Windows verifies the signature on install.

Then the install, in one elevated helper:

- winget runs pinned to the exact version, scope, architecture and installer type that passed, so it installs the same bytes. winget re-checks the hash itself. Rehome never passes `--ignore-security-hash`, `--force` or `--override`.
- The cost: each installer downloads twice, once to check and once to install, so a restore uses about 2x the bandwidth.
- Apps already on the new PC are skipped. They're matched by winget's correlation or, for Store apps, by package family.

Links:

- https only, with a host and no user info. Catalog pages are rebuilt from the package id: the winget-pkgs manifest folder on GitHub, or the apps.microsoft.com listing. Vendor pages come from the winget manifest or the app's own uninstall entry.
- The page sends an app id and a link kind; the host looks the URL up in its own copy of the scan and opens it. Rows for vendor-site apps show the link's host before you open it.

Known gap: the scan file is trusted input. Someone who can edit it could point a vendor link at another https site, or swap a winget id for a different real package. That package would still be hash- and signer-checked and still come from winget, but it wouldn't be what you had. The expected signer comes from the same file, so an edited scan can name the swapped package's real signer and pass as Trusted. Signing the scan file is planned alongside the encrypted package.

The scan itself is read-only, runs unelevated, and makes no network calls beyond winget's own catalog queries. App icons come from each app's installed files (MSIX logo, uninstall entry icon, or main exe), never from the network.

### Elevation

Installs need administrator rights; the window doesn't. On Install, the window starts a second copy of itself elevated (`Rehome.exe --install <pipe> <pid>`), once, and talks to it over a named pipe:

- The pipe name is random and its ACL admits only the current user. It's created first-instance only.
- Each side checks the other's process id (`GetNamedPipeClientProcessId` / `GetNamedPipeServerProcessId`), and the helper checks the window runs from the same exe.
- The helper accepts 1 to 2000 items and re-validates every field against allowlists before building winget arguments. It never parses installers.
- It starts `winget.exe` from App Installer's own package folder under `C:\Program Files\WindowsApps` (owned by SYSTEM), and only after checking it's validly signed by Microsoft Corporation. The `winget` alias in `%LOCALAPPDATA%\Microsoft\WindowsApps` is never used elevated, since the user can write to that folder.
- .NET startup hooks are compiled out, so `DOTNET_STARTUP_HOOKS` can't load a DLL into it.
- It writes no files. Errors go back over the pipe and the window writes them to the log.
- Stop, or closing the window, lets the current installer finish and skips the rest. Closing mid-run asks first.

If you approve the prompt with a different administrator account, the helper can't open the pipe; Rehome says so and nothing installs.

### The desktop app

- The UI is built by Vite and compiled into `Rehome.exe` as resources, served from memory at `https://app.rehome.internal/` under a strict CSP. Nothing on disk can swap it out.
- The page can't navigate, open windows, download, request permissions or reach the network. It sends a fixed set of messages (`boot`, `scan`, `icons`, `export`, `showExport`, `open`, `present`, `install`, `decide`, `stop`, `link`, `about`, `setPreferences`, `showLog`, `log`) and refers to apps by id. The host acts on its own copy of the scan or plan.
- Everything it writes lives in `%LOCALAPPDATA%\Rehome`: `preferences.json` (theme), `rehome.log` (errors plus one line per restored app with its outcome, rotated at 1 MB), `Downloads` (emptied before each verify and after each installer), `App` (the copy it runs from, see Portable), and the WebView2 cache. Nothing goes in the registry, nothing leaves the PC.
- npm and NuGet versions are pinned exactly, locked, and at least two weeks old when added.

## Portable

`dotnet publish src/Rehome.App -c Release` gives one self-contained `Rehome.exe` of about 59 MB.

winget's API needs `Microsoft.Management.Deployment.winmd` (64 KB of type metadata) in the same folder as the running exe. WinRT reads it to talk to winget's out-of-process server, and for an app that isn't packaged Windows only looks beside the exe; without it every winget call fails with "Interface not registered". So the winmd is embedded in the exe, and on start, if it isn't beside it, Rehome copies its own program bytes and the winmd to `%LOCALAPPDATA%\Rehome\App\<hash>\` and restarts from there (about half a second, measured on an SSD). The cost is a hidden 59 MB copy on each PC that runs Rehome. Nothing is written next to the exe you started, so the USB drive stays one file. The folder is named by SHA-256 of the program bytes, re-checked before reuse and again right before the admin prompt; other versions in `App` are deleted on start.

An exported exe is the program's bytes followed by the sealed scan, its length and an 8-byte marker. Windows and the .NET single-file host ignore bytes past the bundle, so it runs like any other copy. Two things to know:

- Appending data to an exe can look odd to antivirus heuristics. Not seen here with Defender, but not tested widely either.
- Once Rehome is code-signed, appended bytes would break the signature. The usual fix is to carry the scan inside the signature's unauthenticated attributes, the way some vendors tag signed installers. Not built yet.

Managed code runs from inside the exe. Only two native DLLs (the WebView2 loader and winget's COM factory) are unpacked to `%TEMP%\.net`. The elevated install helper loads neither, since it only starts `winget.exe`. `IncludeAllContentForSelfExtract` stays off: it would make the elevated copy load every assembly from that user-writable folder.

A fresh Windows needs nothing else installed, with two exceptions it already has:

- the WebView2 Runtime (ships with Windows 11 and current Windows 10)
- App Installer (winget). On a brand-new install it can take a few minutes to register; if Rehome says winget isn't responding, update App Installer from the Store and try again.

Pass a scan file as the first argument to open straight into restore. With no argument, an exported exe opens the scan inside it.

Export copies the running program's bytes, so it only works from a published build. A developer build (an exe plus loose DLLs) says so instead of writing a file that can't start.

## Build

Needs the .NET 10 SDK, Node 22+, and App Installer (winget) 1.29 or newer.

```
dotnet test Rehome.slnx
dotnet build Rehome.slnx                  # also builds the UI (npm ci on first run)
dotnet publish src/Rehome -c Release      # CLI
dotnet publish src/Rehome.App -c Release  # desktop app, one portable Rehome.exe
```

Working on the UI without the app: `npm run dev` in `src/Rehome.App/ui` serves it at http://localhost:5199 with a fixture scan (`dev/fixture.json`) and a simulated install run (one unsigned app to review, one restart, one failure). The fixture is dropped from production builds.

Set `REHOME_TRACE=1` to print per-step timings to stderr, plus each settings file the secret check skipped.

## Layout

```
src/Rehome.Core/Scanner.cs        Scan entry point shared by the CLI and the app
src/Rehome.Core/Resolver.cs       Installed app -> group and package. Pure, unit tested
src/Rehome.Core/Categories.cs     Drivers, runtimes and Windows parts
src/Rehome.Core/WindowsApps.cs    Apps Windows installs itself, left out
src/Rehome.Core/ScanSeal.cs       SHA-256 seal on every scan file
src/Rehome.Core/ScanInExe.cs      Scan carried inside an exported exe
src/Rehome.App/Portable.cs        One-exe start: private copy with winget's metadata beside it
src/Rehome.Core/Settings.cs       Settings catalog, probe and secret check
src/Rehome.Core/Selection.cs      What the person picked, applied to a scan
src/Rehome.Core/Restore/          Plan loading, verification, trust decisions, winget runner, error text
src/Rehome.Core/Scan/             winget COM API, MSIX facts, Authenticode, cached MSIs
src/Rehome/                       CLI
src/Rehome.App/                   WebView2 shell: window, bridge, elevated helper, icons, preferences
src/Rehome.App/ui/                React UI
tests/Rehome.Tests/               xUnit
```
