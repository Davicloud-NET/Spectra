using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>Starts a program as a process of its own.</summary>
public sealed class ProcessStarter : IProgramStarter
{
    // A stand-in that fails does so at once. One that works may still be
    // running after this, and that is taken as started.
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(3);

    /// <inheritdoc/>
    public bool TryStart(ProgramStart start, out string failure)
    {
        ArgumentNullException.ThrowIfNull(start);

        var info = new ProcessStartInfo(start.FileName)
        {
            UseShellExecute = start.UseShell,
            CreateNoWindow = start.NoWindow,
        };

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

            if (start.ChecksExitCode && process is not null
                && process.WaitForExit(ExitWait) && process.ExitCode != 0)
            {
                failure = $"It ended with exit code {process.ExitCode}.";
                return false;
            }

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
