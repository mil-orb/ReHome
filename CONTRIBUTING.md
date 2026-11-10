# Contributing

## Setup

Windows 10 2004 (build 19041) or newer, the .NET 10 SDK 10.0.300 exactly (`global.json` pins it so lock files match CI), Node 22+, and App Installer (winget) 1.29+.

```
git clone https://github.com/mil-orb/Rehome
cd Rehome
dotnet test Rehome.slnx      # first build runs npm ci for the UI
dotnet run --project src/Rehome.App
```

For UI-only work, `npm run dev` in `src/Rehome.App/ui` serves the UI at http://localhost:5199 with a fixture scan and a fake install run. No winget calls, nothing installed.

## Testing without breaking your PC

Scans are read-only and safe to run anywhere. `rehome install` and the app's Install button really install software. Test those in a VM or Windows Sandbox, never on your daily machine. The unit tests never call winget or touch the registry.

## Rules for changes

- **Official sources only.** Rehome installs from winget's community repository and the Microsoft Store. A change that adds any other download path won't be merged.
- **Dependencies.** Avoid new NuGet or npm packages. If one is truly needed, pin an exact version that's been out at least two weeks, commit the updated lock file, and say why in the PR. Dependabot follows the same two-week wait.
- **Scan files are untrusted.** Anything read from a scan goes through `Plan.Parse`, and reaches winget as separate arguments, never through a shell.
- **Warnings are errors.** `TreatWarningsAsErrors` is on for every project.
- **Messages.** Error text tells the person what to do next, in plain words. No codes, no stack traces in the window.
- **README.** If behavior or a user-facing message changes, update the README in the same PR.

## Pull requests

Keep a PR to one change. CI runs the typecheck, the tests and a publish on `windows-2025`; the built `Rehome.exe` is attached to the run for 14 days so reviewers can try it in a VM.

## Releases

Bump `<Version>` in `Directory.Build.props`, merge, then run the **release** workflow from the Actions tab. It builds, tests, and leaves a draft release with `Rehome.exe` and `SHA256SUMS.txt`. Check the draft, then publish it.
