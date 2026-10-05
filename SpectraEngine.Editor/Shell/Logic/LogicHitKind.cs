namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What a point in the scene is on.</summary>
public enum LogicHitKind
{
    /// <summary>Empty ground.</summary>
    None,

    /// <summary>A port row, or the dot a wire meets it at.</summary>
    Port,

    /// <summary>A card, away from its ports.</summary>
    Card,

    /// <summary>A wire's label.</summary>
    Label,

    /// <summary>A wire's path.</summary>
    Edge,
}
