using System;

namespace SpectraEngine.Core.Scene;

/// <summary>The names a running level may give a node.</summary>
public static class NodeName
{
    /// <summary>The longest name, in characters.</summary>
    public const int MaxLength = 100;

    /// <summary>
    /// Why a node cannot have this name, or null when it can: the end of a
    /// sentence, with no full stop.
    /// </summary>
    public static string? WhyRefused(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrWhiteSpace(name))
            return "a name cannot be empty";

        if (name.Length > MaxLength)
            return $"a name is at most {MaxLength} characters long, and this one has {name.Length}";

        // A name is shown on one line: in the scene tree, the console, a log.
        foreach (char character in name)
        {
            if (char.IsControl(character))
                return "a name cannot hold a line break or any other control character";
        }

        return null;
    }
}
