# Security model

ReHome runs installers as administrator on a PC someone just set up, so this is the part that has to be right. This page lists every check between a scan file and a running installer, and the gaps that are still open. To report a problem, see [SECURITY.md](../SECURITY.md).

## Sources and checks

Sources:

- Only Microsoft's two catalogs: `winget` (winget-pkgs) and `msstore`. Sources added with `winget source add` are ignored, since anyone can host one.
- Opening a scan re-validates every entry. Package ids must match `^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$` and the source must be one of the two; anything else stops being installable. A guess installs only if it was confirmed when the scan was saved.

Every installer, before it runs (unelevated):

1. winget downloads it.
2. ReHome hashes it and compares SHA-256 with the manifest. A mismatch never installs and can't be overridden. The manifest's download URL has to be https; winget-pkgs only accepts https, so anything else didn't come through its review and fails.
3. ReHome reads the Authenticode signer and compares the organization with the one recorded on the old PC. Vendors renew certificates but keep their legal name, so the organization is what's compared.
4. Windows checks the signing certificate's revocation status for the whole chain, online. Revoked, explicitly distrusted, or chained to an untrusted root fails the install. If the revocation server can't be reached, step 3's result stands.

| Result | When | What happens |
|---|---|---|
| Trusted | Same signer, or the old PC recorded none and the new one is validly signed, or MSIX/APPX | Installs |
| Unsigned | No valid signature | Asks. Nothing preselected; shows the download host and hash |
| Signer changed | Signed by a different organization | Asks, showing old and new signer |
| Failed | Hash mismatch, not https, certificate revoked or distrusted, download failed, manifest unreadable | Never installs |

Each passed check is logged with what it proved, for example "SHA-256 matches the winget catalog. Signed by Mozilla Corporation. From download.mozilla.org." The restore screen's log and `rehome verify` show the same line.

Store apps skip the download: the Store signs them and Windows verifies the signature on install.

Then the install, in one elevated helper:

