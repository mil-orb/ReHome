namespace Rehome.Restore;

/// <summary>
/// winget results in words a person can act on. Codes from winget-cli's AppInstallerErrors.h.
/// Anything unknown keeps its hex code so it can be looked up.
/// </summary>
internal static class Errors
{
    public const int HashMismatch = unchecked((int)0x8A150011);
    public const int AlreadyInstalled = unchecked((int)0x8A150061);
    public const int InstallAlreadyInstalled = unchecked((int)0x8A15010D);
    public const int RebootToFinish = unchecked((int)0x8A150109);
    public const int RebootInitiated = unchecked((int)0x8A15010B);
    public const int UacCancelled = unchecked((int)0x800704C7);

    /// <summary>Our own: the installer ran past the time limit and was stopped.</summary>
    public const int TimedOut = unchecked((int)0x800705B4);

    public enum Outcome { Installed, AlreadyThere, NeedsRestart, Failed }

    public static Outcome Classify(int code) => code switch
    {
        0 => Outcome.Installed,
        AlreadyInstalled or InstallAlreadyInstalled => Outcome.AlreadyThere,
        RebootToFinish or RebootInitiated or 3010 or 1641 => Outcome.NeedsRestart,
        _ => Outcome.Failed,
    };

    public static string Describe(int code) => unchecked((uint)code) switch
    {
        0 => "Installed",
        0x8A150011 => "The download didn't match the hash in winget's manifest. Rehome won't install it.",
        0x8A150010 => "No installer for this PC's architecture or Windows version.",
        0x8A150014 or 0x8A150017 or 0x8A15004D => "Not in the catalog anymore.",
        0x8A150016 => "More than one package has this id.",
        0x8A150008 or 0x8A150107 or 0x8A15002E => "Couldn't download. Check your internet connection.",
        0x8A15006D => "The catalog service is unavailable. Try again later.",
        0x8A150056 => "This installer can't run as administrator. Install it from its homepage.",
        0x8A150061 or 0x8A15010D => "Already installed",
        0x8A150109 or 0x8A15010B => "Installed. Restart to finish.",
        0x8A15010A => "Restart Windows, then install again.",
        0x8A150041 or 0x8A150046 => "The package's license terms weren't accepted.",
        0x8A15003A or 0x8A15010F => "Blocked by a policy on this PC.",
        0x8A15001B or 0x8A15001C => "The Microsoft Store is blocked on this PC.",
        0x8A15001E => "The Microsoft Store couldn't install it. Open the Store, sign in, and try again.",
        0x8A15010C => "The installer was cancelled.",
        0x8A150102 => "Another installer is running. Try again when it finishes.",
        0x8A150101 or 0x8A150103 or 0x8A150111 => "The app is open. Close it and try again.",
        0x8A150104 or 0x8A150110 or 0x8A15004C => "A dependency is missing.",
        0x8A150105 => "Not enough disk space.",
        0x8A150106 => "Not enough memory.",
        0x8A150108 => "The installer failed. The vendor's support page may help.",
        0x8A150113 => "This app doesn't support this version of Windows.",
        0x8A15010E => "A newer version is already installed.",
        0x8A15002A => "The package's manifest is invalid.",
        0x8A15005F => "This app needs an install location.",
        0x8A150049 or 0x8A150006 or 0x8A150052 or 0x8A150065 or 0x8A150115 => "The installer failed.",
        0x800704C7 => "Administrator approval was declined.",
        0x800705B4 => "The installer stopped responding and was closed.",
        0x80070070 => "Not enough disk space.",
        0x80072EE7 or 0x80072EFD or 0x80072EE2 => "Couldn't download. Check your internet connection.",
        1602 => "The installer was cancelled.",
        1603 => "The installer failed.",
        1618 => "Another installer is running. Try again when it finishes.",
        1638 => "Another version is already installed.",
        3010 or 1641 => "Installed. Restart to finish.",
        var other => $"The installer failed (0x{other:X8}).",
    };
}
