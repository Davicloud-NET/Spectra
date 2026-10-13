using SpectraEngine.Core.Entities;

namespace SpectraEngine.Scripting.Tests;

// An entity whose own outputs are named as the touch of a part is. Poke
// fires them both, with nothing touching anything.
internal sealed class TouchyEntity : Entity
{
    public const string Class = "touchy";

    public static EntitySchema Schema { get; } = new(
        Class,
        placement: EntityPlacement.Brush,
        inputs: EntityPort.Named("Poke"),
        outputs: EntityPort.Named(TouchSignal.Touched, TouchSignal.TouchEnded));

    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        if (input != "Poke")
            return false;

        FireOutput(TouchSignal.Touched);
        FireOutput(TouchSignal.TouchEnded);
        return true;
    }
}
