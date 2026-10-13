using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// A trigger as small as one gets: it senses with its own node's brush and
// reports each touch that starts and ends where scripts print.
internal sealed class SensingEntity(IScriptSink sink) : Entity, ITouchListener
{
    public const string Class = "sensor";

    public const string Touched = "touched";

    public const string Left = "left";

    public void OnTouchStarted(in TouchVisitor visitor) => Say(Touched);

    public void OnTouchEnded(in TouchVisitor visitor) => Say(Left);

    protected internal override void OnSpawn() => World.Touches.Register(this, [Node]);

    private void Say(string what) => sink.Report(new ScriptMessage(ScriptSeverity.Print, what, string.Empty, 0));
}
