using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;
using System;
using System.IO;

namespace SpectraEngine.Executable;

// Runs one script file with no level and no window: the quickest way to try
// a script, and the proof that the script host works in this build.
internal static class ScriptRun
{
    // False when the script failed: it did not compile, it raised an error
    // nothing caught, or the host stopped it.
    public static bool Run(string path, ILogger logger)
    {
        if (LuauLibrary.FindProblem() is { } problem)
        {
            logger.LogError("Scripts cannot run: {Problem}", problem);
            return false;
        }

        if (ReadText(path, logger) is not { } source)
            return false;

        using LuauHost host = new(new ScriptLogSink(logger));
        host.Seal();

        // Forward slashes, as a content path has them.
        LuauScript? script = host.Load(path.Replace('\\', '/'), source);
        if (script is null)
            return false;

        LuauResume outcome = script.Start();
        if (outcome == LuauResume.Yielded)
        {
            logger.LogWarning(
                "{Prefix} {Script} waits. Nothing resumes a script in a run with no level, so the run ends here.",
                ScriptLogSink.Prefix, script.Name);
        }

        return outcome != LuauResume.Failed;
    }

    // Null when the file cannot be read, which is logged. A script out of a
    // cooked folder has the pack's header in front of its text.
    internal static byte[]? ReadText(string path, ILogger logger)
    {
        try
        {
            return PackedScript.TextOf(File.ReadAllBytes(path), path).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError("The script {Path} could not be read: {Reason}", path, e.Message);
            return null;
        }
    }
}
