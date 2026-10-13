using System;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// The rule for the name of an attribute, which is Roblox's: letters,
/// digits, the underscore, the period, the hyphen and the slash, at most a
/// hundred, and no RBX in front.
/// </summary>
public static class AttributeName
{
    /// <summary>The longest name, in characters.</summary>
    public const int MaxLength = 100;

    /// <summary>The rule as a sentence with no full stop, for whoever refuses a name.</summary>
    public const string Rule =
        "An attribute's name is 1 to 100 letters, digits and any of _ . - /, and does not start with RBX";

    /// <summary>
    /// Whether an attribute can have this name. The letters are those of
    /// ASCII, and a name may start with a digit.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<char> name)
    {
        if (name.Length is 0 or > MaxLength || name.StartsWith("RBX", StringComparison.Ordinal))
            return false;

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '.' or '-' or '/'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a name holds a period, a hyphen or a slash. A map reader from
    /// before map format 7 refuses such a name.
    /// </summary>
    public static bool HasLaterCharacter(ReadOnlySpan<char> name) => name.IndexOfAny('.', '-', '/') >= 0;

    /// <summary>
    /// What to say of a name that breaks the rule: the name, and the rule. A
    /// name longer than the rule allows is not said back.
    /// </summary>
    public static string Refusal(ReadOnlySpan<char> name) =>
        name.Length > MaxLength
            ? $"A name of {name.Length} characters is not a valid attribute name. {Rule}"
            : $"'{name}' is not a valid attribute name. {Rule}";
}
