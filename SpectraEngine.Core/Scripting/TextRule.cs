using System;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// Whether text comes back as it was written once it has been to a file or
/// to a script, which both hold it as UTF-8.
/// </summary>
public static class TextRule
{
    /// <summary>False for text with half of a surrogate pair in it.</summary>
    public static bool IsWellFormed(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i]))
                continue;

            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                return false;

            i++;
        }

        return true;
    }
}
