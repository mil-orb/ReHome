# AGENTS.md

Guidance for coding assistants (Claude Code, Codex, Copilot, Cursor and others) working in this repository. People will find it useful too.

Rehome scans a Windows PC's apps and reinstalls them on another PC from winget and the Microsoft Store only, checking every installer before it runs as admin. C# on .NET 10 for the host and logic, React 19 + Vite for the UI inside a WebView2 window.

## Safety first

This code installs software as administrator. Some commands here do it for real.

**Never run on the machine you're working on:**

- `rehome install …` or `rehome verify …` (the CLI). `verify` downloads installers; `install` runs them.
- The **Install** button in the desktop app, or anything that sends the `install` bridge message to a real host.
- `winget install`, `winget upgrade`, `winget uninstall`.

**Always safe:**

- `dotnet build`, `dotnet test`, `dotnet publish`
- `rehome scan` and the app's **Scan this PC** (read-only, unelevated)
- **Export for new PC** (writes one file where you choose)
- Opening a scan or an exported exe and looking at the list
- `npm run dev`, `npm run typecheck`, `npm run screenshots` (fixture data, simulated installs)

To test installs, use Windows Sandbox or a VM. See [BUILD.md](BUILD.md#testing-installs). If a task seems to need a real install, stop and ask the person.

## Commands

```powershell
dotnet test Rehome.slnx                         # all tests; must pass before you say you're done
dotnet build Rehome.slnx                        # warnings are errors
dotnet run --project src/Rehome -- scan         # read-only CLI scan, human output
dotnet run --project src/Rehome -- scan -o scan.json  # sealed JSON; /scan*.json is gitignored
dotnet publish src/Rehome.App -c Release -o out # one portable out\Rehome.exe

cd src/Rehome.App/ui
npm run typecheck
npm run dev                                      # http://localhost:5199, simulated host
npm run screenshots                              # rewrites docs/images/*.png
```

Locked restore (`dotnet restore --locked-mode`) is what CI runs. If it fails with NU1004, the SDK isn't 10.0.300; don't regenerate lock files to make it pass.

## Map

Read [docs/architecture.md](docs/architecture.md) for the full file map, the bridge protocol and the scan format. The short version:

- `src/Rehome.Core`: all logic. `Resolver`, `Categories`, `WindowsApps`, `Selection`, `ScanSeal`, `ScanInExe`, `ExportName` are pure and tested.
- `src/Rehome.Core/Restore/Plan.cs`: the single entry for scan files. Everything from a scan is untrusted until it passes here.
- `src/Rehome.App/Bridge.cs`: every message the page can send. `ui/src/bridge.ts` mirrors it and simulates it for dev.
- `src/Rehome.App/Elevated.cs`: the elevated helper and its pipe. Treat changes here as security changes.
- `src/Rehome/`: the CLI, argument parsing only.

## Rules

These hold everywhere. A change that breaks one won't be merged.

1. **Official sources only.** Packages come from `winget` (winget-pkgs) or `msstore`. Never add another source, a direct download URL, or a fallback that fetches from a vendor site.
2. **Scan data is untrusted.** Route it through `Plan.Parse`. Pass values to processes with `ProcessStartInfo.ArgumentList`, never a command string or a shell.
3. **No overrides of winget's checks.** No `--ignore-security-hash`, `--force` or `--override`, anywhere.
4. **The elevated helper stays small.** It starts `WingetCli.Path()` and nothing else, writes no files, and re-validates every field it receives.
5. **No new dependencies** unless the person asks. When they do: exact version, out at least 14 days, lock file committed in the same change, reason in the commit message.
6. **Warnings are errors.** Fix the cause; don't add suppressions.
7. **Settings are data only.** Never add a settings recipe for scripts, profiles, or a whole folder. Every file passes `Secrets.Found`.

## Recipes

**Add a bridge method.** Change four places together: the `switch` and a method in `Bridge.cs`, the `Methods` interface in `ui/src/bridge.ts`, the simulated host in `dev()` in the same file, and the message list in `docs/security-model.md`. The page passes `localId`s, never ids, paths or URLs.

**Add a settings recipe.** Add a `Recipe` to `SettingsCatalog` in `src/Rehome.Core/Settings.cs` keyed by winget id. Exact paths or one `*` in a single segment, with `{AppData}`, `{LocalAppData}`, `{Home}` and similar templates. Add a case to `SettingsTests`.

**Change how an app is grouped.** `Resolver.cs` decides the group; `Categories.cs` decides drivers, runtimes and Windows parts. Both are pure: add an `[InlineData]` case to `ResolverTests` or `ScanTests` first, watch it fail, then change the rule.

**Change user-facing text.** Messages tell the person what to do next in plain words: "Couldn't write there. Pick another place, and check it has about 60 MB free." No error codes, no stack traces, no em dashes. winget failures map to text in `Restore/Errors.cs`.

**Change the UI.** Check it in `npm run dev` at 600×520 (the window's minimum) and 760×820 (its default), light and dark. If it changes what a screenshot shows, run `npm run screenshots` and commit the images.

## Before you finish

- `dotnet test Rehome.slnx` passes, and `npm run typecheck` if you touched the UI.
- README or `docs/` updated in the same change when behavior or a message changed. Search for the old wording.
- Say what you couldn't verify. Anything that needs elevation or a real install usually can't be verified on a dev machine; name it.

## GitHub

- Issues: use `.github/ISSUE_TEMPLATE/`. Blank issues are off.
- Pull requests: fill in every section of `.github/PULL_REQUEST_TEMPLATE.md`, including AI assistance. If a section asks for the person's own words, leave a TODO for them rather than writing it.
- Commits: imperative subject under 72 characters, body says why. Security-relevant changes say what they close and what stays open.
