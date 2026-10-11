using Microsoft.Extensions.Logging;
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

        byte[] source;
        try
        {
            source = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError("The script {Path} could not be read: {Reason}", path, e.Message);
            return false;
        }

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
}
