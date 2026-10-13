using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What makes a signal one the level's queue fires: the node and the output
// behind it. The signal hands every listener it gets on to the level's table.
internal sealed class QueuedSignal(NodeSignals signals, SceneNode node, EntityPort output)
{
    public ParameterType Sends => output.Parameter;

    public void Listen(ScriptConnection connection) => signals.Listen(node, output.Name, connection);

    public void Forget(ScriptConnection connection) => signals.Forget(node, output.Name, connection);
}
