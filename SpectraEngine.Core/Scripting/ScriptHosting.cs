using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Entities;
using System;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// How a scene manager gets a level's scripts run: what makes the runtime,
/// where scripts report and where their text comes from. Kept between play
/// sessions, and every session gets a runtime of its own.
/// </summary>
public sealed class ScriptHosting
{
    private bool _saidScriptsAreOff;

    /// <summary>
    /// Makes the runtime for a level that is about to play. Null plays every
    /// level without its scripts. It throws <see cref="InvalidOperationException"/>
    /// when scripts cannot run here, with the reason as the message.
    /// </summary>
    public Func<ScriptRuntimeSetup, IScriptRuntime>? RuntimeFactory { get; set; }

    /// <summary>Where scripts report what they print and how they fail. Null drops it all.</summary>
    public IScriptSink? Sink { get; set; }

    /// <summary>Where a script's text is read from, or null to read it from the level's content.</summary>
    public IScriptSource? Source { get; set; }

    // Null for a level with no script on it, and for one whose scripts cannot
    // run. The second is said once, since a level is played many times.
    internal IScriptRuntime? CreateRuntime(EntityWorld world, IContentSource? content, ILogger logger)
    {
        int scripts = world.Scene.ScriptNodes.Count;
        if (scripts == 0)
            return null;

        if (RuntimeFactory is not { } factory)
            return SayScriptsAreOff(logger, scripts, "The host installed nothing that runs scripts.");

        try
        {
            return factory(new ScriptRuntimeSetup(
                world, Sink ?? SilentScriptSink.Instance, Source ?? new ContentScriptSource(content)));
        }
        catch (InvalidOperationException e)
        {
            return SayScriptsAreOff(logger, scripts, e.Message);
        }
    }

    private IScriptRuntime? SayScriptsAreOff(ILogger logger, int scripts, string reason)
    {
        if (!_saidScriptsAreOff)
        {
            _saidScriptsAreOff = true;
            logger.LogWarning(
                "This level has {Count} script(s) and none of them runs. {Reason}", scripts, reason);
        }

        return null;
    }
}
