using System;
using System.Globalization;
using System.Text;

namespace SpectraEngine.Core.Scene;

/// <summary>The names a running level may give a node.</summary>
public static class NodeName
{
    /// <summary>
    /// The longest name, in characters. A character outside the basic plane,
    /// like most emoji, counts once though a string holds it as two.
    /// </summary>
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

        int length = 0;
        foreach (Rune character in name.EnumerateRunes())
        {
            // A name is shown on one line: in the scene tree, the console, a log.
            if (EndsALine(character))
                return "a name cannot hold a line break or any other control character";

            length++;
        }

        if (length > MaxLength)
            return $"a name is at most {MaxLength} characters long, and this one has {length}";

        // A wire's target is typed by hand, and nobody sees such a space.
        if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]))
            return "a name cannot start or end with a space";

        // TargetNamePattern reads such a target as one of !self, !activator
        // and !caller, and never as a name.
        if (name[0] == '!')
            return "a name cannot start with !, since no wire can reach one that does";

        return null;
    }

    private static bool EndsALine(Rune character) =>
        Rune.IsControl(character)
        || Rune.GetUnicodeCategory(character) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
