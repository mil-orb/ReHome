# Agent notes

Rehome: Windows app that scans a PC's apps and reinstalls them on another PC from winget and the Microsoft Store only. C# (.NET 10, WinForms + WebView2 shell) with a React UI in `src/Rehome.App/ui`.

## Commands

```
dotnet test Rehome.slnx                     # all unit tests, no winget calls
npm run typecheck                           # in src/Rehome.App/ui
npm run dev                                 # in src/Rehome.App/ui, fixture data on :5199
dotnet publish src/Rehome.App -c Release    # one portable Rehome.exe
dotnet run --project src/Rehome -- scan     # read-only scan, safe on any PC
```

## Don't

- Run `rehome install`, `rehome verify`, or click Install in the app on a real machine. Both download and run installers. Use a VM or Windows Sandbox.
- Add NuGet or npm packages without being asked. Exact versions only, at least two weeks old, lock files committed.
- Add any download source besides winget's community repository and the Microsoft Store.
- Pass scan-file values through a shell. Everything from a scan is untrusted and goes through `Plan.Parse`.

## Where things are

See "Layout" in README.md. Core logic is in `src/Rehome.Core` and is pure where it can be (Resolver, Categories, Selection, ScanSeal, ExportName), with tests in `tests/Rehome.Tests`.

## Style

- Warnings are errors.
- User-facing text: short, plain, tells the person what to do next. No em dashes.
- Update the README in the same change when behavior or a message changes.
