# Building ReHome

## Prerequisites

| Tool | Version | Why |
|---|---|---|
| Windows | 10 2004 (build 19041) or newer, x64 | WinRT APIs, winget |
| .NET SDK | **10.0.300 exactly** | `global.json` pins it with `rollForward: disable`, so `packages.lock.json` resolves the same packages here and in CI. Get it from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Node.js | 22 or newer (CI uses 24) | Builds the UI |
| App Installer (winget) | 1.29 or newer | Scanning and restore talk to it. Already on Windows 11 |
| WebView2 Runtime | any current | The desktop app's window. Already on Windows 11 |
| Visual Studio Build Tools 2022 | "Desktop development with C++" workload | Publishing only. NativeAOT links the exe with MSVC's linker. `dotnet build`, `test` and `run` don't need it |

The Visual Studio IDE isn't needed; VS Code with the recommended extensions (`.vscode/extensions.json`) works.

## Build and test

```powershell
dotnet test Rehome.slnx                    # 243 tests, about 2 s after the first build
dotnet build Rehome.slnx                   # also builds the UI; runs npm ci the first time
dotnet run --project src/Rehome.App        # the desktop app
dotnet run --project src/Rehome -- scan    # the CLI; scan is read-only
```

The App project builds the UI as part of its own build (`BuildUi` target in `Rehome.App.csproj`): `npm ci` if `node_modules` is missing, then `npm run build`, then every file in `ui/dist` is embedded as a resource. Change a `.tsx` file and the next `dotnet build` picks it up.

## Working on the UI

```powershell
cd src/Rehome.App/ui
npm run dev          # http://localhost:5199
npm run typecheck
npm run screenshots  # regenerates docs/images from the dev UI, light and dark
npm run icon         # rebuilds src/Rehome.App/Rehome.ico and src/Rehome/Rehome.ico from assets/
```

`npm run dev` runs without the app. `bridge.ts` notices there's no host and simulates one with `dev/fixture.json` (29 apps, real icons) and a fake install run that includes one unsigned installer to review, one restart and one failure. Nothing calls winget. The fixture isn't in production builds.

`npm run screenshots` starts Vite itself and drives the Edge that ships with Windows over the DevTools protocol (`scripts/edge.mjs`). No extra packages. `npm run icon` uses the same Edge to draw each icon size from the SVGs, so 16 px is drawn at 16 px rather than shrunk from 256. Run it after a visible UI change and commit the PNGs with it.

## Publishing

```powershell
dotnet publish src/Rehome.App -c Release -o out   # out\ReHome.exe, about 8 MB, native, one file
dotnet publish src/Rehome -c Release -o out-cli   # out-cli\rehome.exe plus winget's two files beside it
```

Both exes are compiled with NativeAOT. The `.pdb` files next to them are symbols for crash dumps; don't ship them. The .NET runtime compiled in is pinned in `Directory.Build.targets` (`RehomeRuntimeVersion`), separately from the SDK, so a runtime security fix doesn't wait for an SDK change. Raise it when .NET ships one.

A developer build (`dotnet build`, `dotnet run`) is a normal JIT app with loose DLLs. Code that only breaks under AOT (reflection, built-in COM) shows up as a publish warning, which is an error here, so publish before you push a change to `Rehome.App`.

```powershell
```

Only a published `ReHome.exe` can export. A `dotnet build` output is an exe plus loose DLLs, and exporting it would produce a file that can't start, so the app refuses with a message.

Releases are built by the **release** workflow (Actions tab, Run workflow). It builds from `main`, runs the tests and leaves a draft release with `ReHome.exe` and `SHA256SUMS.txt`. Bump `<Version>` in `Directory.Build.props` first.

## Testing installs

`rehome install`, `rehome verify` and the Install button download and run real installers. Use [Windows Sandbox](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview) or a VM:

1. Publish `ReHome.exe` and export from it on your PC (export is read-only on the old PC).
2. Copy the exported exe into the Sandbox and open it there.

Sandbox starts without winget registered. Install App Installer in it first, or use a Hyper-V VM with a normal Windows install.

## Troubleshooting

**`NU1004: The packages lock file is inconsistent`** in locked restore. Your SDK isn't 10.0.300, so its implicit `Microsoft.NET.ILLink.Tasks` reference differs from the lock file. Install 10.0.300, or when updating the SDK on purpose, bump `global.json` and run `dotnet restore --force-evaluate` to refresh the lock files in the same commit.

**`Interface not registered` from any winget call.** `Microsoft.Management.Deployment.winmd` or `Microsoft.Management.Deployment.dll` isn't beside the running exe. A published exe handles this by relocating itself (see [Portable](docs/how-it-works.md#portable)). For a dev build it should be copied to `bin` automatically; a clean rebuild fixes it if not.

**`'vswhere.exe' is not recognized`** when publishing. The C++ build tools are installed but their setup script can't find `vswhere`. Add `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH` for that shell and publish again.

**`npm ci` fails during `dotnet build`.** Run it by hand in `src/Rehome.App/ui` to see the error. Installs run with lock-file versions only.

**WebView2 window is blank.** Run `npm run build` in `src/Rehome.App/ui`, then rebuild the App project. The window serves only what's embedded in the exe.
