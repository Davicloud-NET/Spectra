using Microsoft.Extensions.Logging;

namespace SpectraEngine.Core.Entities;

// What the dispatcher says when a tick's budget or a queue's room runs out.
// The words depend on who ran out: the entities or the scripts.
internal sealed class EntityDispatchLog
{
    private readonly ILogger _logger;

    public EntityDispatchLog(ILogger logger) => _logger = logger;

    public void NeverDue(in EntityEvent made)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
            return;

        _logger.LogDebug(
            "Not queued: {What} at '{TargetName}' waits longer than a level can count, so it would never come.",
            Describe(made), NameOf(made));
    }

    public void FellBehind(bool ofScripts, int budget, in EntityEvent next)
    {
        if (ofScripts && next.Kind == EntityEventKind.ScriptWake)
        {
            _logger.LogWarning(
                "More than {Budget} waiting script threads were due on one tick. The rest go on over the next " +
                "ticks, in the same order, so those scripts run late. Entities are not held up. Said once a level.",
                budget);
            return;
        }

        if (ofScripts)
        {
            _logger.LogWarning(
                "More than {Budget} events of scripts were due on one tick. The rest go on over the next " +
                "ticks, in the same order, so what scripts send and hear runs late. First in line is {What} " +
                "at '{TargetName}'. Entities are not held up. Said once a level.",
                budget, Describe(next), NameOf(next));
            return;
        }

        _logger.LogWarning(
            "More than {Budget} entity events were waiting for one tick. The rest are sent on the next " +
            "ticks, in the same order, so this level's logic runs late. First in line is {What} at " +
            "'{TargetName}'. A loop that waits and multiplies starts like this. Said once a level.",
            budget, Describe(next), NameOf(next));
    }

    public void DroppedWhatADrainMade(bool ofScripts, int budget, in EntityEvent offender, int discarded)
    {
        if (ofScripts)
        {
            _logger.LogError(
                "Scripts made more than {Budget} events in one tick that were due in that same tick: threads " +
                "to run, inputs to send and listeners to call. The {Discarded} that had not gone out were " +
                "dropped, {What} at '{TargetName}' first. This is what a loop of task.defer with no wait " +
                "looks like, or a listener that sets off its own signal.",
                budget, discarded, Describe(offender), NameOf(offender));
            return;
        }

        _logger.LogError(
            "Entity dispatch budget of {Budget} was exhausted in one tick; the cascade was still firing " +
            "{What} at '{TargetName}'. The {Discarded} event(s) it had made and not sent were dropped. " +
            "What was already waiting has run. This is what a zero-delay loop between two entities " +
            "looks like.",
            budget, Describe(offender), NameOf(offender), discarded);
    }

    public void RefusedForRoom(bool ofScripts, int room, in EntityEvent made)
    {
        if (ofScripts && made.Kind == EntityEventKind.ScriptWake)
        {
            _logger.LogError(
                "{Limit} script threads are waiting, so a wait was refused, and so is every wait asked for " +
                "while there is no room. The script that asked gets an error. This is what a loop that " +
                "starts threads and never ends them looks like.",
                room);
            return;
        }

        if (ofScripts)
        {
            _logger.LogError(
                "The scripts of this level have {Limit} events waiting, so {What} at '{TargetName}' was " +
                "dropped, and so is everything a script sends or is to hear while there is no room. A " +
                "script that sends an input gets an error. What entities send is not held up.",
                room, Describe(made), NameOf(made));
            return;
        }

        _logger.LogError(
            "The entity event queue is full at {Limit} events, so {What} at '{TargetName}' was dropped, " +
            "and so is every event made while there is no room. What is already waiting will still run. " +
            "This is what a loop that waits and multiplies looks like.",
            room, Describe(made), NameOf(made));
    }

    private static string Describe(in EntityEvent made) => made.Kind switch
    {
        EntityEventKind.Think => "a think",
        EntityEventKind.ScriptWake => "the wake of a waiting thread",
        EntityEventKind.ScriptSignal => $"a listener's call for output '{made.Output}'",
        _ when made.Output.Length > 0 => $"output '{made.Output}' sending '{made.Input}'",
        _ => $"input '{made.Input}'",
    };

    private static string NameOf(in EntityEvent made) => made.Kind switch
    {
        EntityEventKind.Think or EntityEventKind.ScriptSignal => made.Entity?.TargetName ?? "",
        EntityEventKind.ScriptWake => "a script",
        _ => made.Target?.TargetName ?? made.TargetName,
    };
}
