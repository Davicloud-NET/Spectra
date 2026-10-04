using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// Fires an output once when the map begins, so a level can start its own wiring.
/// </summary>
// The map begins in OnActivate, once every entity has spawned. OnMapSpawn is
// queued behind anything a spawn fired and arrives on the first tick.
// An entity spawned into a running world fires when it joins.
[SpectraEntity("logic_auto", Group = "Logic", Placement = EntityPlacement.Abstract)]
public sealed partial class LogicAuto : Entity
{
    /// <summary>Fired once, when the map begins.</summary>
    [EntityOutput]
    public const string OnMapSpawn = nameof(OnMapSpawn);

    /// <inheritdoc/>
    protected override void OnActivate() => FireOnMapSpawn();
}
