using System;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>
/// Finds Visual Studio Code: <c>code</c> on the path, or one of the places its
/// installer puts it.
/// </summary>
// Pure over the two questions it is handed, so a test can describe a computer.
public static class VisualStudioCodeLocator
{
    private static readonly string[] UnixPlaces =
    [
        "/usr/bin/code",
        "/usr/local/bin/code",
        "/snap/bin/code",
        "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code",
    ];

    /// <summary>The program to start, or null when Visual Studio Code is not there.</summary>
    /// <param name="environment">Reads an environment variable, null when it is not set.</param>
    /// <param name="fileExists">Whether a full path names a file.</param>
    public static string? Find(Func<string, string?> environment, Func<string, bool> fileExists, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(fileExists);

        return isWindows ? FindOnWindows(environment, fileExists) : FindOnUnix(environment, fileExists);
    }

    private static string? FindOnWindows(Func<string, string?> environment, Func<string, bool> fileExists)
    {
        foreach (string folder in Folders(environment("PATH"), ';'))
        {
            if (fileExists(folder + @"\Code.exe"))
                return folder + @"\Code.exe";

            if (!fileExists(folder + @"\code.cmd"))
                continue;

            // The path holds the bin folder, whose code.cmd is a batch file.
            // The program is one folder up, and starting it needs no shell
            // to read the arguments a second time.
            if (ParentOf(folder) is { } home && fileExists(home + @"\Code.exe"))
                return home + @"\Code.exe";

            // A batch file with no program beside it: a package manager's
            // stand-in for one kept somewhere else.
            return folder + @"\code.cmd";
        }

        // The installer's own choices: for one user, then for everybody.
        (string Variable, string Tail)[] places =
        [
            ("LOCALAPPDATA", @"\Programs\Microsoft VS Code\Code.exe"),
            ("ProgramFiles", @"\Microsoft VS Code\Code.exe"),
            ("ProgramFiles(x86)", @"\Microsoft VS Code\Code.exe"),
        ];

        foreach ((string variable, string tail) in places)
        {
            if (environment(variable) is { Length: > 0 } root && fileExists(Trimmed(root) + tail))
                return Trimmed(root) + tail;
        }

        return null;
    }

    private static string? FindOnUnix(Func<string, string?> environment, Func<string, bool> fileExists)
    {
        foreach (string folder in Folders(environment("PATH"), ':'))
        {
            if (fileExists(folder + "/code"))
                return folder + "/code";
        }

        foreach (string place in UnixPlaces)
        {
            if (fileExists(place))
                return place;
        }

        return null;
    }

    private static string[] Folders(string? path, char separator)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        string[] folders = path.Split(
            separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int i = 0; i < folders.Length; i++)
            folders[i] = Trimmed(folders[i].Trim('"'));

        return folders;
    }

    private static string Trimmed(string folder) => folder.TrimEnd('\\', '/');

    private static string? ParentOf(string folder)
    {
        int cut = folder.LastIndexOfAny(['\\', '/']);
        return cut > 0 ? folder[..cut] : null;
    }
}