- winget runs pinned to the exact version, scope, architecture and installer type that passed. winget checks the download against the manifest's hash again. ReHome never passes `--ignore-security-hash`, `--force` or `--override`. The hash itself isn't pinned, though: see [Known gaps](#known-gaps).
- The cost: each installer downloads twice, once to check and once to install, so a restore uses about 2x the bandwidth.
- Apps already on the new PC are skipped. They're matched by winget's correlation or, for Store apps, by package family.

Links:

- https only, with a host and no user info. Catalog pages are rebuilt from the package id: the winget-pkgs manifest folder on GitHub, or the apps.microsoft.com listing. Vendor pages come from the winget manifest or the app's own uninstall entry.
- The page sends an app id and a link kind; the host looks the URL up in its own copy of the scan and opens it. Rows for vendor-site apps show the link's host before you open it.

The scan itself is read-only, runs unelevated, and makes no network calls beyond winget's own catalog queries. App icons come from each app's installed files (MSIX logo, uninstall entry icon, or main exe), never from the network.

## Elevation

Installs need administrator rights; the window doesn't. On Install, the window starts a second copy of itself elevated (`ReHome.exe --install <pipe> <pid>`), once, and talks to it over a named pipe:

- The pipe name is random and its ACL admits only the current user. It's created first-instance only. The helper connects at identification level, so the window can check who connected but can't act with the helper's token.
- Each side checks the other's process id (`GetNamedPipeClientProcessId` / `GetNamedPipeServerProcessId`), and the helper checks the window runs from the same exe.
- The helper accepts 1 to 2000 items and re-validates every field against allowlists before building winget arguments. It never parses installers.
- It starts `winget.exe` from App Installer's own package folder under `C:\Program Files\WindowsApps` (owned by SYSTEM), and only after checking it's validly signed by Microsoft Corporation. The `winget` alias in `%LOCALAPPDATA%\Microsoft\WindowsApps` is never used elevated, since the user can write to that folder.
- The published exe only elevates from its copy in `%LOCALAPPDATA%\Rehome\App`, a folder only you can write to. It always moves there first, even when winget's metadata file sits beside it, so a file planted next to a copy in Downloads or on a FAT-formatted USB drive can't keep it running from there. If the copy can't be set up, Install stops before the admin prompt.
- `ReHome.exe` is native code, compiled with NativeAOT. There's no JIT, no startup hooks, no profiler API and no diagnostics port, so no `DOTNET_` or `CORECLR_` environment variable can load code into the helper. (The GC still reads its tuning settings from `DOTNET_` variables. Those change memory use, not what runs.)
- DLLs come from System32 only. The exe's imports are resolved there (`/DEPENDENTLOADFLAG`), and the helper calls `SetDefaultDllDirectories` before anything else, so a DLL planted beside the cached copy isn't loaded.
- ReHome's own code writes no files there. Errors go back over the pipe and the window writes them to the log.
- Stop, or closing the window, lets the current installer finish and skips the rest. Closing mid-run asks first.

If you approve the prompt with a different administrator account, the helper can't open the pipe; ReHome says so and nothing installs.

## The desktop app

- The UI is built by Vite and compiled into `ReHome.exe` as resources, served from memory at `https://app.rehome.internal/` under a strict CSP. Nothing on disk can swap it out.
- The page can't navigate, open windows, download, request permissions or reach the network. It sends a fixed set of messages (`boot`, `scan`, `icons`, `export`, `showExport`, `open`, `openDropped`, `present`, `install`, `decide`, `stop`, `link`, `about`, `setPreferences`, `showLog`, `log`) and refers to apps by id. The host acts on its own copy of the scan or plan.
- A file dropped on the window reaches the host as WebView2's own `File` object next to the `openDropped` message; the host reads its path from that object, never from text the page sends. One file only, `.exe` or `.json`. An exe is only read for the scan appended to it, never run. Both go through the same checks as a file picked in the Open dialog.
- Scan text written to the log (app ids, winget messages) has control characters replaced, and errors the page reports are indented, so a crafted scan can't add lines that look like ReHome's own.
- Everything it writes lives in `%LOCALAPPDATA%\Rehome`: `preferences.json` (theme), `rehome.log` (errors plus one line per restored app with its outcome, rotated at 1 MB), `Downloads` (emptied before each verify and after each installer), `App` (the copy it runs from, see [Portable](how-it-works.md#portable)), and the WebView2 cache. Nothing goes in the registry, nothing leaves the PC.
- npm and NuGet versions are pinned exactly, locked, and at least two weeks old when added.

## Known gaps

These are open, written down so nobody has to rediscover them.

- **The scan file is trusted for what to install.** Someone who can edit it can point a vendor link at another https site, or swap a winget id for a different real package. That package still comes from winget and is still hash-checked, but it isn't what you had. The expected signer comes from the same file, so an edited scan can name the swapped package's real signer and pass as Trusted. The seal doesn't help here: it has no key (see [Sealed scans](how-it-works.md#sealed-scans)). The fix is a signed scan, planned with the encrypted settings package.
- **ReHome isn't code-signed.** A downloaded copy gets SmartScreen's "unrecognized app" warning, and anyone who can edit the exe can change what it does. Signing also closes the next two.
- **Hash, then prompt.** Before the admin prompt ReHome re-hashes its cached copy in `%LOCALAPPDATA%\Rehome\App`, then asks Windows to start it elevated. A same-user process that swaps the file in between gets the approval. The helper's check that the window runs from the same exe compares paths, not contents. Both need a signature check right before launch.
- **The cached copy runs from a user-writable folder.** DLLs planted beside it aren't loaded (see [Elevation](#elevation)), but anything already running as you can replace the exe itself, which is the gap above. Microsoft doesn't treat UAC as a security boundary for this reason; a code-signed build installed per machine would.
- **The checked hash isn't pinned for the install.** The elevated winget checks the download against whatever the manifest says when it installs. If winget-pkgs changes the hash for that same version between the check and the install (it happens for vendors whose download URL always serves the latest build), the new file installs without ReHome's signer check. winget-pkgs reviews that change, but it isn't the file ReHome looked at.
- **Appended data and antivirus.** An exported exe carries its scan after the program bytes. Defender hasn't flagged it here; other products haven't been tried.

Each of these needs a process already running as you, or physical access to the file before you open it. None lets a web page or a stranger on the network reach in.
