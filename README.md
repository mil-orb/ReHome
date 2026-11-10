<div align="center">

# Rehome

**Your apps on a fresh Windows, a new laptop or a VM.**

Scan the old PC, carry one exe over on a USB drive, press Install.<br>
Every app comes from winget or the Microsoft Store, and every installer is checked before it runs.

[![ci](https://github.com/mil-orb/Rehome/actions/workflows/ci.yml/badge.svg)](https://github.com/mil-orb/Rehome/actions/workflows/ci.yml)
![Windows 10 2004+ and 11](https://img.shields.io/badge/Windows-10%202004%2B%20%7C%2011-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/start-dark.png">
  <img src="docs/images/start.png" width="620" alt="Rehome's start screen: Scan this PC, or Restore from a scan">
</picture>

</div>

## How it works

<table>
<tr>
<td width="33%" valign="top">

**1. Scan the old PC**

Rehome lists what's installed and matches each app to its exact winget or Store package. Drivers, runtimes and apps Windows ships with are sorted out of the way. Untick anything you don't want.

</td>
<td width="33%" valign="top">

**2. Export one exe**

**Export for new PC** writes a single `Rehome 7-Zip Blender Docker Desktop Git +13 more Installer.exe` with your list sealed inside. About 59 MB. Copy it to a USB drive.

</td>
<td width="33%" valign="top">

**3. Open it on the new PC**

Your apps are listed and ticked. Press Install, approve one admin prompt, and Rehome works through the list. Apps already there are skipped.

</td>
</tr>
<tr>
<td><picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/scan-dark.png"><img src="docs/images/scan.png" alt="The old PC's app list with Export for new PC"></picture></td>
<td><picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/export-dark.png"><img src="docs/images/export.png" alt="Export finished: copy the installer to the new PC"></picture></td>
<td><picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/done-dark.png"><img src="docs/images/done.png" alt="Install results on the new PC: installed, restart needed, one failed with a reason"></picture></td>
</tr>
</table>

Nothing to install on either side. The same exe scans on the old PC and installs on the new one.

## What it checks

Before anything runs as administrator, each installer is downloaded unelevated and checked:

| Check | What it proves |
|---|---|
| Source is `winget` or `msstore` | The package is in Microsoft's own catalogs. Sources added with `winget source add` are ignored. |
| SHA-256 matches the manifest | These are the bytes winget-pkgs reviewed. A mismatch never installs and can't be overridden. |
| Signer matches the old PC's | The same company signed it. A changed or missing signature stops and asks. |
| Exact version, scope and architecture | winget installs the same file that was just checked, and re-checks the hash itself. |

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/review-dark.png">
  <img src="docs/images/review.png" width="520" alt="An unsigned installer stops and asks, showing its download host and SHA-256">
</picture>

An unsigned installer, or one signed by a different company than before, stops and asks with nothing preselected. Apps no official catalog carries (RustDesk, VMware Workstation) are listed with a link to the vendor's page; Rehome never downloads those itself.

The full list of checks, and the gaps that are still open, is in [docs/security-model.md](docs/security-model.md).

## Get it

Rehome isn't published yet. Until the first release, build it:

```powershell
git clone https://github.com/mil-orb/Rehome
cd Rehome
dotnet publish src/Rehome.App -c Release -o out   # out\Rehome.exe
```

Needs the .NET 10 SDK (10.0.300) and Node 22+. Full setup in [BUILD.md](BUILD.md).

Releases will carry `Rehome.exe` and `SHA256SUMS.txt`. To check a download:

```powershell
(Get-FileHash .\Rehome.exe -Algorithm SHA256).Hash   # compare with SHA256SUMS.txt
```

The exe isn't code-signed yet, so Windows SmartScreen shows "Windows protected your PC" for a downloaded copy. A copy carried over on a USB drive doesn't get the warning.

**On the new PC** Rehome needs the WebView2 Runtime (in Windows 11 and current Windows 10) and App Installer, which provides winget. Both come with Windows.

## Command line

`rehome.exe` (from `dotnet publish src/Rehome -c Release`) does the same jobs without a window, for scripts, agents and Windows Sandbox:

```
rehome scan                    # readable list
rehome scan -o scan.json       # sealed JSON for scripts and agents
rehome verify scan.json        # download and check every installer, install nothing
rehome install scan.json       # check, then install; asks about anything unusual
rehome install scan.json --yes # unattended: installs only what passes every check
```

Piped output is JSON lines; the human summary goes to stderr. Exit codes: `0` all good, `1` something failed, `2` bad arguments, `130` stopped with Ctrl+C. Set `REHOME_TRACE=1` for per-step timings.

## Troubleshooting

**"winget isn't available" or "winget isn't responding"**. On a brand-new Windows, App Installer can take a few minutes to register after first sign-in. Open the Microsoft Store, update App Installer, and try again.

**"This scan was changed or damaged after it was saved"**. The file didn't arrive intact, usually a half-finished copy to the USB drive. Export again on the old PC.

**An app shows "Failed" with "The app is open"**. Close it and press Retry.

**Something else**. The log is at `%LOCALAPPDATA%\Rehome\rehome.log` (or **⋯ > About > Show in Explorer**). It lists app names, package ids and each check's result, never settings contents. Attach it to a [bug report](https://github.com/mil-orb/Rehome/issues/new/choose).

## Limits

- Restore installs the latest version in the catalog, not the one the old PC had.
- Each installer downloads twice (once to check, once to install), so a restore uses about twice the bandwidth.
- Settings are listed but not carried yet. That needs the encrypted package, which isn't built.
- Windows only, x64.

## More

- [How it works](docs/how-it-works.md): what a scan includes and leaves out, sealed scans, the portable exe
- [Security model](docs/security-model.md): every check, the elevated helper, known gaps
- [Architecture](docs/architecture.md): projects, files, the UI bridge, scan format, invariants
- [Building](BUILD.md) and [contributing](CONTRIBUTING.md)
- [AGENTS.md](AGENTS.md) for coding assistants working in this repo
- [Reporting a security problem](SECURITY.md)
