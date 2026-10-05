using System;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Projects;

/// <summary>
/// A language as a project names it: a short lowercase tag such as
/// <c>en</c>, <c>de</c> or <c>pt-br</c>.
/// </summary>
// A tag is part of file names, so it is kept to what every file system
// spells the same way: lowercase letters, digits and hyphens.
public static class LanguageTag
{
    /// <summary>The language of a project that names none.</summary>
    public const string Default = "en";

    /// <summary>The longest tag there is.</summary>
    public const int MaxLength = 16;

    private const int MaxPartLength = 8;

    /// <summary>
    /// Whether <paramref name="tag"/> is a language tag: two or three
    /// lowercase letters, then any number of parts of lowercase letters and
    /// digits, each after a hyphen.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<char> tag)
    {
        if (tag.Length > MaxLength)
            return false;

        int partLength = 0;
        bool isFirstPart = true;

        foreach (char letter in tag)
        {
            if (letter == '-')
            {
                if (!IsPartLength(partLength, isFirstPart))
                    return false;

                partLength = 0;
                isFirstPart = false;
                continue;
            }

            bool isDigit = letter is >= '0' and <= '9';
            if (letter is not (>= 'a' and <= 'z') && !(isDigit && !isFirstPart))
                return false;

            partLength++;
        }

        return IsPartLength(partLength, isFirstPart);
    }

    /// <summary>
    /// Reads a tag a person typed: the ends are trimmed and capitals become
    /// lowercase. False when what is left is not a tag.
    /// </summary>
    public static bool TryNormalize(ReadOnlySpan<char> text, [NotNullWhen(true)] out string? tag)
    {
        text = text.Trim();

        Span<char> lowered = stackalloc char[MaxLength];
        if (text.Length > MaxLength || text.ToLowerInvariant(lowered) != text.Length
            || !IsValid(lowered[..text.Length]))
        {
            tag = null;
            return false;
        }

        tag = lowered[..text.Length].ToString();
        return true;
    }

    private static bool IsPartLength(int length, bool isFirstPart) =>
        isFirstPart ? length is 2 or 3 : length is >= 1 and <= MaxPartLength;
}
