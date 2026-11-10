# Security model

Rehome runs installers as administrator on a PC someone just set up, so this is the part that has to be right. This page lists every check between a scan file and a running installer, and the gaps that are still open. To report a problem, see [SECURITY.md](../SECURITY.md).

## Sources and checks

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

The scan itself is read-only, runs unelevated, and makes no network calls beyond winget's own catalog queries. App icons come from each app's installed files (MSIX logo, uninstall entry icon, or main exe), never from the network.

## Elevation

Installs need administrator rights; the window doesn't. On Install, the window starts a second copy of itself elevated (`Rehome.exe --install <pipe> <pid>`), once, and talks to it over a named pipe:

- The pipe name is random and its ACL admits only the current user. It's created first-instance only.
- Each side checks the other's process id (`GetNamedPipeClientProcessId` / `GetNamedPipeServerProcessId`), and the helper checks the window runs from the same exe.
- The helper accepts 1 to 2000 items and re-validates every field against allowlists before building winget arguments. It never parses installers.
- It starts `winget.exe` from App Installer's own package folder under `C:\Program Files\WindowsApps` (owned by SYSTEM), and only after checking it's validly signed by Microsoft Corporation. The `winget` alias in `%LOCALAPPDATA%\Microsoft\WindowsApps` is never used elevated, since the user can write to that folder.
- .NET startup hooks are compiled out, so `DOTNET_STARTUP_HOOKS` can't load a DLL into it.
- It writes no files. Errors go back over the pipe and the window writes them to the log.
- Stop, or closing the window, lets the current installer finish and skips the rest. Closing mid-run asks first.

If you approve the prompt with a different administrator account, the helper can't open the pipe; Rehome says so and nothing installs.

## The desktop app

- The UI is built by Vite and compiled into `Rehome.exe` as resources, served from memory at `https://app.rehome.internal/` under a strict CSP. Nothing on disk can swap it out.
- The page can't navigate, open windows, download, request permissions or reach the network. It sends a fixed set of messages (`boot`, `scan`, `icons`, `export`, `showExport`, `open`, `present`, `install`, `decide`, `stop`, `link`, `about`, `setPreferences`, `showLog`, `log`) and refers to apps by id. The host acts on its own copy of the scan or plan.
- Everything it writes lives in `%LOCALAPPDATA%\Rehome`: `preferences.json` (theme), `rehome.log` (errors plus one line per restored app with its outcome, rotated at 1 MB), `Downloads` (emptied before each verify and after each installer), `App` (the copy it runs from, see [Portable](how-it-works.md#portable)), and the WebView2 cache. Nothing goes in the registry, nothing leaves the PC.
- npm and NuGet versions are pinned exactly, locked, and at least two weeks old when added.

## Known gaps

These are open, written down so nobody has to rediscover them.

- **The scan file is trusted for what to install.** Someone who can edit it can point a vendor link at another https site, or swap a winget id for a different real package. That package still comes from winget and is still hash-checked, but it isn't what you had. The expected signer comes from the same file, so an edited scan can name the swapped package's real signer and pass as Trusted. The seal doesn't help here: it has no key (see [Sealed scans](how-it-works.md#sealed-scans)). The fix is a signed scan, planned with the encrypted settings package.
- **Rehome isn't code-signed.** A downloaded copy gets SmartScreen's "unrecognized app" warning, and anyone who can edit the exe can change what it does. Signing also closes the next two.
- **Hash, then prompt.** Before the admin prompt Rehome re-hashes its cached copy in `%LOCALAPPDATA%\Rehome\App`, then asks Windows to start it elevated. A same-user process that swaps the file in between gets the approval. The helper's check that the window runs from the same exe compares paths, not contents. Both need a signature check right before launch.
- **The cached copy runs from a user-writable folder.** Anything already running as you can plant a DLL there that the elevated copy might load by name. Microsoft doesn't treat UAC as a security boundary for this reason; a code-signed build installed per machine would.
- **Appended data and antivirus.** An exported exe carries its scan after the program bytes. Defender hasn't flagged it here; other products haven't been tried.

Each of these needs a process already running as you, or physical access to the file before you open it. None lets a web page or a stranger on the network reach in.
