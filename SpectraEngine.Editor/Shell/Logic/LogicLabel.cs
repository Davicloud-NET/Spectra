using SpectraEngine.Core.Entities;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>The words drawn on a wire. Empty text draws nothing.</summary>
/// <param name="Text">What the label says.</param>
/// <param name="IsMono">Whether it is drawn in the mono font, as a parameter is.</param>
public readonly record struct LogicLabel(string Text, bool IsMono)
{
    /// <summary>No label.</summary>
    public static LogicLabel None => new("", false);

    /// <summary>Whether there is nothing to draw.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Text);

    /// <summary>
    /// What a wire was authored with: its parameter, its delay and how often
    /// it may fire. Empty when it has none of them.
    /// </summary>
    public static LogicLabel Of(EntityConnection wire)
    {
        var parts = new List<string>(3);
        bool hasParameter = !string.IsNullOrEmpty(wire.Parameter);

        if (hasParameter)
            parts.Add($"\"{wire.Parameter}\"");

        if (wire.Delay > 0f)
            parts.Add($"after {Seconds(wire.Delay)} s");

        if (wire.TimesToFire == 1)
            parts.Add("once");
        else if (wire.TimesToFire > 1)
            parts.Add($"{wire.TimesToFire.ToString(CultureInfo.InvariantCulture)} times");

        return new LogicLabel(string.Join(", ", parts), hasParameter);
    }

    /// <summary>The label of several wires drawn as one.</summary>
    public static LogicLabel Count(int wires) =>
        new($"{wires.ToString(CultureInfo.InvariantCulture)} wires", false);

    private static string Seconds(float delay)
    {
        string text = delay.ToString("0.###", CultureInfo.InvariantCulture);

        // A delay under a millisecond would read as none.
        return text == "0" ? delay.ToString(CultureInfo.InvariantCulture) : text;
    }
}
