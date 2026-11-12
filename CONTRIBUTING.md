# Contributing

Thanks for looking. ReHome installs software as administrator on someone's new PC, so the bar for changes is "safe first, then simple, then fast". Bug fixes and better messages are the most useful contributions right now.

## Setup

See [BUILD.md](BUILD.md). The short version, with the .NET SDK 10.0.300 and Node 22+:

```powershell
git clone https://github.com/mil-orb/ReHome
cd ReHome
dotnet test Rehome.slnx                  # first build also runs npm ci for the UI
dotnet run --project src/Rehome.App
```

For UI work, `npm run dev` in `src/Rehome.App/ui` runs the whole UI in a browser with fixture data and a simulated install. Nothing calls winget.

Then read [docs/architecture.md](docs/architecture.md) for the file map and [docs/security-model.md](docs/security-model.md) for what the checks are.

## Don't break your PC

Scanning and exporting are read-only and safe anywhere. `rehome install`, `rehome verify` and the app's Install button download and run real installers. Test those in Windows Sandbox or a VM ([how](BUILD.md#testing-installs)). The unit tests never call winget or touch the registry.

## Reporting bugs

Use the [bug report template](https://github.com/mil-orb/ReHome/issues/new/choose). The most useful things to include are the exact message ReHome showed, which side it happened on (old PC or new), and the relevant lines from `%LOCALAPPDATA%\Rehome\rehome.log`.

Security problems go through [SECURITY.md](SECURITY.md), not public issues.

## Suggesting features

Open a [feature request](https://github.com/mil-orb/ReHome/issues/new?template=feature_request.md) and describe the situation you were in, not only the feature. Some things are out of scope on purpose:

- Installing from anywhere other than winget-pkgs and the Microsoft Store, including direct vendor downloads.
- Copying whole app folders, scripts or anything that runs code on the new PC.
- Running without the per-installer checks, even as an option.

## Making changes

1. One change per pull request. A bug fix and a refactor are two PRs.
2. Write or update a test for anything in `Rehome.Core`. Pure parts (`Resolver`, `Categories`, `Selection`, `ScanSeal`, `ExportName`) are easy to test, so they should be.
3. Run `dotnet test Rehome.slnx`, and `npm run typecheck` if you touched the UI.
4. If the change is visible, check the window at 600×520 and 760×820 in light and dark, and run `npm run screenshots` if a README image changed.
5. Update README or `docs/` in the same PR when behavior or a message changes.

### Rules

- **Official sources only.** No new download paths.
- **Scan files are untrusted.** Everything from a scan goes through `Plan.Parse` and reaches processes as separate arguments.
- **Dependencies.** Avoid new NuGet or npm packages. If one is truly needed: exact version, out for at least two weeks, lock file updated in the same PR, and a sentence on why. Dependabot follows the same two-week wait.
- **Warnings are errors** in every project. Fix the cause.
- **Messages** tell the person what to do next, in plain words. No codes or stack traces in the window.

### AI assistance

AI-assisted contributions are welcome. Say so in the PR: which tool, and roughly how much it did ("wrote the test cases", "most of the code"). Coding assistants should read [AGENTS.md](AGENTS.md) first; it has the commands that are unsafe to run and the recipes for common changes.

The PR description's "Why" should be yours. An assistant can draft the rest.

## Pull requests

ReHome is MIT licensed, and anything you contribute is under the same license.


CI runs the UI typecheck, a locked restore, all tests and a publish on `windows-2025`. The built `ReHome.exe` is attached to the run for 14 days so a reviewer can try it in a VM.

## Releases

Maintainers bump `<Version>` in `Directory.Build.props`, merge, then run the **release** workflow. It leaves a draft release with `ReHome.exe` and `SHA256SUMS.txt` to check before publishing.
