using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities;

// What every trigger class keeps: whether it is switched on and how many
// visitors are inside. An object each class owns, not a base class: the
// generator reads only the members a class declares itself.
internal sealed class TriggerState
{
    public bool IsEnabled { get; private set; } = true;

    public int TouchCount { get; private set; }

    public bool IsTouched => TouchCount > 0;

    // The owner's OnSpawn. The brushes are the ones it collected.
    public void Spawn(Entity owner, IReadOnlyList<SceneNode> brushes, bool startDisabled)
    {
        owner.World.Touches.Register(owner, brushes);
        SetEnabled(owner, !startDisabled);
    }

    // Switching off ends every touch, so the owner hears OnTouchEnded before
    // this returns. Switching on with the player inside starts a touch on the
    // next tick.
    public void SetEnabled(Entity owner, bool on)
    {
        IsEnabled = on;
        owner.World.Touches.SetSensing(owner, on);
    }

    public void Toggle(Entity owner) => SetEnabled(owner, !IsEnabled);

    public void Started() => TouchCount++;

    public void Ended() => TouchCount--;
}
