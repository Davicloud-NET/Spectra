namespace SpectraEngine.Core.Entities;

/// <summary>
/// The one line an entity says about itself when there is room for one: a
/// label and a value, such as "opening" and "14 of 39 ticks". Filled through
/// <see cref="EntityStateWriter.Headline"/>.
/// </summary>
public sealed class EntityHeadline
{
    /// <summary>What the value is.</summary>
    public string Label { get; private set; } = "";

    /// <summary>The value.</summary>
    public string Value { get; private set; } = "";

    /// <summary>Whether the entity gave a headline since the last <see cref="Clear"/>.</summary>
    public bool IsSet { get; private set; }

    /// <summary>Empties it for the next entity.</summary>
    public void Clear()
    {
        Label = "";
        Value = "";
        IsSet = false;
    }

    internal void Set(string label, string value)
    {
        Label = label;
        Value = value;
        IsSet = true;
    }
}
