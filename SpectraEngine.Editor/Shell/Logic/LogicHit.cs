namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What <see cref="LogicScene.HitTest"/> found under a point.</summary>
/// <param name="Kind">What was hit.</param>
/// <param name="Card">The card, for a card or a port.</param>
/// <param name="Port">The port, for a port.</param>
/// <param name="Edge">The edge, for a label or a path.</param>
public readonly record struct LogicHit(
    LogicHitKind Kind,
    LogicSceneCard? Card,
    LogicScenePort? Port,
    LogicSceneEdge? Edge)
{
    /// <summary>Nothing was hit.</summary>
    public static LogicHit None => default;
}
