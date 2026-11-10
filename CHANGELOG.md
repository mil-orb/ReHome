# Changelog

Notable changes, newest first. Versions follow `<Version>` in `Directory.Build.props`.

## Unreleased (0.1.0)

First version.

- Scan an old PC's apps and match each to its exact winget or Microsoft Store package. Drivers, runtimes, launcher games and apps Windows ships with are sorted out of the way.
- Export one portable `Rehome … Installer.exe` with the scan sealed inside (SHA-256), named after the apps it carries.
- Restore on the new PC: every installer downloaded and checked unelevated (catalog hash, signer against the old PC's) before one elevated helper installs the exact checked version.
- Settings for some apps are listed, with a secret check on every file. Carrying them over isn't built yet.
- `rehome` CLI with `scan`, `verify` and `install`, JSON lines when piped.
