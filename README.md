# Rehome

Move your apps to a new Windows PC. Every app is reinstalled from its official source, never copied.

Phase 1 is the scanner: it lists what's installed and how each app can come back.

```
rehome scan              # readable list
rehome scan -o scan.json # JSON for scripts and agents
rehome scan | jq         # piped output is JSON too; the summary goes to stderr
```

## Statuses

| Status | Meaning | Restore |
|---|---|---|
| `Ready` | winget correlated the install to an exact package id | Unattended |
| `Guess` | Same name and publisher in winget or the Store | Needs a yes |
| `Launcher` | A Steam game | Reinstall from the launcher |
| `Manual` | No official package (drivers, OEM tools, delisted apps) | Listed so nothing goes missing |
| `Companion` | Shell extension or component installed by another app | Comes back with its parent |

Frameworks and OS packages are left out. winget installs dependencies on its own.

## Trust model

- Only Microsoft's two catalogs: `winget` (winget-pkgs) and `msstore`. Sources added with `winget source add` are ignored, since anyone can host one.
- The scan records who signed each app on the old machine: the MSIX publisher, or the Authenticode leaf signer of the cached MSI or main exe. Restore (Phase 2) will refuse an installer whose signer doesn't match, on top of winget's SHA256 check.
- A guess never installs without confirmation. Ambiguous matches, prerelease channels and publisher mismatches fall to `Manual`.
- The scan is read-only, runs unelevated, and makes no network calls beyond winget's own catalog queries.

## Build

Needs the .NET 10 SDK and App Installer (winget) 1.29 or newer.

```
dotnet test Rehome.slnx
dotnet publish src/Rehome -c Release
```

Set `REHOME_TRACE=1` to print per-step timings to stderr.

## Layout

```
src/Rehome/Program.cs        CLI and output
src/Rehome/Resolver.cs       Install -> restore plan. Pure, fully unit tested
src/Rehome/Scan/Inventory.cs winget COM API, MSIX facts, signer file lookup
src/Rehome/Scan/Authenticode.cs  WinVerifyTrust
src/Rehome/Scan/Msi.cs       Cached MSI lookup
```
