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

    /// <summary>
    /// The line of code a script's wire comes from: the file's name with no
    /// folder and no ending, then the line. Empty when the file is not known.
    /// </summary>
    /// <param name="file">The file as a content path.</param>
    /// <param name="line">The line, or 0 when it is not known.</param>
    public static LogicLabel Place(string? file, int line)
    {
        string name = FileName(file);
        if (name.Length == 0 || line <= 0)
            return new LogicLabel(name, false);

        return new LogicLabel($"{name}, line {line.ToString(CultureInfo.InvariantCulture)}", false);
    }

    /// <summary>The label of a script's wire that several lines of code make.</summary>
    public static LogicLabel Places(int lines) =>
        new($"{lines.ToString(CultureInfo.InvariantCulture)} lines of code", false);

    // A module is known by its file, so the name is all a label has room for.
    private static string FileName(string? file)
    {
        if (string.IsNullOrEmpty(file))
            return "";

        int folder = file.LastIndexOfAny(['/', '\\']);
        string name = file[(folder + 1)..];
        int ending = name.LastIndexOf('.');
        return ending > 0 ? name[..ending] : name;
    }

    private static string Seconds(float delay)
    {
        string text = delay.ToString("0.###", CultureInfo.InvariantCulture);

        // A delay under a millisecond would read as none.
        return text == "0" ? delay.ToString(CultureInfo.InvariantCulture) : text;
    }
}
