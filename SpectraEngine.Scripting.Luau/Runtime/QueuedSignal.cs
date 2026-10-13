using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What makes a signal one the level's queue fires: the node and the output
// behind it, or the node and one of the two names of a touch. The signal
// hands every listener it gets on to the level's table.
internal sealed class QueuedSignal(NodeSignals signals, SceneNode node, EntityPort output, bool isTouch = false)
{
    // A class that fires one bad value tends to fire it every tick.
    private bool _hasSaidUnread;

    public ParameterType Sends => output.Parameter;

    // Whether the touch pass fires it, and hands a listener who touches.
    public bool IsTouch => isTouch;

    public void Listen(ScriptConnection connection) => signals.Listen(node, output.Name, connection);

    public void Forget(ScriptConnection connection) => signals.Forget(node, output.Name, connection);

    // Why nothing can listen to it, as the script's error says it: a touch
    // of a world brush can never be told. Null for any other.
    public string? WhyNoListener() => isTouch ? TouchSignal.WhyNone(node) : null;

    // What to tell the first script that listens to an output which fired
    // while the level spawned, when no script ran yet. Null for any other.
    public string? TakeMissedFire() =>
        !isTouch && signals.TakeMissedFire(node, output.Name)
            ? $"{output.Name} of \"{node.Name}\" fired while the level started, before any script ran. " +
              "A script hears what fires after that."
            : null;

    // What to say of a fire whose text the output's type cannot read, the
    // first time there is one. Null after that.
    public string? TakeUnreadFire(string parameter)
    {
        if (_hasSaidUnread)
            return null;

        _hasSaidUnread = true;
        string says = string.Join(", ", EntityClassApi.SentValuesOf(output.Parameter).Select(value => value.Type));
        return $"{output.Name} of \"{node.Name}\" fired with '{parameter}', where its class says {says}. " +
            "A listener gets no value for such a fire.";
    }
}
