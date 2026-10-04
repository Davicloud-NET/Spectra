namespace SpectraEngine.Core.Entities;

/// <summary>
/// Stands in for an entity whose class name is not registered. Parses nothing,
/// accepts no input, fires nothing; the authored data stays untouched on the node.
/// </summary>
public sealed class PlaceholderEntity : Entity
{
    /// <inheritdoc/>
    public override bool ParseKeyValue(string key, string value) => false;

    /// <inheritdoc/>
    public override bool AcceptInput(string input, ref EntityInputContext context)
    {
        // Warns once per class name, not per attempt.
        World.ReportPlaceholderInput(this, input);
        return false;
    }
}
