# Security

Rehome runs installers with administrator rights on someone's new PC, so a bug that lets it install the wrong thing matters more than most.

## Reporting

Use [private vulnerability reporting](https://github.com/mil-orb/Rehome/security/advisories/new). Please don't open a public issue. Include the Rehome version, the Windows build, and a scan file or steps that show the problem (remove app names you'd rather keep private).

You'll get a reply within 7 days. Fixes go out as a new release with the advisory linked from its notes.

## What counts

Anything that gets Rehome to:

- install from a source other than winget's community repository or the Microsoft Store
- run an installer whose hash or signer didn't pass the checks in `src/Rehome.Core/Restore/`
- run anything elevated that the person didn't approve in the window
- open a scan whose seal doesn't match without refusing it
- copy a settings file that holds a secret, or write one outside the paths in `SettingsCatalog`
- run code from a scan file (paths, ids, links or icons in it)

Known and documented in [docs/security-model.md](docs/security-model.md#known-gaps), so not a vulnerability on its own:

- The scan seal is a plain SHA-256 with no key. Someone who can edit the exe can recompute it. See "Sealed scans" in the README.
- Rehome isn't code-signed yet, so SmartScreen warns on a downloaded copy.
- A small window exists between Rehome re-hashing its cached copy and Windows starting it elevated.

## Supported versions

Only the latest release gets fixes.
