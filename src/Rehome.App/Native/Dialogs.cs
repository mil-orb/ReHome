using System.Runtime.InteropServices;

namespace Rehome.Shell.Native;

/// <summary>
/// Windows' own Open and Save dialogs. With no hook or template, GetOpenFileName and GetSaveFileName show the same
/// Explorer-style dialog every other app uses. Both are modal to the window passed in.
/// </summary>
static unsafe partial class Dialogs
{
    const uint OFN_OVERWRITEPROMPT = 0x2, OFN_HIDEREADONLY = 0x4, OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800,
        OFN_FILEMUSTEXIST = 0x1000, OFN_NOREADONLYRETURN = 0x8000, OFN_EXPLORER = 0x80000;

    // Room for a long path. ExportName keeps names short, but a deep folder can still be long.
    const int MaxPath = 32_768;

    [StructLayout(LayoutKind.Sequential)]
    struct OpenFileName
    {
        public int StructSize;
        public nint Owner, Instance;
        public char* Filter, CustomFilter;
        public int MaxCustomFilter, FilterIndex;
        public char* File;
        public int MaxFile;
        public char* FileTitle;
        public int MaxFileTitle;
        public char* InitialDir, Title;
        public uint Flags;
        public ushort FileOffset, FileExtension;
        public char* DefExt;
        public nint CustData, Hook;
        public char* TemplateName;
        public nint Reserved;
        public int Reserved2;
        public uint FlagsEx;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSaveFileName(ref OpenFileName ofn);

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileName(ref OpenFileName ofn);

    [LibraryImport("comdlg32.dll")]
    private static partial uint CommDlgExtendedError();

    /// <param name="filter">Pairs of label and pattern, as in "Program (*.exe)|*.exe".</param>
    public static string? Save(nint owner, string title, string filter, string defaultExtension, string fileName, string initialDir) =>
        Show(owner, title, filter, defaultExtension, fileName, initialDir,
            OFN_EXPLORER | OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOREADONLYRETURN | OFN_HIDEREADONLY | OFN_NOCHANGEDIR, save: true);

    public static string? Open(nint owner, string filter, string initialDir) =>
        Show(owner, null, filter, null, "", initialDir,
            OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR, save: false);

    static string? Show(nint owner, string? title, string filter, string? defaultExtension, string fileName, string initialDir, uint flags, bool save)
    {
        // The filter is NUL-separated and ends with two NULs.
        var filterText = filter.Replace('|', '\0') + "\0\0";
        var buffer = new char[MaxPath];
        fileName.AsSpan(0, Math.Min(fileName.Length, MaxPath - 1)).CopyTo(buffer);

        fixed (char* file = buffer)
        fixed (char* filterPtr = filterText)
        fixed (char* titlePtr = title)
        fixed (char* ext = defaultExtension)
        fixed (char* dir = initialDir)
        {
            var ofn = new OpenFileName
            {
                StructSize = sizeof(OpenFileName),
                Owner = owner,
                Filter = filterPtr,
                FilterIndex = 1,
                File = file,
                MaxFile = MaxPath,
                InitialDir = dir,
                Title = titlePtr,
                DefExt = ext,
                Flags = flags,
            };

            var ok = save ? GetSaveFileName(ref ofn) : GetOpenFileName(ref ofn);
            if (!ok)
            {
                // Zero means the person cancelled. Anything else is a dialog error, which reads the same to them.
                if (CommDlgExtendedError() is var error and not 0)
                    Log.Write($"file dialog error 0x{error:X}");
                return null;
            }
        }

        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }

    public static int Message(nint owner, string text, string caption, uint flags) => Win32.MessageBox(owner, text, caption, flags);
}
