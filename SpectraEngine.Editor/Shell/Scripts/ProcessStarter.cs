using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>Starts a program as a process of its own.</summary>
public sealed class ProcessStarter : IProgramStarter
{
    /// <inheritdoc/>
    public bool TryStart(ProgramStart start, out string failure)
    {
        ArgumentNullException.ThrowIfNull(start);

        var info = new ProcessStartInfo(start.FileName) { UseShellExecute = start.UseShell };

        if (start.VerbatimArguments is { } verbatim)
        {
            info.Arguments = verbatim;
        }
        else
        {
            foreach (string argument in start.Arguments)
                info.ArgumentList.Add(argument);
        }

        try
        {
            // Not kept: the program outlives the call and the editor.
            using Process? process = Process.Start(info);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex) when (
            ex is Win32Exception or IOException or InvalidOperationException
                or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // Not there, not a program, or the system has none for the file.
            failure = ex.Message;
            return false;
        }
    }
}
