using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>Asks Windows whether it has a program for a kind of file.</summary>
internal static partial class FileAssociations
{
    // Without it Windows answers an unknown extension with its own
    // "How do you want to open this file?" window, which is no program.
    private const uint IgnoreUnknown = 0x400;

    // ASSOCSTR_EXECUTABLE: the program a double click would start.
    private const int Executable = 2;

    /// <summary>Whether a double click on a file with this extension starts a program.</summary>
    /// <param name="extension">With its dot, as in <c>.luau</c>.</param>
    [SupportedOSPlatform("windows")]
    public static bool HasProgramFor(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        try
        {
            // Asked for the length only. S_FALSE with a length is a program.
            uint length = 0;
            int result = AssocQueryString(IgnoreUnknown, Executable, extension, null, 0, ref length);
            return result >= 0 && length > 1;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial int AssocQueryString(
        uint flags, int kind, string association, string? extra, nint output, ref uint length);
}
